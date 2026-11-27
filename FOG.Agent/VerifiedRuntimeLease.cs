using FOG.Protocol;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

internal sealed class VerifiedRuntimeLease : IDisposable
{
    private FileReadLease? _files;

    internal static async Task<VerifiedRuntimeLease> AcquireAsync(
        string runtimeRoot, string manifestPath, byte[] trustedBytes, CancellationToken cancellationToken)
    {
        var lease = new VerifiedRuntimeLease();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimeRoot = Path.GetFullPath(runtimeRoot);
            manifestPath = Path.GetFullPath(manifestPath);
            lease._files = FileReadLease.LockDirectories(runtimeRoot, Path.GetDirectoryName(manifestPath)!);

            var manifestStream = lease._files.OpenRead(manifestPath);
            if (trustedBytes.Length is 0 or > 1048576 || manifestStream.Length != trustedBytes.Length)
                throw new InvalidDataException("Runtime manifest size check failed.");
            var actualManifestHash = await SHA256.HashDataAsync(manifestStream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(trustedBytes), actualManifestHash))
                throw new InvalidDataException("Runtime manifest integrity check failed.");

            var manifest = JsonSerializer.Deserialize<RuntimeManifest>(
                Encoding.UTF8.GetString(trustedBytes).TrimStart('\uFEFF'), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Trusted runtime manifest is invalid.");
            if (manifest.Sha256 is null || manifest.Sha256.Count is 0 or > 128)
                throw new InvalidDataException("Trusted runtime file count is invalid.");
            foreach (var entry in manifest.Sha256)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key != Path.GetFileName(entry.Key) ||
                    entry.Key is "." or ".." || entry.Key.IndexOfAny(new[] { ':', '/', '\\' }) >= 0 ||
                    entry.Value is null || entry.Value.Length != 64 || !entry.Value.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Trusted runtime entry is unsafe.");
                var stream = lease._files.OpenRead(Path.Combine(runtimeRoot, entry.Key));
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Runtime integrity check failed: {entry.Key}");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _files, null)?.Dispose();
    }
}
