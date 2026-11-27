using FOG.Agent;
using FOG.Protocol;
using System.Reflection;

internal static class OptimizationTests
{
    internal static void SnapshotCaching()
    {
        var clock = new Clock();
        var calls = 0;
        var cache = new SnapshotCache<int>(() => ++calls, TimeSpan.FromSeconds(5), clock);
        for (var index = 0; index < 1000; index++) Require(cache.Read() == 1, "Cache repeatedly collected unchanged state.");
        clock.Advance(TimeSpan.FromSeconds(5));
        Require(cache.Read() == 2, "Expired cache did not refresh.");
        cache.Invalidate();
        Require(cache.Read() == 3, "Network notification did not invalidate the cache.");
        var failed = true;
        var retry = new SnapshotCache<int>(() => failed ? throw new IOException("test") : 7, TimeSpan.FromSeconds(5), clock);
        try { retry.Read(); throw new InvalidOperationException("Collector failure swallowed."); }
        catch (IOException) { }
        failed = false;
        Require(retry.Read() == 7, "A failed collection poisoned subsequent refreshes.");
    }

    internal static void SchedulingAndPersistence()
    {
        Require(TransportPolicy.GetHealthInterval("engine", "ready") == TimeSpan.FromSeconds(60), "Stable Engine still probes too frequently.");
        Require(TransportPolicy.GetHealthInterval("direct", "ready") == TimeSpan.FromSeconds(15), "Stable direct policy changed.");
        Require(TransportPolicy.GetHealthInterval("engine", "recovering") == TimeSpan.FromSeconds(3), "Recovery became slow.");
        Require(TransportPolicy.GetHealthInterval("stopped", "stopped") >= TimeSpan.FromMinutes(2), "Stopped mode is busy polling.");
        Require(TransportPolicy.GetCompatibilityInterval("ready") == TimeSpan.FromSeconds(15), "Compatibility loop still scans continuously.");
        Require(!TransportPolicy.ShouldPersistSnapshot(false, TimeSpan.FromSeconds(60)), "Unchanged snapshots still write every cycle.");
        Require(TransportPolicy.ShouldPersistSnapshot(false, TimeSpan.FromMinutes(2)), "Periodic state checkpoint lost.");
        Require(TransportPolicy.ShouldPersistSnapshot(true, TimeSpan.Zero), "State transitions are not saved promptly.");
    }

    internal static void NetworkNotificationsCoalesce()
    {
        using var fixture = new SupervisorProbeFixture();
        for (var index = 0; index < 100; index++) fixture.Supervisor.NotifyNetworkChanged();
        Require(fixture.Supervisor.WaitForEnvironmentRefreshAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(),
            "Network notification did not wake compatibility reconciliation.");
        Require(!fixture.Supervisor.WaitForEnvironmentRefreshAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(),
            "Network event burst produced repeated scans.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            fixture.Supervisor.WaitForEnvironmentRefreshAsync(TimeSpan.FromMinutes(2), cancellation.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("Idle scheduler ignored shutdown.");
        }
        catch (OperationCanceledException) { }
    }

    internal static void StoppedRecoveryDoesNotProbeOrAutostart()
    {
        using var fixture = new SupervisorProbeFixture(withTunnel: true);
        var snapshot = fixture.Supervisor.RecoverIfNeededAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(snapshot.State == "stopped" && !snapshot.EngineRunning, "Stopped FOG tried to recover a connection.");
        // Probe/launch dependencies in the fixture are null and must remain unused.
        fixture.Set("_observedNetworkIdentity", "previous-network");
        var reconciled = fixture.Supervisor.ReconcileCompatibilityAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(reconciled.State == "stopped", "Network changes started explicitly stopped FOG.");
        Require(fixture.Get("_preferredContext") is null, "Previous network preference context was retained.");
    }

    internal static void NetworkChangesPreserveLiveEngine()
    {
        using var fixture = new SupervisorProbeFixture(startChild: true);
        using var checker = new ConnectionHealthChecker(new NoNetworkHandler());
        var dependency = typeof(EngineSupervisor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(ConnectionHealthChecker));
        dependency.SetValue(fixture.Supervisor, checker);
        fixture.Set("_observedNetworkIdentity", "previous-network");
        fixture.Set("_lastProbes", new[] { new ProbeResult("gateway", true, 1, "previous route") });
        var snapshot = fixture.Supervisor.ReconcileCompatibilityAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(snapshot.EngineRunning && fixture.Child is { HasExited: false }, "Network notification restarted a live Engine.");
        Require(snapshot.Probes.Count == 0 && !snapshot.ConnectionHealthy, "Previous network results were reused as fresh success.");
        Require(fixture.Get("_preferredContext") is null, "New network retained the old profile preference context.");
        var health = fixture.Supervisor.WaitForHealthRefreshAsync(TimeSpan.Zero, CancellationToken.None);
        Require(health.IsCompletedSuccessfully, "Network change did not wake fresh health checks.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan interval) => _timestamp += interval.Ticks;
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Network reconciliation must schedule probes, not run them inline for a live Engine.");
    }
}
