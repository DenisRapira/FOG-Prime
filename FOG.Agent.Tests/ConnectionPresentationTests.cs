using FOG.Protocol;
using FOG.WebView2;

internal static class ConnectionPresentationTests
{
    internal static void GraceAndReadiness()
    {
        var clock = new Clock();
        var view = new ConnectionPresentation(clock);
        Require(view.Present(null).Status == "starting", "Missing evidence became ready or immediately failed.");
        clock.Advance(TimeSpan.FromSeconds(59));
        Require(view.Present(Snapshot("degraded", false)).Status == "starting", "Transient failure interrupted automatic startup.");
        clock.Advance(TimeSpan.FromSeconds(1));
        Require(view.Present(Snapshot("starting", false)).Status == "needsAttention", "Persistent startup hid the retry action.");
        Require(view.Present(Snapshot("ready", true)).Status == "ready", "Confirmed healthy Engine was not ready.");
        Require(view.Present(null).Status == "recovering", "Missing IPC after readiness falsely stayed connected.");
    }

    internal static void RecoveryAndReset()
    {
        var clock = new Clock();
        var view = new ConnectionPresentation(clock);
        view.Present(Snapshot("ready", true));
        Require(view.Present(Snapshot("degraded", false)).Status == "recovering", "Short outage showed an alarm.");
        clock.Advance(TimeSpan.FromMinutes(1));
        Require(view.Present(null).Status == "needsAttention", "Persistent outage did not offer retry.");
        view.Reset();
        Require(view.Present(null).Status == "starting", "Explicit retry retained the old failure deadline.");
        view.Present(Snapshot("ready", true));
        Require(view.Present(Snapshot("degraded", false)).Status == "recovering", "Successful recovery did not reset the deadline.");
    }

    internal static void HonestVpnAndStoppedState()
    {
        var view = new ConnectionPresentation();
        Require(view.Present(Snapshot("stopped", true)).Status != "ready", "Contradictory stopped snapshot was accepted.");
        Require(view.Present(Snapshot("ready", true) with { EngineRunning = false }).Status != "ready", "Missing transport was accepted.");
        var vpn = Snapshot("ready", true) with { EngineRunning = false, ActiveTunnel = "Test VPN" };
        Require(view.Present(vpn).Status == "vpnReady", "Confirmed direct VPN availability was not represented honestly.");
        Require(view.Present(vpn with { ConnectionHealthy = false }).Status == "recovering", "VPN presence alone qualified readiness.");
    }

    private static AgentSnapshot Snapshot(string state, bool healthy) =>
        new(state, true, healthy, "hidden-profile", DateTimeOffset.UtcNow, [new("dns", healthy, 0, "private diagnostics")]);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
}
