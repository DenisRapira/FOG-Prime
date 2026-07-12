using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FOG.Agent;

public sealed class ProfileCatalog
{
    private readonly IReadOnlyList<EngineProfile> _profiles;

    public ProfileCatalog(IOptions<AgentOptions> options)
    {
        var path = options.Value.ResolveFromBase(options.Value.ProfilesPath);
        _profiles = JsonSerializer.Deserialize<EngineProfile[]>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?.OrderBy(profile => profile.Priority)
            .ToArray()
            ?? throw new InvalidDataException("Engine profile catalog is invalid.");

        if (_profiles.Count == 0 || _profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Id) || profile.Arguments.Count == 0))
        {
            throw new InvalidDataException("Engine profile catalog is empty or unsafe.");
        }
    }

    public IReadOnlyList<EngineProfile> All => _profiles;
}
