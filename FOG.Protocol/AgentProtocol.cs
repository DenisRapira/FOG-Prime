namespace FOG.Protocol;

public static class AgentProtocol
{
    public const string PipeName = "fog-prime-agent-v1";
    public const int Version = 1;
}

public sealed record AgentRequest(string Command, string? CorrelationId = null);

public sealed record AgentResponse(
    bool Ok,
    string State,
    string Message,
    string? CorrelationId,
    AgentSnapshot? Snapshot = null);

public sealed record AgentSnapshot(
    string State,
    bool EngineRunning,
    bool ConnectionHealthy,
    string? ActiveProfile,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ProbeResult> Probes);

public sealed record ProbeResult(string Name, bool Ok, int LatencyMs, string Detail);

public sealed record EngineProfile(
    string Id,
    int Priority,
    string Executable,
    IReadOnlyList<string> Arguments);

public sealed record RuntimeManifest(
    string Version,
    string UpstreamCommit,
    IReadOnlyDictionary<string, string> Sha256);
