using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

internal static class GameCatalog
{
    public static IReadOnlyList<GameDefinition> All { get; } = Load();
    public static string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(All))));

    internal static IReadOnlyList<GameDefinition> Parse(string json)
    {
        if (json.Length > 32768) throw new InvalidDataException("Game catalog is too large.");
        var games = JsonSerializer.Deserialize<GameDefinition[]>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (games is null || games.Length is 0 or > 16 ||
            games.Any(game => game is null || string.IsNullOrWhiteSpace(game.Id) || game.Id.Length > 48 ||
                game.Id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
                string.IsNullOrWhiteSpace(game.Name) || game.Name.Length > 64 || game.Name.Any(char.IsControl) ||
                game.Domains is null || game.Domains.Count is 0 or > 32 ||
                game.Domains.Any(domain => string.IsNullOrWhiteSpace(domain) || domain.Length > 253 ||
                    domain != domain.ToLowerInvariant() || !domain.Contains('.') || domain.EndsWith('.') ||
                    Uri.CheckHostName(domain) != UriHostNameType.Dns) ||
                game.Domains.Distinct(StringComparer.OrdinalIgnoreCase).Count() != game.Domains.Count) ||
            games.Select(game => game.Id).Distinct(StringComparer.Ordinal).Count() != games.Length)
            throw new InvalidDataException("Game catalog contains unsafe or duplicate entries.");
        return Array.AsReadOnly(games.Select(game => game with
        {
            Domains = Array.AsReadOnly(game.Domains.ToArray())
        }).ToArray());
    }

    private static IReadOnlyList<GameDefinition> Load()
    {
        using var resource = typeof(GameCatalog).Assembly.GetManifestResourceStream("FOG.Agent.TrustedGames.json")
            ?? throw new InvalidDataException("Trusted game catalog is missing.");
        using var reader = new StreamReader(resource);
        return Parse(reader.ReadToEnd());
    }
}

internal sealed record GameDefinition(string Id, string Name, IReadOnlyList<string> Domains);
