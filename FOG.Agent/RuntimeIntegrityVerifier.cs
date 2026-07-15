using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace FOG.Agent;

public sealed class RuntimeIntegrityVerifier(IOptions<AgentOptions> options)
{
    private const string TrustedManifestResource = "FOG.Agent.TrustedRuntimeManifest.json";

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var trustedBytes = await ReadTrustedManifestAsync(cancellationToken);
        var manifestPath = options.Value.ResolveFromBase(options.Value.ManifestPath);
        var externalBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(trustedBytes), SHA256.HashData(externalBytes)))
        {
            throw new InvalidDataException("Runtime manifest integrity check failed.");
        }

        var runtimeRoot = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var manifest = JsonSerializer.Deserialize<RuntimeManifest>(trustedBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Trusted runtime manifest is invalid.");
        if (manifest.Sha256.Count == 0)
        {
            throw new InvalidDataException("Trusted runtime manifest does not contain file hashes.");
        }

        foreach (var entry in manifest.Sha256)
        {
            var fullPath = Path.GetFullPath(Path.Combine(runtimeRoot, entry.Key));
            var relative = Path.GetRelativePath(runtimeRoot, fullPath);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || !File.Exists(fullPath))
            {
                throw new InvalidDataException($"Runtime file is missing or unsafe: {entry.Key}");
            }

            await using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Runtime integrity check failed: {entry.Key}");
            }
        }
    }

    private static async Task<byte[]> ReadTrustedManifestAsync(CancellationToken cancellationToken)
    {
        await using var resource = typeof(RuntimeIntegrityVerifier).Assembly.GetManifestResourceStream(TrustedManifestResource)
            ?? throw new InvalidDataException("Trusted runtime manifest is missing from this build.");
        using var memory = new MemoryStream();
        await resource.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}
