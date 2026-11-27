using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FOG.Agent;

public sealed class PreferredProfileStore
{
    private const int StateVersion = 3;
    private const int MaximumEntries = 16;
    private const int MaximumBytes = 64 * 1024;
    private readonly object _sync = new();
    private readonly string _path;
    private readonly ILogger<PreferredProfileStore> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PreferredProfileStore(
        IOptions<AgentOptions> options,
        ILogger<PreferredProfileStore> logger)
        : this(
            Path.Combine(options.Value.ResolveDataDirectory(), "preferred-profile.json"),
            logger)
    {
    }

    internal PreferredProfileStore(string path, ILogger<PreferredProfileStore> logger)
    {
        _path = Path.GetFullPath(path);
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public PreferredProfileState? TryLoad(string? context = null)
    {
        if (!ValidContext(context)) return null;
        lock (_sync)
        {
            try
            {
                var entries = ReadEntries(out var migrated);
                if (migrated)
                {
                    try { WriteEntries(entries); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogDebug(ex, "Preferred profile migration will be retried later");
                    }
                }
                return entries.FirstOrDefault(entry => string.Equals(entry.Context, context, StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogDebug(ex, "Preferred profile state could not be read");
                return null;
            }
        }
    }

    public void Save(string profileId, string? context = null)
    {
        if (!ValidId(profileId) || !ValidContext(context)) return;
        lock (_sync)
        {
            try
            {
                List<PreferredProfileState> entries;
                try { entries = ReadEntries(out _); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException)
                {
                    _logger.LogDebug(ex, "Invalid preferred profile history will be replaced");
                    entries = new();
                }
                entries.RemoveAll(entry => string.Equals(entry.Context, context, StringComparison.Ordinal));
                entries.Insert(0, new PreferredProfileState(StateVersion, profileId, DateTimeOffset.UtcNow, context));
                WriteEntries(entries);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Preferred profile state could not be saved");
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            try { File.Delete(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Preferred profile state cleanup was deferred");
            }
        }
    }

    public void Clear(string? context)
    {
        if (!ValidContext(context)) return;
        lock (_sync)
        {
            try
            {
                var entries = ReadEntries(out _);
                if (entries.RemoveAll(entry => string.Equals(entry.Context, context, StringComparison.Ordinal)) > 0)
                    WriteEntries(entries);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogDebug(ex, "Preferred profile context cleanup was deferred");
            }
        }
    }

    private List<PreferredProfileState> ReadEntries(out bool migrated)
    {
        migrated = false;
        if (!File.Exists(_path)) return new();
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length is 0 or > MaximumBytes) throw new InvalidDataException("Preferred profile history size is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var json = bytes.AsMemory();
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) json = json[3..];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            throw new InvalidDataException("Preferred profile history version is invalid.");
        List<PreferredProfileState> entries;
        if (number == 2)
        {
            var legacy = document.RootElement.Deserialize<PreferredProfileState>(_json)
                ?? throw new InvalidDataException("Legacy preferred profile state is invalid.");
            entries = new() { legacy with { Version = StateVersion } };
            migrated = true;
        }
        else if (number == StateVersion)
        {
            var history = document.RootElement.Deserialize<ProfileHistory>(_json);
            entries = history?.Entries ?? throw new InvalidDataException("Preferred profile history is invalid.");
        }
        else throw new InvalidDataException("Preferred profile history version is unsupported.");
        var now = DateTimeOffset.UtcNow;
        if (entries.Count > MaximumEntries || entries.Any(entry => entry is null || entry.Version != StateVersion ||
            !ValidId(entry.ProfileId) || !ValidContext(entry.Context) || entry.VerifiedAt == default ||
            entry.VerifiedAt > now.AddMinutes(5)) ||
            entries.Select(entry => entry.Context).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Preferred profile history contains invalid entries.");
        return entries.Where(entry => now - entry.VerifiedAt <= TimeSpan.FromDays(7)).ToList();
    }

    private void WriteEntries(List<PreferredProfileState> entries)
    {
        entries = entries.OrderByDescending(entry => entry.VerifiedAt).Take(MaximumEntries).ToList();
        byte[] bytes;
        while ((bytes = JsonSerializer.SerializeToUtf8Bytes(new ProfileHistory(StateVersion, entries), _json)).Length > MaximumBytes)
            entries.RemoveAt(entries.Count - 1);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(bytes);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { if (created) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Preferred profile temporary file cleanup was deferred");
            }
        }
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool ValidContext(string? context) => context is null ||
        (!string.IsNullOrWhiteSpace(context) && context.Length <= 1024 && !context.Any(char.IsControl));
    private sealed record ProfileHistory(int Version, List<PreferredProfileState> Entries);
}

public sealed record PreferredProfileState(
    int Version,
    string ProfileId,
    DateTimeOffset VerifiedAt,
    string? Context = null);
