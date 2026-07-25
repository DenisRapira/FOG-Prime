using FOG.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace FOG.Agent;

public static class TransportPolicy
{
    public static bool ShouldProbeBeforeEngine([NotNullWhen(true)] NetworkAdapterInfo? activeTunnel) =>
        activeTunnel is not null;

    public static bool CanUseDirectMode(
        NetworkAdapterInfo? activeTunnel,
        IReadOnlyCollection<ProbeResult> probes)
    {
        return activeTunnel is not null &&
            probes.Count > 0 &&
            probes.All(probe => probe.Ok);
    }

    public static bool ShouldRotateProfile(
        bool engineRunning,
        IReadOnlyCollection<ProbeResult> probes)
    {
        if (!engineRunning)
        {
            return true;
        }

        return probes.Count > 0 && !probes.All(probe => probe.Ok);
    }
}
