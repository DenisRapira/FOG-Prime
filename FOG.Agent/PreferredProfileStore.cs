using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FOG.Agent;

public sealed class PreferredProfileStore
{
    private const int StateVersion = 2;
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
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<PreferredProfileState>(File.ReadAllText(_path), _json);
            if (state is null ||
                state.Version != StateVersion ||
                string.IsNullOrWhiteSpace(state.ProfileId) ||
                state.VerifiedAt == default ||
                state.VerifiedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
                DateTimeOffset.UtcNow - state.VerifiedAt > TimeSpan.FromDays(7) ||
                !string.Equals(state.Context, context, StringComparison.Ordinal))
            {
                Clear();
                return null;
            }

            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex, "Preferred profile state could not be read");
            Clear();
            return null;
        }
    }

    public void Save(string profileId, string? context = null)
    {
        try
        {
            var state = new PreferredProfileState(StateVersion, profileId, DateTimeOffset.UtcNow, context);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, _json));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Preferred profile state could not be saved");
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Preferred profile state cleanup was deferred");
        }
    }
}

public sealed record PreferredProfileState(
    int Version,
    string ProfileId,
    DateTimeOffset VerifiedAt,
    string? Context = null);
