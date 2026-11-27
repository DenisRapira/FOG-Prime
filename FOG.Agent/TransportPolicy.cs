using FOG.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace FOG.Agent;

public static class TransportPolicy
{
    public static readonly TimeSpan EngineLaunchSettleDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan InitialHealthInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DirectHealthInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan EngineHealthInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StoppedHealthInterval = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DegradedHealthInterval = TimeSpan.FromSeconds(10);

    internal static bool ShouldPersistSnapshot(bool changed, TimeSpan sinceLastWrite) =>
        changed || sinceLastWrite >= TimeSpan.FromMinutes(2);

    internal static TimeSpan GetCompatibilityInterval(string state) => state == "recovering"
        ? InitialHealthInterval : state == "stopped" ? StoppedHealthInterval : TimeSpan.FromSeconds(15);

    public static bool ShouldProbeBeforeEngine([NotNullWhen(true)] NetworkAdapterInfo? activeTunnel) =>
        activeTunnel is not null;

    public static bool CanRememberEngineProfile(NetworkAdapterInfo? activeTunnel) => activeTunnel is null;

    public static bool CanUseDirectMode(
        NetworkAdapterInfo? activeTunnel,
        IReadOnlyCollection<ProbeResult> probes,
        bool engineRunning = false)
    {
        return activeTunnel is not null &&
            !engineRunning &&
            probes.Count > 0 &&
            probes.All(probe => probe.Ok);
    }

    public static TimeSpan GetHealthInterval(string? transportMode, string? state = null) =>
        string.Equals(state, "recovering", StringComparison.OrdinalIgnoreCase)
            ? InitialHealthInterval
            : string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase)
            ? StoppedHealthInterval
            : string.Equals(state, "degraded", StringComparison.OrdinalIgnoreCase)
            ? DegradedHealthInterval
            : string.Equals(transportMode, "engine", StringComparison.OrdinalIgnoreCase)
            ? EngineHealthInterval
            : DirectHealthInterval;

    public static bool ShouldRotateProfile(
        bool engineRunning,
        IReadOnlyCollection<ProbeResult> probes)
    {
        // Connectivity probes describe the control plane, not an active Discord
        // media session. A live Engine must only be replaced after it exits.
        return !engineRunning;
    }

    public static bool ShouldRotatePendingProfile(
        bool engineRunning,
        int consecutiveFailures,
        int attemptedProfiles,
        int totalProfiles,
        IReadOnlyCollection<ProbeResult> probes)
    {
        return engineRunning &&
            consecutiveFailures >= 2 &&
            attemptedProfiles < totalProfiles &&
            probes.Count > 0 &&
            probes.Any(probe => !probe.Ok);
    }
}
