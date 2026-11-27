using FOG.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

internal static class ProfileMemoryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void RoamingPreservesEachNetworkAndDefault()
    {
        using var fixture = new Fixture();
        fixture.Store.Save("default-profile");
        fixture.Store.Save("profile-a", "network-a");
        fixture.Store.Save("profile-b", "network-b");
        var before = File.ReadAllBytes(fixture.Path);
        Require(fixture.Store.TryLoad("network-missing") is null, "Unknown network reused another context.");
        Require(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "Context miss rewrote or deleted history.");
        Require(fixture.Store.TryLoad("network-a")?.ProfileId == "profile-a", "A-B-A roaming lost network A.");
        Require(fixture.Store.TryLoad("network-b")?.ProfileId == "profile-b", "Network B preference was overwritten.");
        Require(fixture.Store.TryLoad()?.ProfileId == "default-profile", "Network preferences overwrote default context.");
        Require(fixture.Store.TryLoad("NETWORK-A") is null, "Context matching became case insensitive.");
        var reloaded = fixture.NewStore();
        Require(reloaded.TryLoad("network-a")?.ProfileId == "profile-a", "History did not survive store recreation.");
        fixture.Store.Save("profile-a-new", "network-a");
        Require(fixture.Store.TryLoad("network-a")?.ProfileId == "profile-a-new", "Same-context save did not replace preference.");
        Require(fixture.EntryCount == 3, "Same-context save duplicated an entry.");
    }

    internal static void FailedContextInvalidationIsIsolated()
    {
        using var fixture = new Fixture();
        fixture.Store.Save("default-profile");
        fixture.Store.Save("profile-a", "network-a");
        fixture.Store.Save("profile-b", "network-b");
        fixture.Store.Clear("network-a");
        Require(fixture.Store.TryLoad("network-a") is null, "Failed context was retained.");
        Require(fixture.Store.TryLoad("network-b")?.ProfileId == "profile-b", "Clearing A erased B.");
        Require(fixture.Store.TryLoad()?.ProfileId == "default-profile", "Clearing A erased default context.");
        var before = File.ReadAllBytes(fixture.Path);
        fixture.Store.Clear("unknown");
        Require(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "Clearing absent context rewrote history.");
        fixture.Store.Clear(null);
        Require(fixture.Store.TryLoad() is null && fixture.Store.TryLoad("network-b") is not null,
            "Explicit null-context clear erased other networks.");
        fixture.Store.Clear();
        Require(!File.Exists(fixture.Path) && fixture.Store.TryLoad("network-b") is null, "Clear() did not clear all history.");
    }

    internal static void EvictsOldestAndBoundsSerializedBytes()
    {
        using var fixture = new Fixture();
        fixture.WriteHistory(Enumerable.Range(0, 16).Select(index => State("profile-" + index, "network-" + index,
            DateTimeOffset.UtcNow.AddHours(-16 + index))).ToArray());
        fixture.Store.Save("new-profile", "new-network");
        Require(fixture.EntryCount == 16, "History exceeded entry bound or evicted too many entries.");
        Require(fixture.Store.TryLoad("network-0") is null, "Oldest verification was not evicted.");
        Require(fixture.Store.TryLoad("network-1") is not null && fixture.Store.TryLoad("new-network") is not null,
            "Eviction lost newer contexts.");
        fixture.Store.Clear();
        for (var index = 0; index < 20; index++)
            fixture.Store.Save("profile-" + index, index + new string('\u4e2d', 1020));
        Require(new FileInfo(fixture.Path).Length <= 65536 && fixture.EntryCount <= 16, "Serialized history exceeded its bounds.");
        Require(fixture.Store.TryLoad("19" + new string('\u4e2d', 1020))?.ProfileId == "profile-19",
            "Byte-budget eviction discarded the newly saved context.");
        fixture.AssertNoTemporaryFiles();
    }

    internal static void ExpiryAndClockValidationPreserveOtherContexts()
    {
        using var fixture = new Fixture();
        var now = DateTimeOffset.UtcNow;
        fixture.WriteHistory(State("expired", "old", now.AddDays(-8)), State("fresh", "active", now.AddMinutes(-1)),
            State("near-future", "clock-tolerance", now.AddMinutes(4)));
        Require(fixture.Store.TryLoad("old") is null, "Expired context was reused.");
        Require(fixture.Store.TryLoad("active")?.ProfileId == "fresh", "Expired A erased fresh B.");
        Require(fixture.Store.TryLoad("clock-tolerance") is not null, "Existing five-minute clock tolerance changed.");
        fixture.Store.Save("new", "new-context");
        Require(fixture.EntryCount == 3, "Save did not prune expired entries.");
        fixture.WriteHistory(State("future", "future", now.AddMinutes(6)));
        Require(fixture.Store.TryLoad("future") is null, "Future verification time was accepted.");
        fixture.WriteHistory(State("unset", "unset", default(DateTimeOffset)));
        Require(fixture.Store.TryLoad("unset") is null, "Default verification time was accepted.");
    }

    internal static void MigratesValidVersionTwoWithoutContextLoss()
    {
        using var fixture = new Fixture();
        var legacy = new PreferredProfileState(2, "legacy-a", DateTimeOffset.UtcNow.AddHours(-1), "network-a");
        File.WriteAllBytes(fixture.Path, JsonSerializer.SerializeToUtf8Bytes(legacy, Json));
        Require(fixture.Store.TryLoad("network-b") is null, "Legacy preference leaked to another network.");
        Require(fixture.Store.TryLoad("network-a")?.ProfileId == "legacy-a", "Legacy A was lost on a B lookup.");
        using (var migrated = JsonDocument.Parse(File.ReadAllBytes(fixture.Path)))
            Require(migrated.RootElement.GetProperty("version").GetInt32() == 3, "Legacy file was not migrated.");
        fixture.Store.Save("profile-b", "network-b");
        Require(fixture.Store.TryLoad("network-a")?.VerifiedAt == legacy.VerifiedAt, "Migration reset verification age.");
        Require(fixture.NewStore().TryLoad("network-b")?.ProfileId == "profile-b", "New context was not persisted after migration.");
        File.WriteAllBytes(fixture.Path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(
            JsonSerializer.SerializeToUtf8Bytes(legacy with { Context = null }, Json)).ToArray());
        Require(fixture.Store.TryLoad()?.ProfileId == "legacy-a", "Default-context legacy migration failed.");
        File.WriteAllBytes(fixture.Path, JsonSerializer.SerializeToUtf8Bytes(legacy with { VerifiedAt = DateTimeOffset.UtcNow.AddDays(-8) }, Json));
        Require(fixture.Store.TryLoad("network-a") is null, "Expired legacy entry was revived by migration.");
        File.WriteAllBytes(fixture.Path, JsonSerializer.SerializeToUtf8Bytes(legacy, Json));
        using (var held = File.Open(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Require(fixture.Store.TryLoad("network-a")?.ProfileId == "legacy-a", "Deferred migration hid a valid legacy preference.");
        Require(fixture.Store.TryLoad("network-a")?.ProfileId == "legacy-a", "Migration retry lost the legacy preference.");
        fixture.AssertNoTemporaryFiles();
    }

    internal static void RejectsCorruptionAndInvalidInputSafely()
    {
        using var fixture = new Fixture();
        foreach (var body in new[] { "", "{", "null", "[]", "{}", "{\"version\":\"3\"}",
            "{\"version\":999,\"entries\":[]}", "{\"version\":3,\"entries\":null}",
            "{\"version\":3,\"entries\":[null]}", new string(' ', 65537) })
        {
            File.WriteAllText(fixture.Path, body);
            Require(fixture.Store.TryLoad("network-a") is null, "Malformed history was accepted.");
            fixture.Store.Clear("network-a");
            fixture.Store.Save("recovered", "network-a");
            Require(fixture.Store.TryLoad("network-a")?.ProfileId == "recovered", "Corrupt history could not be replaced safely.");
            fixture.AssertNoTemporaryFiles();
        }
        foreach (var invalid in new[] { State("bad id", "a"), State(new string('a', 129), "a"),
            State("valid", " "), State("valid", "bad\ncontext"), State("valid", new string('a', 1025)),
            State("valid", "a") with { Version = 2 } })
        {
            fixture.WriteHistory(invalid);
            Require(fixture.Store.TryLoad(invalid.Context) is null, "Invalid stored identity was accepted.");
        }
        fixture.WriteHistory(State("one", "same"), State("two", "same"));
        Require(fixture.Store.TryLoad("same") is null, "Duplicate contexts were accepted.");
        fixture.WriteHistory(Enumerable.Range(0, 17).Select(index => State("profile", "context-" + index)).ToArray());
        Require(fixture.Store.TryLoad("context-0") is null, "Over-count history was accepted.");
        fixture.Store.Clear();
        fixture.Store.Save("preserved", "safe");
        var before = File.ReadAllBytes(fixture.Path);
        foreach (var id in new string?[] { null, "", " ", "bad/id", "bad\\id", "bad:id", new string('a', 129) })
            fixture.Store.Save(id!, "safe");
        foreach (var context in new[] { " ", "bad\ncontext", new string('a', 1025) })
        {
            fixture.Store.Save("bad", context);
            fixture.Store.Clear(context);
            Require(fixture.Store.TryLoad(context) is null, "Invalid query context was accepted.");
        }
        Require(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "Invalid API input damaged valid history.");
    }

    internal static void FailedAtomicReplaceCleansOnlyItsOwnTemporaryFile()
    {
        using var fixture = new Fixture();
        fixture.Store.Save("preserved", "a");
        var before = File.ReadAllBytes(fixture.Path);
        var unrelated = fixture.Path + ".other-operation.tmp";
        File.WriteAllText(unrelated, "not owned by this save");
        using (var held = File.Open(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            fixture.Store.Save("replacement", "b");
        Require(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "Failed replacement damaged original file.");
        Require(File.Exists(unrelated), "Save deleted another operation's temporary file.");
        Require(Directory.GetFiles(fixture.Directory, "*.tmp").Length == 1, "Failed save leaked its unique temporary file.");
        File.Delete(unrelated);
        fixture.Store.Save("replacement", "b");
        Require(fixture.Store.TryLoad("a")?.ProfileId == "preserved" && fixture.Store.TryLoad("b")?.ProfileId == "replacement",
            "Successful atomic replacement lost a context.");
        fixture.AssertNoTemporaryFiles();
    }

    private static PreferredProfileState State(string id, string? context, DateTimeOffset? time = null) =>
        new(3, id, time ?? DateTimeOffset.UtcNow.AddMinutes(-1), context);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fog-profile-memory-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Directory, "preferred-profile.json");
        internal PreferredProfileStore Store { get; }
        internal Fixture() => Store = NewStore();
        internal PreferredProfileStore NewStore() => new(Path, NullLogger<PreferredProfileStore>.Instance);
        internal int EntryCount
        {
            get
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(Path));
                return document.RootElement.GetProperty("entries").GetArrayLength();
            }
        }
        internal void WriteHistory(params PreferredProfileState[] entries) => File.WriteAllBytes(Path,
            JsonSerializer.SerializeToUtf8Bytes(new { version = 3, entries }, Json));
        internal void AssertNoTemporaryFiles() => Require(System.IO.Directory.GetFiles(Directory, "*.tmp").Length == 0,
            "Preference persistence leaked temporary files.");
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
