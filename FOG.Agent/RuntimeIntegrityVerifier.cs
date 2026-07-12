using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace FOG.Agent;

public sealed class RuntimeIntegrityVerifier(IOptions<AgentOptions> options)
{
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var manifestPath = options.Value.ResolveFromBase(options.Value.ManifestPath);
        var runtimeRoot = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var manifest = JsonSerializer.Deserialize<RuntimeManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Runtime manifest is invalid.");

        if (manifest.Sha256.Count == 0)
        {
            throw new InvalidDataException("Runtime manifest does not contain trusted file hashes.");
        }

        foreach (var entry in manifest.Sha256)
        {
            var fullPath = Path.GetFullPath(Path.Combine(runtimeRoot, entry.Key));
            if (!fullPath.StartsWith(runtimeRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                throw new InvalidDataException($"Runtime file is missing: {entry.Key}");
            }

            await using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Runtime integrity check failed: {entry.Key}");
            }
        }
    }
}
