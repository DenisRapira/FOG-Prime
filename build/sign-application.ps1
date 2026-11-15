param(
    [Parameter(Mandatory)][string]$FilePath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory)][string]$SignToolPath,
    [uri]$TimestampServer,
    [switch]$AllowDevelopmentSignature
)
$ErrorActionPreference = 'Stop'
$file = (Resolve-Path -LiteralPath $FilePath).Path
$tool = (Resolve-Path -LiteralPath $SignToolPath).Path
if ([IO.Path]::GetExtension($file) -ine '.exe') { throw 'Application signing accepts EXE files only.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
$now = Get-Date
$usage = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
$codeSigningUsage = $false
if ($usage) {
    $decodedUsage = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usage, $usage.Critical)
    $codeSigningUsage = '1.3.6.1.5.5.7.3.3' -in @($decodedUsage.EnhancedKeyUsages | ForEach-Object { $_.Value })
}
if (!$certificate.HasPrivateKey -or $certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now -or
    !$codeSigningUsage) {
    throw 'Expected a current code-signing certificate with an accessible private key.'
}
if (!$AllowDevelopmentSignature) {
    if (!$TimestampServer) { throw 'Production signing requires an HTTPS RFC 3161 timestamp server.' }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        if (!$chain.Build($certificate)) { throw 'Untrusted certificate: development signing must be explicitly enabled.' }
    } finally { $chain.Dispose() }
}
if ($TimestampServer -and ($TimestampServer.Scheme -ne 'https' -or $TimestampServer.UserInfo)) {
    throw 'Timestamp server must be an HTTPS URL without credentials.'
}

$signArguments = @('sign', '/s', 'My', '/sha1', $CertificateThumbprint, '/fd', 'SHA256')
if ($TimestampServer) { $signArguments += @('/tr', $TimestampServer.AbsoluteUri, '/td', 'SHA256') }
$signArguments += $file
$toolOutput = & $tool @signArguments 2>&1
if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed: $toolOutput" }
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.SignerCertificate.Thumbprint -ine $CertificateThumbprint -or
    $signature.Status -in @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible') -or
    (!$AllowDevelopmentSignature -and $signature.Status -ne 'Valid')) {
    throw "Authenticode verification failed: $($signature.Status)"
}
if (!$AllowDevelopmentSignature -and !$signature.TimeStamperCertificate) { throw 'Trusted timestamp is missing.' }

# Verify cryptography separately from Windows trust; development certificates
# intentionally remain outside Root and TrustedPublisher.
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
$stream = [IO.File]::OpenRead($file)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $table = $pe.PEHeaders.PEHeader.CertificateTableDirectory
    if ($table.Size -lt 8 -or $table.Size -gt 1MB -or $table.RelativeVirtualAddress -lt 0 -or
        [long]$table.RelativeVirtualAddress + $table.Size -gt $stream.Length) { throw 'Invalid PE certificate table.' }
    $stream.Position = $table.RelativeVirtualAddress
    $reader = [IO.BinaryReader]::new($stream, [Text.Encoding]::UTF8, $true)
    try {
        $length = $reader.ReadUInt32()
        if ($length -lt 8 -or $length -gt $table.Size -or $reader.ReadUInt16() -ne 0x200 -or $reader.ReadUInt16() -ne 2) {
            throw 'Invalid Authenticode certificate entry.'
        }
        $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
        $cms.Decode($reader.ReadBytes($length - 8))
        $cms.CheckSignature($true)
        if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ine $CertificateThumbprint) {
            throw 'Unexpected Authenticode signer.'
        }
    } finally { $reader.Dispose() }

    # Compare the signed SPC image digest with the PE bytes, independently of
    # certificate-chain trust. Windows SIP owns PE section-gap/overlay semantics;
    # keep the single trailing certificate-table restriction below.
    Add-Type -AssemblyName System.Formats.Asn1
    if ($cms.ContentInfo.ContentType.Value -ne '1.3.6.1.4.1.311.2.1.4') { throw 'Unexpected signed content type.' }
    $asn = [System.Formats.Asn1.AsnReader]::new(
        [ReadOnlyMemory[byte]]::new($cms.ContentInfo.Content), [System.Formats.Asn1.AsnEncodingRules]::BER)
    $content = $asn.ReadSequence()
    $null = $content.ReadEncodedValue()
    $digestInfo = $content.ReadSequence()
    $algorithm = $digestInfo.ReadSequence()
    if ($algorithm.ReadObjectIdentifier() -ne '2.16.840.1.101.3.4.2.1') { throw 'Expected SHA-256 image digest.' }
    if ($algorithm.HasData) { $algorithm.ReadNull() }
    $algorithm.ThrowIfNotEmpty()
    $expectedDigest = $digestInfo.ReadOctetString()
    $digestInfo.ThrowIfNotEmpty()
    $content.ThrowIfNotEmpty()
    $asn.ThrowIfNotEmpty()
    if ($expectedDigest.Length -ne 32) { throw 'Invalid SHA-256 image digest length.' }

    $header = $pe.PEHeaders
    $checksum = [long]$header.PEHeaderStartOffset + 64
    $directoryStart = if ($header.PEHeader.Magic -eq [Reflection.PortableExecutable.PEMagic]::PE32Plus) { 112 } else { 96 }
    $securityDirectory = [long]$header.PEHeaderStartOffset + $directoryStart + 32
    $headerSize = [long]$header.PEHeader.SizeOfHeaders
    if ($header.PEHeader.NumberOfRvaAndSizes -lt 5 -or $checksum -lt 0 -or
        $securityDirectory -lt $checksum + 4 -or $securityDirectory + 8 -gt $headerSize -or
        [long]$table.RelativeVirtualAddress + $table.Size -ne $stream.Length) {
        throw 'Unsupported PE digest layout.'
    }
    if (-not ('FogSigning.PeSipDigest' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FogSigning {
    public static class PeSipDigest {
        [StructLayout(LayoutKind.Sequential)]
        struct Blob { public uint Size; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)]
        struct Algorithm { public IntPtr Oid; public Blob Parameters; }
        [StructLayout(LayoutKind.Sequential)]
        struct Attribute { public IntPtr Oid; public Blob Value; }
        [StructLayout(LayoutKind.Sequential)]
        struct IndirectData { public Attribute Data; public Algorithm Algorithm; public Blob Digest; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Subject {
            public uint Size;
            public IntPtr Type, File;
            [MarshalAs(UnmanagedType.LPWStr)] public string FileName;
            public IntPtr DisplayName;
            public uint Reserved1, Version;
            public IntPtr Provider;
            public Algorithm Algorithm;
            public uint Flags, Encoding, Reserved2, CapiSettings, SecuritySettings, Index, UnionChoice;
            public IntPtr AdditionalInfo, ClientData;
        }
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CryptSIPCreateIndirectData(ref Subject subject, ref uint size, IntPtr data);
        public static byte[] Compute(string path) {
            IntPtr type = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            IntPtr oid = IntPtr.Zero, data = IntPtr.Zero;
            try {
                // Fixed PE SIP, not a generic flat-file or caller-selected provider.
                Marshal.StructureToPtr(new Guid("c689aab8-8e78-11d0-8c47-00c04fc295ee"), type, false);
                oid = Marshal.StringToHGlobalAnsi("2.16.840.1.101.3.4.2.1");
                var subject = new Subject {
                    Size = (uint)Marshal.SizeOf<Subject>(), Type = type,
                    File = new IntPtr(-1), FileName = path,
                    Algorithm = new Algorithm { Oid = oid }, Encoding = 0x10001
                };
                uint size = 0;
                if (!CryptSIPCreateIndirectData(ref subject, ref size, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (size < Marshal.SizeOf<IndirectData>() || size > 1048576)
                    throw new InvalidOperationException("Invalid SIP data size.");
                uint capacity = size;
                data = Marshal.AllocHGlobal((int)capacity);
                if (!CryptSIPCreateIndirectData(ref subject, ref size, data))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (size > capacity || size < Marshal.SizeOf<IndirectData>())
                    throw new InvalidOperationException("Invalid SIP result size.");
                var result = Marshal.PtrToStructure<IndirectData>(data);
                long offset = result.Digest.Data.ToInt64() - data.ToInt64();
                if (result.Digest.Size != 32 || offset < 0 || offset > size - 32)
                    throw new InvalidOperationException("Invalid SIP SHA-256 digest.");
                if (Marshal.PtrToStringAnsi(result.Algorithm.Oid) != "2.16.840.1.101.3.4.2.1")
                    throw new InvalidOperationException("Unexpected SIP digest algorithm.");
                var digest = new byte[32];
                Marshal.Copy(result.Digest.Data, digest, 0, digest.Length);
                return digest;
            } finally {
                if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                if (oid != IntPtr.Zero) Marshal.FreeHGlobal(oid);
                Marshal.FreeHGlobal(type);
            }
        }
    }
}
'@
    }
    $actualDigest = [FogSigning.PeSipDigest]::Compute($file)
    if ([Convert]::ToHexString($actualDigest) -cne [Convert]::ToHexString($expectedDigest)) {
        throw 'Signed Authenticode image digest does not match the executable.'
    }
} finally { $pe.Dispose(); $stream.Dispose() }

[pscustomobject]@{
    subject = $certificate.Subject
    thumbprint = $certificate.Thumbprint
    windowsTrustStatus = $signature.Status.ToString()
    signingMode = if ($AllowDevelopmentSignature) { 'development' } else { 'production' }
    imageDigestVerified = $true
    productionTrusted = !$AllowDevelopmentSignature -and $signature.Status -eq 'Valid' -and $null -ne $signature.TimeStamperCertificate
    timestamped = $null -ne $signature.TimeStamperCertificate
}
