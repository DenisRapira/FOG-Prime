using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace FOG.Agent;

public sealed class ProfileCatalog
{
    private const string TrustedProfilesResource = "FOG.Agent.TrustedProfiles.json";
    private readonly IReadOnlyList<EngineProfile> _profiles;

    public ProfileCatalog(IOptions<AgentOptions> options)
    {
        using var resource = typeof(ProfileCatalog).Assembly.GetManifestResourceStream(TrustedProfilesResource)
            ?? throw new InvalidDataException("Trusted Engine profiles are missing.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var trustedBytes = memory.ToArray();

        var externalPath = options.Value.ResolveFromBase(options.Value.ProfilesPath);
        var externalBytes = File.ReadAllBytes(externalPath);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(trustedBytes), SHA256.HashData(externalBytes)))
        {
            throw new InvalidDataException("Engine profile integrity check failed.");
        }

        _profiles = JsonSerializer.Deserialize<EngineProfile[]>(trustedBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
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
