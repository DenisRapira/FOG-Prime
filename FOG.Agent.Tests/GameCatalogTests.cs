using FOG.Agent;
using FOG.Protocol;
using System.Text.Json;

internal static class GameCatalogTests
{
    private const string HostlistPrefix = "--hostlist-domains=";
    private static readonly string[] RobRunDomains =
        ["robrun.com", "kefirgames.com", "robrun.wiki", "robrun.site", "robrun.org"];

    internal static void CatalogContainsBothGamesWithUniqueIds()
    {
        IReadOnlyList<GameDefinition> games = GameCatalog.All;
        Require(games.Count >= 2, "Catalog must contain multiple games.");
        Require(games.All(game => !string.IsNullOrWhiteSpace(game.Id) && !string.IsNullOrWhiteSpace(game.Name)),
            "Catalog contains an empty game identity.");
        Require(games.Select(game => game.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == games.Count,
            "Catalog IDs are not unique ignoring case.");
        var robRun = games.Single(game => game.Name == "ROB RUN");
        Require(robRun.Domains.OrderBy(domain => domain).SequenceEqual(RobRunDomains.OrderBy(domain => domain)),
            "ROB RUN domains changed.");
        var warframe = games.Single(game => game.Name == "Warframe");
        Require(warframe.Domains.SequenceEqual(new[] { "warframe.com" }), "Warframe domain is missing or unexpected.");
        foreach (var game in games)
        {
            Require(game.Domains.Count > 0, $"{game.Id}: no domains.");
            Require(game.Domains.Distinct(StringComparer.OrdinalIgnoreCase).Count() == game.Domains.Count,
                $"{game.Id}: duplicate domains.");
            Require(game.Domains.All(domain => domain == domain.Trim() &&
                Uri.CheckHostName(domain) == UriHostNameType.Dns && domain.Contains('.') &&
                !domain.Any(char.IsWhiteSpace)), $"{game.Id}: unsafe domain.");
        }
    }

    internal static void ParserRejectsUnsafeCatalogs()
    {
        var valid = new GameDefinition("valid-game", "Valid game", new[] { "valid.example" });
        var invalidGames = new[]
        {
            valid with { Id = "" }, valid with { Id = "UPPERCASE" }, valid with { Id = "bad id" },
            valid with { Id = new string('a', 49) }, valid with { Name = " " },
            valid with { Name = "bad\nname" }, valid with { Name = new string('a', 65) },
            valid with { Domains = null! }, valid with { Domains = Array.Empty<string>() },
            valid with { Domains = Enumerable.Range(0, 33).Select(index => $"host{index}.example").ToArray() },
            valid with { Domains = new[] { "valid.example", "valid.example" } }
        };
        var invalidJson = new List<string> { "null", "[]", "[null]", "{", "{}", new string(' ', 32769) };
        invalidJson.AddRange(invalidGames.Select(game => JsonSerializer.Serialize(new[] { game })));
        invalidJson.Add(JsonSerializer.Serialize(new[] { valid, valid with { Name = "Duplicate ID" } }));
        invalidJson.Add(JsonSerializer.Serialize(Enumerable.Range(0, 17).Select(index => valid with { Id = $"game-{index}" })));
        foreach (var domain in new string?[] { null, "", " ", "UPPER.example", "localhost", "valid.example.",
            "https://valid.example", "*.valid.example", "valid.example/path", "valid.example,other.example",
            "valid.example\n", new string('a', 254) + ".example" })
            invalidJson.Add(JsonSerializer.Serialize(new[] { valid with { Domains = new[] { domain! } } }));
        foreach (var json in invalidJson)
        {
            var rejected = false;
            try { GameCatalog.Parse(json); }
            catch (InvalidDataException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            Require(rejected, "Unsafe catalog was accepted: " + json[..Math.Min(json.Length, 100)]);
        }
        Require(GameCatalog.Parse(JsonSerializer.Serialize(new[] { valid })).Count == 1, "Valid catalog was rejected.");
        var maximum = Enumerable.Range(0, 16).Select(index => valid with
        {
            Id = $"game-{index}".PadRight(48, 'a'), Name = new string('n', 64),
            Domains = Enumerable.Range(0, 32).Select(domain => $"host{domain}.example").ToArray()
        }).ToArray();
        Require(GameCatalog.Parse(JsonSerializer.Serialize(maximum)).Count == 16, "Valid catalog bounds were rejected.");
    }

    internal static void EnabledPolicyExtendsOnlyExistingGameHostlistsInAllProfiles()
    {
        foreach (var profile in TrustedProfiles())
        {
            var original = profile.Arguments.ToArray();
            var enabled = GameFilterPolicy.Apply(profile, true);
            RequireSameIdentity(profile, enabled);
            Require(enabled.Arguments.Count == original.Length, $"{profile.Id}: argument count changed.");
            var gameHostlists = 0;
            for (var index = 0; index < original.Length; index++)
            {
                var argument = original[index];
                var expected = argument;
                if (IsGameHostlist(argument))
                {
                    gameHostlists++;
                    expected = MergeCatalogDomains(argument);
                    var domains = Domains(enabled.Arguments[index]);
                    foreach (var domain in CatalogDomains())
                        Require(domains.Count(value => value.Equals(domain, StringComparison.OrdinalIgnoreCase)) == 1,
                            $"{profile.Id}: {domain} was missing or duplicated.");
                }
                Require(enabled.Arguments[index] == expected,
                    $"{profile.Id}: unexpected rewrite at argument {index}: {argument}");
            }
            Require(gameHostlists == 2, $"{profile.Id}: expected existing TCP and UDP game hostlists.");
            Require(GameFilterPolicy.Apply(enabled, true).Arguments.SequenceEqual(enabled.Arguments),
                $"{profile.Id}: enabled policy is not idempotent.");
            Require(profile.Arguments.SequenceEqual(original), $"{profile.Id}: trusted input was mutated.");
        }
    }

    internal static void DisabledPolicyPreservesNarrow20202ServiceRules()
    {
        foreach (var profile in TrustedProfiles())
        {
            var original = profile.Arguments.ToArray();
            var boundary = Array.IndexOf(original, "--filter-tcp=1024-65535");
            Require(boundary > 0 && original[boundary - 1] == "--new", $"{profile.Id}: missing game boundary.");
            var expected = original.Take(boundary - 1).Select(argument =>
                argument.StartsWith("--wf-tcp=", StringComparison.Ordinal)
                    ? "--wf-tcp=80,443,2053,2083,2087,2096,8443"
                    : argument.StartsWith("--wf-udp=", StringComparison.Ordinal)
                        ? "--wf-udp=443,19294-19344,50000-50100" : argument).ToArray();
            var disabled = GameFilterPolicy.Apply(profile, false);
            RequireSameIdentity(profile, disabled);
            Require(disabled.Arguments.SequenceEqual(expected), $"{profile.Id}: disabled behavior differs from 2.0.2 narrow rules.");
            Require(!disabled.Arguments.Any(argument => argument.Contains("warframe.com", StringComparison.OrdinalIgnoreCase)),
                $"{profile.Id}: disabled mode added Warframe.");
            Require(profile.Arguments.SequenceEqual(original), $"{profile.Id}: disabled policy mutated input.");
        }
    }

    internal static void MissingDomainsAreAddedOnceWithoutTouchingGoogleOrVoice()
    {
        var profile = new EngineProfile("catalog-scope-test", 1, "FOG.Engine.exe", new[]
        {
            "--filter-tcp=443", HostlistPrefix + "google.com,youtube.com", "--dpi-desync=hostfakesplit", "--new",
            "--filter-udp=19294-19344,50000-50100", "--filter-l7=discord,stun",
            HostlistPrefix + "discord.media", "--dpi-desync=fake", "--new",
            "--filter-tcp=80,443", HostlistPrefix + "robrun.com,example.org,warframe.com", "--dpi-desync=fake", "--new",
            "--filter-udp=443", HostlistPrefix + "robrun.com", "--dpi-desync=fake", "--new",
            "--filter-tcp=443", HostlistPrefix + "notrobrun.com,robrun.com.invalid", "--dpi-desync=fake"
        });
        var original = profile.Arguments.ToArray();
        var enabled = GameFilterPolicy.Apply(profile, true);
        Require(enabled.Arguments.Count == original.Length, "Synthetic policy added rules or arguments.");
        for (var index = 0; index < original.Length; index++)
        {
            var expected = IsGameHostlist(original[index]) ? MergeCatalogDomains(original[index]) : original[index];
            Require(enabled.Arguments[index] == expected, $"Synthetic policy changed rule scope at {index}.");
        }
        Require(GameFilterPolicy.Apply(enabled, true).Arguments.SequenceEqual(enabled.Arguments),
            "Applying enabled mode twice duplicated catalog domains.");
        Require(profile.Arguments.SequenceEqual(original), "Synthetic source profile was mutated.");
        var oversized = profile with { Arguments = new[] { HostlistPrefix + "robrun.com," + new string('a', 2048) } };
        try
        {
            GameFilterPolicy.Apply(oversized, true);
            throw new InvalidOperationException("Oversized game argument was accepted.");
        }
        catch (InvalidDataException) { }
    }

    private static EngineProfile[] TrustedProfiles()
    {
        using var resource = typeof(ProfileCatalog).Assembly.GetManifestResourceStream("FOG.Agent.TrustedProfiles.json")
            ?? throw new InvalidOperationException("Trusted profiles resource is missing.");
        var profiles = JsonSerializer.Deserialize<EngineProfile[]>(resource, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Trusted profiles resource is invalid.");
        Require(profiles.Length == 4, "Tests must cover all four release profiles.");
        return profiles;
    }

    private static IEnumerable<string> CatalogDomains() => GameCatalog.All.SelectMany(game => game.Domains);
    private static string[] Domains(string argument) => argument[HostlistPrefix.Length..].Split(',');
    private static bool IsGameHostlist(string argument) => argument.StartsWith(HostlistPrefix, StringComparison.Ordinal) &&
        Domains(argument).Contains("robrun.com", StringComparer.OrdinalIgnoreCase);

    private static string MergeCatalogDomains(string argument)
    {
        var domains = Domains(argument).ToList();
        foreach (var domain in CatalogDomains())
            if (!domains.Contains(domain, StringComparer.OrdinalIgnoreCase)) domains.Add(domain);
        return HostlistPrefix + string.Join(',', domains);
    }

    private static void RequireSameIdentity(EngineProfile original, EngineProfile actual) =>
        Require(actual.Id == original.Id && actual.Priority == original.Priority && actual.Executable == original.Executable,
            $"{original.Id}: policy changed profile identity.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
