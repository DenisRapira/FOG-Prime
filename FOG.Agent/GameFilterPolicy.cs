using FOG.Protocol;

namespace FOG.Agent;

internal static class GameFilterPolicy
{
    public static EngineProfile Apply(EngineProfile profile, bool enabled)
    {
        if (enabled)
        {
            var domains = GameCatalog.All.SelectMany(game => game.Domains);
            var expanded = profile.Arguments.Select(argument =>
            {
                const string prefix = "--hostlist-domains=";
                if (!argument.StartsWith(prefix, StringComparison.Ordinal)) return argument;
                var hosts = argument[prefix.Length..].Split(',');
                if (!hosts.Contains("robrun.com", StringComparer.OrdinalIgnoreCase)) return argument;
                return prefix + string.Join(',', hosts.Concat(domains).Distinct(StringComparer.OrdinalIgnoreCase));
            }).ToArray();
            if (expanded.Any(argument => argument.Length > 2048))
                throw new InvalidDataException("Expanded game host list exceeds the argument budget.");
            return profile with { Arguments = expanded };
        }
        var arguments = profile.Arguments.ToList();
        var start = arguments.IndexOf("--filter-tcp=1024-65535");
        if (start <= 0 || arguments[start - 1] != "--new")
            throw new InvalidDataException("Game filter boundary is missing.");
        arguments.RemoveRange(start - 1, arguments.Count - start + 1);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith("--wf-tcp=")) arguments[i] = "--wf-tcp=80,443,2053,2083,2087,2096,8443";
            if (arguments[i].StartsWith("--wf-udp=")) arguments[i] = "--wf-udp=443,19294-19344,50000-50100";
        }
        return profile with { Arguments = arguments };
    }
}

internal sealed class GameFilterSettings(string path)
{
    public bool Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 32) return false;
            return System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public void Save(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, enabled ? "true" : "false");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

internal sealed class GameFilterAssetLease : IDisposable
{
    private readonly List<FileStream> _files = new();
    public static GameFilterAssetLease Acquire(string runtime)
    {
        var lease = new GameFilterAssetLease();
        try
        {
            foreach (var name in new[] { "game-ipset.txt", "game-exclude.txt", "ACTIVE_GAME_UDP.bin", "tls_clienthello_www_sferum_ru.bin" })
                lease._files.Add(File.Open(Path.Combine(runtime, name), FileMode.Open, FileAccess.Read, FileShare.Read));
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var file in _files) file.Dispose();
    }
}
