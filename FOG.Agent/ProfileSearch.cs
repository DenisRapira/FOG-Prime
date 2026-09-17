using FOG.Protocol;

namespace FOG.Agent;

internal sealed class ProfileSearch
{
    private readonly Dictionary<string, int> _scores = new(StringComparer.OrdinalIgnoreCase);
    public bool VoiceRequired { get; private set; }

    public IReadOnlyList<ProbeResult> IncludeVoiceRequirement(IReadOnlyList<ProbeResult> probes)
    {
        if (probes.Any(p => p.Name == "discord-voice-idle")) VoiceRequired = false;
        else VoiceRequired |= probes.Any(p => p.Name == "discord-voice");
        return VoiceRequired && !probes.Any(p => p.Name == "discord-voice")
            ? probes.Append(new ProbeResult("discord-voice", false, 0,
                "Waiting for desktop voice reconnection; audio not verified")).ToArray()
            : probes;
    }

    public void Record(string profile, IReadOnlyList<ProbeResult> probes) => _scores[profile] = Score(probes);

    public string? Best(string current) => _scores.OrderByDescending(p => p.Value)
        .ThenByDescending(p => p.Key.Equals(current, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Key).FirstOrDefault();

    internal static int Score(IReadOnlyList<ProbeResult> probes) => probes.Where(p => p.Ok).Sum(p => p.Name switch
    {
        "discord-voice" => 100,
        "discord-client" => 40,
        "gateway" => 30,
        "www.youtube.com" => 20,
        "discord.com" => 10,
        "cdn.discordapp.com" => 5,
        _ => 0
    });

    internal static bool PreserveConnectedClient(IReadOnlyList<ProbeResult> probes) =>
        probes.Any(p => p.Name == "discord-client" && p.Ok) &&
        probes.Any(p => p.Name == "gateway" && p.Ok) &&
        probes.Any(p => p.Name == "www.youtube.com" && p.Ok) &&
        probes.Where(p => !p.Ok).All(p => p.Name is "discord.com" or "cdn.discordapp.com" or "dns");

    public void Reset()
    {
        _scores.Clear();
        VoiceRequired = false;
    }
}
