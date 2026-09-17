namespace FOG.Protocol;

public static class AgentProtocol
{
    public const string PipeName = "fog-prime-agent-v3";
    public const string LegacyPipeName = "fog-prime-agent-v2";
    public const string LegacyPipeNameV1 = "fog-prime-agent-v1";
    public const int Version = 3;
    public const string SessionPipeEnvironment = "FOG_PRIME_PIPE_NAME";

    public static bool IsSessionPipeName(string? name) =>
        name is not null && name.StartsWith("fog-prime-session-", StringComparison.Ordinal) &&
        name.Length == 50 && name.AsSpan(18).ToArray().All(Uri.IsHexDigit);
}

public sealed record AgentRequest(string Command, string? CorrelationId = null, string? SessionToken = null);

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
    IReadOnlyList<ProbeResult> Probes,
    CompatibilityInfo? Compatibility = null,
    string TransportMode = "engine",
    string? ActiveTunnel = null);

public sealed record CompatibilityInfo(
    bool Paused,
    string? Reason,
    string? Signal,
    DateTimeOffset? RetryAt);

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
