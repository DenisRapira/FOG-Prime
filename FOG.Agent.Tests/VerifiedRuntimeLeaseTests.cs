using FOG.Agent;
using FOG.Protocol;
using System.Security.Cryptography;
using System.Text.Json;

internal static class VerifiedRuntimeLeaseTests
{
    internal static void PinsFilesManifestAndAncestorDirectories()
    {
        using var fixture = new Fixture();
        fixture.AssertReleased();
        using (fixture.Acquire())
        {
            foreach (var path in fixture.PinnedPaths)
            {
                Require(File.ReadAllBytes(path).Length > 0, "Pinned file could not be read: " + path);
                ExpectDenied(() => { using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); },
                    "write " + path);
                ExpectDenied(() => File.Delete(path), "delete " + path);
                ExpectRenameDenied(path, directory: false);
            }
            foreach (var directory in new[] { fixture.Runtime, fixture.Metadata, fixture.Package, fixture.Root })
                ExpectRenameDenied(directory, directory: true);
        }
        fixture.AssertReleased();
    }

    internal static void DisposalIsIdempotentAndReleasesAllLocks()
    {
        using var fixture = new Fixture();
        var lease = fixture.Acquire();
        try { lease.Dispose(); lease.Dispose(); }
        finally { lease.Dispose(); }
        fixture.AssertReleased();
        foreach (var path in fixture.PinnedPaths)
        {
            var contents = File.ReadAllBytes(path);
            File.Delete(path);
            File.WriteAllBytes(path, contents);
        }
        using (fixture.Acquire()) { }
        fixture.AssertReleased();
    }

    internal static void RejectsManifestMismatchAndInvalidSizes()
    {
        using var fixture = new Fixture();
        var original = fixture.TrustedBytes.ToArray();
        var modified = original.ToArray();
        modified[0] ^= 1;
        File.WriteAllBytes(fixture.Manifest, modified);
        ExpectRejected<InvalidDataException>(fixture);
        File.WriteAllBytes(fixture.Manifest, original[..^1]);
        ExpectRejected<InvalidDataException>(fixture);
        File.WriteAllBytes(fixture.Manifest, original.Concat(new byte[] { 32 }).ToArray());
        ExpectRejected<InvalidDataException>(fixture);
        fixture.SetManifestBytes(Array.Empty<byte>());
        ExpectRejected<InvalidDataException>(fixture);
        fixture.SetManifestBytes(new byte[1048577]);
        ExpectRejected<InvalidDataException>(fixture);
    }

    internal static void RejectsMissingFilesAndWrongContentHashes()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Runtime, "notes.txt");
        var original = File.ReadAllBytes(path);
        File.WriteAllText(path, "changed after manifest creation");
        ExpectRejected<InvalidDataException>(fixture);
        File.Delete(path);
        ExpectRejected<FileNotFoundException>(fixture);
        File.WriteAllBytes(path, original);
        using (fixture.Acquire()) { }
        fixture.AssertReleased();
    }

    internal static void RejectsUnsafeNamesHashesAndEntryCounts()
    {
        using var fixture = new Fixture();
        var valid = fixture.Hashes.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (var name in new[] { "", " ", ".", "..", "../escape.dll", "..\\escape.dll", "sub/file.dll",
            "sub\\file.dll", "C:escape.dll", "C:\\escape.dll", "file.dll:stream" })
        {
            fixture.Hashes.Clear();
            fixture.Hashes.Add("dummy.exe", valid["dummy.exe"]);
            fixture.Hashes.Add(name, valid["dummy.dll"]);
            fixture.WriteManifest();
            ExpectRejected<InvalidDataException>(fixture);
        }
        foreach (var hash in new string?[] { null, "", new string('a', 63), new string('a', 65), new string('g', 64) })
        {
            fixture.Hashes.Clear();
            fixture.Hashes.Add("dummy.exe", valid["dummy.exe"]);
            fixture.Hashes.Add("dummy.dll", hash!);
            fixture.WriteManifest();
            ExpectRejected<InvalidDataException>(fixture);
        }
        foreach (var count in new[] { 0, 129 })
        {
            fixture.Hashes.Clear();
            for (var index = 0; index < count; index++) fixture.Hashes.Add($"file-{index}.txt", valid["notes.txt"]);
            fixture.WriteManifest();
            ExpectRejected<InvalidDataException>(fixture);
        }
        fixture.SetManifestBytes(JsonSerializer.SerializeToUtf8Bytes(new RuntimeManifest("test", "test", null!)));
        ExpectRejected<InvalidDataException>(fixture);
    }

    internal static void AcceptsEntryCountAndManifestSizeBoundaries()
    {
        using var fixture = new Fixture();
        var hash = fixture.Hashes["notes.txt"];
        fixture.Hashes.Clear();
        fixture.Hashes.Add("notes.txt", hash.ToLowerInvariant());
        fixture.WriteManifest();
        using (fixture.Acquire()) { }
        fixture.AssertReleased();
        for (var index = 1; index < 128; index++) fixture.AddFile($"entry-{index}.txt", new byte[] { 1, 2, 3 });
        fixture.WriteManifest();
        var maximum = new byte[1048576];
        Array.Fill(maximum, (byte)' ');
        fixture.TrustedBytes.CopyTo(maximum, 0);
        fixture.SetManifestBytes(maximum);
        using (fixture.Acquire()) { }
        fixture.AssertReleased();
    }

    internal static void CancellationReleasesAcquiredHandles()
    {
        using var fixture = new Fixture();
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            ExpectCancelled(fixture, cancelled.Token);
        }
        var largePath = Path.Combine(fixture.Runtime, "large.bin");
        using (var stream = File.Create(largePath)) stream.SetLength(64L * 1024 * 1024);
        using (var stream = File.OpenRead(largePath)) fixture.Hashes.Add("large.bin", Convert.ToHexString(SHA256.HashData(stream)));
        fixture.WriteManifest();
        using var cancellation = new CancellationTokenSource();
        var pending = VerifiedRuntimeLease.AcquireAsync(fixture.Runtime, fixture.Manifest, fixture.TrustedBytes, cancellation.Token);
        cancellation.Cancel();
        try
        {
            using var unexpected = pending.GetAwaiter().GetResult();
            throw new InvalidOperationException("Acquisition completed before cancellation could be observed.");
        }
        catch (OperationCanceledException) { }
        fixture.AssertReleased();
    }

    internal static void RejectsExistingWriteSharedHandlesWithoutLeaks()
    {
        using var fixture = new Fixture();
        foreach (var path in fixture.PinnedPaths)
        {
            using (var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var rejected = false;
                try { using var unexpected = fixture.Acquire(); }
                catch (IOException) { rejected = true; }
                Require(rejected, "Lease accepted a previously opened writable handle: " + path);
            }
            fixture.AssertReleased();
        }
    }

    internal static void RejectsFixtureLocalReparsePoints()
    {
        using var fixture = new Fixture();
        var directoryLink = Path.Combine(fixture.Package, "runtime-link");
        try { fixture.Links.Add(Directory.CreateSymbolicLink(directoryLink, fixture.Runtime)); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Console.WriteLine("SKIP: fixture-local reparse tests; symbolic link creation unavailable: " + ex.GetType().Name);
            return;
        }
        ExpectRejected<InvalidDataException>(fixture, runtime: directoryLink);
        var ancestorRuntime = Path.Combine(directoryLink, "child");
        Directory.CreateDirectory(Path.Combine(fixture.Runtime, "child"));
        ExpectRejected<InvalidDataException>(fixture, runtime: ancestorRuntime);
        var metadataLink = Path.Combine(fixture.Package, "metadata-link");
        fixture.Links.Add(Directory.CreateSymbolicLink(metadataLink, fixture.Metadata));
        ExpectRejected<InvalidDataException>(fixture, manifest: Path.Combine(metadataLink, Path.GetFileName(fixture.Manifest)));
        var manifestLink = Path.Combine(fixture.Metadata, "manifest-link.json");
        fixture.Links.Add(File.CreateSymbolicLink(manifestLink, fixture.Manifest));
        ExpectRejected<InvalidDataException>(fixture, manifest: manifestLink);
        var fileLink = Path.Combine(fixture.Runtime, "linked.dll");
        fixture.Links.Add(File.CreateSymbolicLink(fileLink, Path.Combine(fixture.Runtime, "dummy.dll")));
        fixture.Hashes.Add("linked.dll", fixture.Hashes["dummy.dll"]);
        fixture.WriteManifest();
        ExpectRejected<InvalidDataException>(fixture);
    }

    internal static void FileReadLeaseRejectsOpenReadAfterDisposal()
    {
        using var fixture = new Fixture();
        var lease = FileReadLease.LockDirectories(fixture.Runtime, fixture.Metadata);
        try
        {
            var stream = lease.OpenRead(Path.Combine(fixture.Runtime, "dummy.exe"));
            Require(stream.ReadByte() >= 0, "Low-level pinned stream was not readable.");
            lease.Dispose();
            lease.Dispose();
            foreach (var path in new[] { Path.Combine(fixture.Runtime, "dummy.exe"), Path.Combine(fixture.Runtime, "absent.dll") })
            {
                var rejected = false;
                try { using var unexpected = lease.OpenRead(path); }
                catch (ObjectDisposedException) { rejected = true; }
                Require(rejected, "Disposed FileReadLease accepted OpenRead: " + path);
            }
        }
        finally { lease.Dispose(); }
        fixture.AssertReleased();
    }

    private static void ExpectCancelled(Fixture fixture, CancellationToken token)
    {
        var cancelled = false;
        try { using var unexpected = fixture.Acquire(token); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "Pre-cancelled acquisition succeeded.");
        fixture.AssertReleased();
    }

    private static void ExpectRejected<T>(Fixture fixture, string? runtime = null, string? manifest = null) where T : Exception
    {
        var rejected = false;
        try { using var unexpected = VerifiedRuntimeLease.AcquireAsync(runtime ?? fixture.Runtime,
            manifest ?? fixture.Manifest, fixture.TrustedBytes, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (T) { rejected = true; }
        Require(rejected, "Expected lease rejection: " + typeof(T).Name);
        fixture.AssertReleased();
    }

    private static void ExpectDenied(Action operation, string description)
    {
        var denied = false;
        try { operation(); }
        catch (IOException) { denied = true; }
        catch (UnauthorizedAccessException) { denied = true; }
        Require(denied, "Pinned operation was allowed: " + description);
    }

    private static void ExpectRenameDenied(string path, bool directory)
    {
        var destination = path + ".renamed";
        try
        {
            if (directory) Directory.Move(path, destination);
            else File.Move(path, destination);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (directory) Directory.Move(destination, path);
        else File.Move(destination, path);
        throw new InvalidOperationException("Pinned rename was allowed: " + path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "fog-verified-lease-" + Guid.NewGuid().ToString("N"));
        internal string Package => Path.Combine(Root, "package");
        internal string Runtime => Path.Combine(Package, "runtime");
        internal string Metadata => Path.Combine(Package, "metadata");
        internal string Manifest => Path.Combine(Metadata, "runtime.manifest.json");
        internal Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        internal List<FileSystemInfo> Links { get; } = new();
        internal byte[] TrustedBytes { get; private set; } = Array.Empty<byte>();
        internal IEnumerable<string> PinnedPaths => new[] { Manifest }.Concat(
            Directory.GetFiles(Runtime).Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0));

        internal Fixture()
        {
            try
            {
                Directory.CreateDirectory(Runtime);
                Directory.CreateDirectory(Metadata);
                foreach (var name in new[] { "dummy.exe", "dummy.dll", "dummy.sys", "notes.txt" })
                    AddFile(name, System.Text.Encoding.ASCII.GetBytes("test fixture only: " + name));
                WriteManifest();
            }
            catch { Dispose(); throw; }
        }

        internal void AddFile(string name, byte[] contents)
        {
            File.WriteAllBytes(Path.Combine(Runtime, name), contents);
            Hashes.Add(name, Convert.ToHexString(SHA256.HashData(contents)));
        }

        internal void WriteManifest() => SetManifestBytes(JsonSerializer.SerializeToUtf8Bytes(
            new RuntimeManifest("test", "fixture-only", Hashes), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        internal void SetManifestBytes(byte[] bytes)
        {
            TrustedBytes = bytes;
            File.WriteAllBytes(Manifest, bytes);
        }

        internal IDisposable Acquire(CancellationToken token = default) =>
            VerifiedRuntimeLease.AcquireAsync(Runtime, Manifest, TrustedBytes, token).GetAwaiter().GetResult();

        internal void AssertReleased()
        {
            foreach (var path in PinnedPaths)
            {
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            foreach (var directory in new[] { Runtime, Metadata, Package, Root })
            {
                Directory.Move(directory, directory + ".released");
                Directory.Move(directory + ".released", directory);
            }
        }

        public void Dispose()
        {
            for (var index = Links.Count - 1; index >= 0; index--) Links[index].Delete();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
