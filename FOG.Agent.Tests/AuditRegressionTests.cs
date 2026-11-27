using FOG.Agent;
using FOG.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;

internal static class AuditRegressionTests
{
    internal static void StartThenStopHonorsGateOrder() => IntentOrderAsync(startFirst: true).GetAwaiter().GetResult();
    internal static void StopThenStartHonorsGateOrder() => IntentOrderAsync(startFirst: false).GetAwaiter().GetResult();

    private static async Task IntentOrderAsync(bool startFirst)
    {
        using var fixture = new Fixture(withTunnel: true);
        var gate = (SemaphoreSlim)fixture.Get("_gate")!;
        await gate.WaitAsync();
        Task<AgentSnapshot> first;
        Task<AgentSnapshot> last;
        try
        {
            first = startFirst ? fixture.Supervisor.StartBestAsync(CancellationToken.None) : fixture.Supervisor.StopAsync(CancellationToken.None);
            last = startFirst ? fixture.Supervisor.StopAsync(CancellationToken.None) : fixture.Supervisor.StartBestAsync(CancellationToken.None);
            Require(!first.IsCompleted && !last.IsCompleted, "An intent bypassed the supervisor gate.");
        }
        finally { gate.Release(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = await last.WaitAsync(TimeSpan.FromSeconds(5));
        Require((bool)fixture.Get("_desiredRunning")! == !startFirst, "The final serialized intent was lost.");
        Require(snapshot.State == (startFirst ? "stopped" : "ready"), "Final snapshot disagrees with gate order.");
        if (startFirst)
        {
            var checks = fixture.Checks;
            var recovered = await fixture.Supervisor.RecoverIfNeededAsync(CancellationToken.None);
            Require(recovered.State == "stopped" && fixture.Checks == checks, "A completed Stop scheduled automatic recovery.");
        }
    }

    internal static void CancelledIntentsDoNotChangeDesiredState()
    {
        using var fixture = new Fixture(withTunnel: true);
        foreach (var running in new[] { true, false })
        {
            fixture.Set("_desiredRunning", running);
            var gate = (SemaphoreSlim)fixture.Get("_gate")!;
            gate.Wait();
            using var cancellation = new CancellationTokenSource();
            try
            {
                var command = running ? fixture.Supervisor.StopAsync(cancellation.Token) : fixture.Supervisor.StartBestAsync(cancellation.Token);
                cancellation.Cancel();
                try { command.GetAwaiter().GetResult(); throw new InvalidOperationException("Cancelled intent completed."); }
                catch (OperationCanceledException) { }
                Require((bool)fixture.Get("_desiredRunning")! == running, "A cancelled waiter changed running intent.");
            }
            finally { gate.Release(); }
        }
    }

    internal static void TunnelPathsRejectStaleProbes() => TunnelPathsAsync().GetAwaiter().GetResult();

    private static async Task TunnelPathsAsync()
    {
        foreach (var path in new[] { "start", "recheck", "recover", "handoff", "direct-refresh" })
        foreach (var change in new[] { "removed", "replaced-without-event", "same-identity-event" })
        foreach (var healthy in new[] { true, false })
        {
            using var fixture = new Fixture(withTunnel: true);
            fixture.PrepareRunning();
            fixture.Set("_directMode", true);
            fixture.Set("_activeTunnelIdentity", "previous-tunnel");
            fixture.HoldProbes();
            var operation = fixture.Run(path);
            var kind = await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(kind == (path is "recheck" or "direct-refresh" ? EngineSupervisor.HealthCheckKind.Regular : EngineSupervisor.HealthCheckKind.Direct),
                "Tunnel path selected an unexpected probe kind.");
            fixture.ChangeTopology(change);
            fixture.CompleteProbes(healthy);
            var snapshot = await operation.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertDiscarded(snapshot);
            Require(snapshot.ActiveTunnel == (change == "removed" ? null : change == "replaced-without-event" ? "Mock VPN B" : "Mock VPN A"),
                "Stale tunnel metadata was published.");
        }
    }

    internal static void EnginePathsRejectStaleProbes() => EnginePathsAsync().GetAwaiter().GetResult();

    private static async Task EnginePathsAsync()
    {
        foreach (var path in new[] { "start", "recheck", "recover", "qualification" })
        foreach (var kind in new[] { EngineSupervisor.HealthCheckKind.Regular, EngineSupervisor.HealthCheckKind.Startup, EngineSupervisor.HealthCheckKind.Confirmation })
        foreach (var withTunnel in new[] { false, true })
        foreach (var change in new[] { "physical-without-event", "same-identity-event", "tunnel-appeared" })
        foreach (var healthy in new[] { true, false })
        {
            if (path == "qualification" && kind == EngineSupervisor.HealthCheckKind.Regular) continue;
            using var fixture = new Fixture(withTunnel);
            fixture.PrepareRunning();
            fixture.AttachInertEngine();
            fixture.Set("_activeProfile", new EngineProfile("mock-profile", 1, "FOG.Engine.exe", ["--mock"]));
            fixture.Set("_startupQualificationPending", kind != EngineSupervisor.HealthCheckKind.Regular);
            fixture.Set("_startupQualificationFailures", kind == EngineSupervisor.HealthCheckKind.Confirmation ? 1 : 0);
            fixture.Set("_nextStartupQualificationAt", DateTimeOffset.MinValue);
            fixture.HoldProbes();
            var operation = fixture.Run(path);
            Require(await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)) == kind, "Engine path used an unexpected probe kind.");
            fixture.ChangeTopology(change);
            fixture.CompleteProbes(healthy);
            var snapshot = await operation.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertDiscarded(snapshot);
            Require(snapshot.EngineRunning && fixture.Get("_engine") is not null, "A topology change replaced the live Engine.");
            Require((int)fixture.Get("_startupQualificationFailures")! == 0, "Stale failure advanced profile qualification.");
            var search = (ProfileSearch)fixture.Get("_profileSearch")!;
            Require(search.Best("mock-profile") is null, "Stale probes were scored for profile selection.");
        }
    }

    internal static void UnchangedTopologyAcceptsResults()
    {
        using (var fixture = new Fixture(withTunnel: true))
        {
            var snapshot = fixture.Run("start").GetAwaiter().GetResult();
            Require(snapshot is { State: "ready", ConnectionHealthy: true, TransportMode: "direct" }, "Current direct probes were rejected.");
        }
        foreach (var kind in new[] { EngineSupervisor.HealthCheckKind.Regular, EngineSupervisor.HealthCheckKind.Startup, EngineSupervisor.HealthCheckKind.Confirmation })
        {
            using var fixture = new Fixture(withTunnel: false);
            fixture.PrepareRunning();
            fixture.AttachInertEngine();
            fixture.Set("_startupQualificationPending", kind != EngineSupervisor.HealthCheckKind.Regular);
            fixture.Set("_startupQualificationFailures", kind == EngineSupervisor.HealthCheckKind.Confirmation ? 1 : 0);
            var snapshot = fixture.Run("recheck").GetAwaiter().GetResult();
            Require(snapshot is { State: "ready", EngineRunning: true, ConnectionHealthy: true }, "Current Engine probes were rejected.");
            Require(fixture.LastKind == kind, "Stable Engine probe selection changed.");
        }
    }

    internal static void NotificationInvalidatesCachedSuccess()
    {
        using var fixture = new Fixture(withTunnel: true);
        Require(fixture.Run("start").GetAwaiter().GetResult().ConnectionHealthy, "Initial check failed.");
        fixture.Supervisor.NotifyNetworkChanged();
        fixture.AssertDiscarded(fixture.Supervisor.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult());
        Require(fixture.Run("start").GetAwaiter().GetResult().ConnectionHealthy, "Fresh checks did not restore readiness.");
    }

    internal static void CancelledProbeDoesNotPublishResults() => CancelledProbeAsync().GetAwaiter().GetResult();

    private static async Task CancelledProbeAsync()
    {
        using var fixture = new Fixture(withTunnel: true);
        fixture.HoldProbes();
        using var cancellation = new CancellationTokenSource();
        var operation = fixture.Supervisor.StartBestAsync(cancellation.Token);
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        fixture.CompleteProbes(healthy: true);
        try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Cancelled probes were accepted."); }
        catch (OperationCanceledException) { }
        Require(((IReadOnlyList<ProbeResult>)fixture.Get("_lastProbes")!).Count == 0, "Cancelled probes populated the cache.");
    }

    internal static void TopologyCancellationReleasesGate() => TopologyCancellationReleasesGateAsync().GetAwaiter().GetResult();

    private static async Task TopologyCancellationReleasesGateAsync()
    {
        using var fixture = new Fixture(withTunnel: true);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe = (_, token) => WaitForCancellationAsync(token, observed);
        var operation = fixture.Run("start");
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owner = fixture.Get("_activeProbe")!;
        var stopped = fixture.Supervisor.StopAsync(CancellationToken.None);
        fixture.Supervisor.NotifyNetworkChanged();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.AssertDiscarded(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Require((await stopped.WaitAsync(TimeSpan.FromSeconds(5))).State == "stopped", "Cancelled old I/O retained the supervisor gate.");
        await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
        Require(fixture.Get("_activeProbe") is null, "Completed probe retained cancellation ownership.");
    }

    internal static void TopologyCancellationCoversProbeKinds() => TopologyCancellationCoversProbeKindsAsync().GetAwaiter().GetResult();

    private static async Task TopologyCancellationCoversProbeKindsAsync()
    {
        foreach (var path in new[] { "start", "recheck", "recover", "handoff", "direct-refresh" })
        {
            using var fixture = new Fixture(withTunnel: true);
            fixture.PrepareRunning();
            fixture.Set("_directMode", true);
            fixture.Set("_activeTunnelIdentity", "previous-tunnel");
            await CancelPathAsync(fixture, path);
        }
        foreach (var kind in new[] { EngineSupervisor.HealthCheckKind.Regular, EngineSupervisor.HealthCheckKind.Startup, EngineSupervisor.HealthCheckKind.Confirmation })
        {
            using var fixture = new Fixture(withTunnel: false);
            fixture.PrepareRunning();
            fixture.AttachInertEngine();
            fixture.Set("_startupQualificationPending", kind != EngineSupervisor.HealthCheckKind.Regular);
            fixture.Set("_startupQualificationFailures", kind == EngineSupervisor.HealthCheckKind.Confirmation ? 1 : 0);
            await CancelPathAsync(fixture, "recheck");
            Require(fixture.LastKind == kind, "Cancellation changed Engine probe selection.");
        }
    }

    private static async Task CancelPathAsync(Fixture fixture, string path)
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe = (_, token) => WaitForCancellationAsync(token, observed);
        var operation = fixture.Run(path);
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owner = fixture.Get("_activeProbe")!;
        fixture.Supervisor.NotifyNetworkChanged();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.AssertDiscarded(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static void CallerCancellationRemainsCallerCancellation() => CallerCancellationAsync().GetAwaiter().GetResult();

    private static async Task CallerCancellationAsync()
    {
        foreach (var alsoNotify in new[] { false, true })
        {
            using var fixture = new Fixture(withTunnel: true);
            using var caller = new CancellationTokenSource();
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Probe = (_, token) => WaitForCancellationAsync(token, observed);
            var operation = fixture.Supervisor.StartBestAsync(caller.Token);
            await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var owner = fixture.Get("_activeProbe")!;
            caller.Cancel();
            if (alsoNotify) fixture.Supervisor.NotifyNetworkChanged();
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Caller cancellation was swallowed."); }
            catch (OperationCanceledException ex) { Require(ex.CancellationToken == caller.Token, "Internal linked token escaped as caller cancellation."); }
            await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
            Require(fixture.Get("_activeProbe") is null, "Caller cancellation leaked an active probe.");
        }
    }

    internal static void BurstCancellationAllowsFreshProbe() => BurstCancellationAsync().GetAwaiter().GetResult();

    private static async Task BurstCancellationAsync()
    {
        using var fixture = new Fixture(withTunnel: true);
        fixture.HoldProbes();
        var operation = fixture.Run("start");
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owner = fixture.Get("_activeProbe")!;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        using var registration = fixture.LastToken.Register(() => { Interlocked.Increment(ref callbacks); observed.TrySetResult(); });
        try
        {
            Parallel.For(0, 128, _ => fixture.Supervisor.NotifyNetworkChanged());
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertDiscarded(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Require((long)fixture.Get("_topologyGeneration")! == 128 && Volatile.Read(ref callbacks) == 1, "Burst lost generations or repeated cancellation callbacks.");
            Require(!await fixture.Supervisor.WaitForEnvironmentRefreshAsync(TimeSpan.Zero, CancellationToken.None), "Burst queued duplicate reconciliation work.");
            Require(!await ((HealthRefreshSignal)fixture.Get("_healthRefresh")!).WaitAsync(TimeSpan.Zero, CancellationToken.None), "Burst queued duplicate health work.");
            fixture.Probe = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<ProbeResult>>([new("gateway", true, 1, "new generation")]);
            };
            var fresh = await fixture.Run("start").WaitAsync(TimeSpan.FromSeconds(5));
            Require(fresh.ConnectionHealthy, "Old cancellation poisoned the new generation.");
            fixture.CompleteProbes(healthy: true);
            await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
            Require(ReferenceEquals(fresh.Probes, fixture.Get("_lastProbes")), "Retired I/O overwrote new-generation results.");
        }
        finally { fixture.CompleteProbes(healthy: true); }
    }

    internal static void CancellationCallbackFaultsAreContained() => CancellationCallbackFaultsAsync().GetAwaiter().GetResult();

    private static async Task CancellationCallbackFaultsAsync()
    {
        using var fixture = new Fixture(withTunnel: true);
        fixture.HoldProbes();
        var operation = fixture.Run("start");
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owner = fixture.Get("_activeProbe")!;
        var token = fixture.LastToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("Mock cancellation callback failure.");
        });
        try
        {
            await Task.Run(fixture.Supervisor.NotifyNetworkChanged).WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertDiscarded(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Require(!Cleanup(owner).IsCompleted, "Token source was retired while I/O or callbacks were still using it.");
            _ = token.WaitHandle;
            Require((await fixture.Supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).State == "stopped",
                "A blocked cancellation callback starved the supervisor gate.");
        }
        finally
        {
            release.TrySetResult();
            fixture.CompleteProbes(healthy: true);
        }
        await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
        Require(Cleanup(owner).IsCompletedSuccessfully, "Callback fault escaped cleanup.");
        AssertSourceDisposed(owner);
    }

    internal static void CompletedProbeReleasesCancellationOwnership() => CompletedProbeOwnershipAsync().GetAwaiter().GetResult();

    private static async Task CompletedProbeOwnershipAsync()
    {
        using var fixture = new Fixture(withTunnel: true);
        using var caller = new CancellationTokenSource();
        fixture.HoldProbes();
        var operation = fixture.Supervisor.StartBestAsync(caller.Token);
        await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owner = fixture.Get("_activeProbe")!;
        var token = fixture.LastToken;
        fixture.CompleteProbes(healthy: true);
        Require((await operation.WaitAsync(TimeSpan.FromSeconds(5))).ConnectionHealthy, "Completed probes were not accepted.");
        await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
        AssertSourceDisposed(owner);
        caller.Cancel();
        fixture.Supervisor.NotifyNetworkChanged();
        Require(!token.IsCancellationRequested && fixture.Get("_activeProbe") is null, "Retired probe retained a caller/event cancellation link.");
        Require((await fixture.Run("start").WaitAsync(TimeSpan.FromSeconds(5))).ConnectionHealthy, "New probe reused a retired token source.");
    }

    internal static void CompletionNotificationRaceIsSafe() => CompletionNotificationRaceAsync().GetAwaiter().GetResult();

    private static async Task CompletionNotificationRaceAsync()
    {
        for (var index = 0; index < 32; index++)
        {
            using var fixture = new Fixture(withTunnel: true);
            fixture.HoldProbes();
            var operation = fixture.Run("start");
            await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var owner = fixture.Get("_activeProbe")!;
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = Task.Run(async () => { await barrier.Task; fixture.CompleteProbes(healthy: true); });
            var changed = Task.Run(async () => { await barrier.Task; fixture.Supervisor.NotifyNetworkChanged(); });
            barrier.SetResult();
            await Task.WhenAll(completed, changed).WaitAsync(TimeSpan.FromSeconds(5));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertDiscarded(await fixture.Supervisor.GetSnapshotAsync(CancellationToken.None));
            await Cleanup(owner).WaitAsync(TimeSpan.FromSeconds(5));
            AssertSourceDisposed(owner);
        }
    }

    private static async Task<IReadOnlyList<ProbeResult>> WaitForCancellationAsync(CancellationToken token, TaskCompletionSource observed)
    {
        using var registration = token.Register(() => observed.TrySetResult());
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Infinite mock probe completed without cancellation.");
    }

    private static Task Cleanup(object owner) => (Task)owner.GetType().GetProperty("Cleanup")!.GetValue(owner)!;

    private static void AssertSourceDisposed(object owner)
    {
        var source = (CancellationTokenSource)owner.GetType().GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        try { _ = source.Token; throw new InvalidOperationException("Retired token source was not disposed."); }
        catch (ObjectDisposedException) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Adapters _adapters;
        private readonly ConnectionHealthChecker _checker;
        private TaskCompletionSource<IReadOnlyList<ProbeResult>>? _pending;
        private Process? _inertProcess;
        internal EngineSupervisor Supervisor { get; }
        internal int Checks { get; private set; }
        internal EngineSupervisor.HealthCheckKind LastKind { get; private set; }
        internal CancellationToken LastToken { get; private set; }
        internal Func<EngineSupervisor.HealthCheckKind, CancellationToken, Task<IReadOnlyList<ProbeResult>>>? Probe { get; set; }
        internal TaskCompletionSource<EngineSupervisor.HealthCheckKind> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture(bool withTunnel)
        {
            _adapters = new Adapters(withTunnel);
            _checker = new ConnectionHealthChecker(new NoNetworkHandler(),
                (_, _, _, _) => throw new InvalidOperationException("Live probes are forbidden in audit regressions."));
            Supervisor = new EngineSupervisor(Options.Create(new AgentOptions()), null!, null!, _checker,
                new CompatibilityGuard(new Signals()), new ExternalTunnelDetector(_adapters), new DriverConflictBackoff(),
                new ProfileRotation(), null!, null!, null!, NullLogger<EngineSupervisor>.Instance);
            Set("_gameFilterEnabled", false);
            Set("_checkHealth", (Func<EngineSupervisor.HealthCheckKind, CancellationToken, Task<IReadOnlyList<ProbeResult>>>)CheckAsync);
        }

        private Task<IReadOnlyList<ProbeResult>> CheckAsync(EngineSupervisor.HealthCheckKind kind, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Checks++;
            LastKind = kind;
            LastToken = token;
            Entered.TrySetResult(kind);
            return Probe?.Invoke(kind, token) ?? _pending?.Task ?? Task.FromResult(Results(healthy: true));
        }

        internal void PrepareRunning()
        {
            Set("_desiredRunning", true);
            Set("_preferredProfileLoaded", true);
            Set("_preferredContext", "previous-context");
            Set("_rememberedProfileId", "mock-profile");
            if (_adapters.Value.FirstOrDefault(adapter => adapter.Type == NetworkInterfaceType.Tunnel) is { } tunnel)
                Set("_activeTunnelIdentity", string.Join('|', tunnel.Name, tunnel.Description, tunnel.Type,
                    tunnel.Status, tunnel.Id, tunnel.ConnectionSignature));
        }

        internal void AttachInertEngine()
        {
            // Only HasExited is inspected. Null launch/stop dependencies fail before any process operation.
            _inertProcess = Process.GetCurrentProcess();
            var type = typeof(EngineSupervisor).GetNestedType("EngineInstance", BindingFlags.NonPublic)!;
            Set("_engine", Activator.CreateInstance(type, _inertProcess, "mock-profile", DateTimeOffset.UtcNow)!);
        }

        internal void HoldProbes() => _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void CompleteProbes(bool healthy) => _pending!.TrySetResult(Results(healthy));
        private static IReadOnlyList<ProbeResult> Results(bool healthy) => [new("gateway", healthy, 1, "mock only")];

        internal Task<AgentSnapshot> Run(string path) => path switch
        {
            "start" => Supervisor.StartBestAsync(CancellationToken.None),
            "recheck" => Supervisor.RefreshHealthAsync(CancellationToken.None),
            "recover" => Supervisor.RecoverIfNeededAsync(CancellationToken.None),
            "handoff" or "qualification" => Supervisor.ReconcileCompatibilityAsync(CancellationToken.None),
            "direct-refresh" => (Task<AgentSnapshot>)typeof(EngineSupervisor).GetMethod("RefreshHealthInternalAsync", PrivateInstance)!
                .Invoke(Supervisor, [CancellationToken.None])!,
            _ => throw new ArgumentException("Unknown regression path.", nameof(path))
        };

        internal void ChangeTopology(string change)
        {
            switch (change)
            {
                case "removed": _adapters.Value = []; Supervisor.NotifyNetworkChanged(); break;
                case "replaced-without-event": _adapters.Value = [Adapters.Tunnel("B")]; break;
                case "physical-without-event": _adapters.Value = [Adapters.Physical("B")]; break;
                case "tunnel-appeared": _adapters.Value = [Adapters.Tunnel("B")]; Supervisor.NotifyNetworkChanged(); break;
                case "same-identity-event":
                    var original = _adapters.Value;
                    _adapters.Value = [Adapters.Physical("transient")];
                    Supervisor.NotifyNetworkChanged();
                    _adapters.Value = original;
                    Supervisor.NotifyNetworkChanged();
                    break;
                default: throw new ArgumentException("Unknown topology change.", nameof(change));
            }
        }

        internal void AssertDiscarded(AgentSnapshot snapshot)
        {
            Require(Checks == 1, "Stale results caused an inline retry instead of scheduling fresh work.");
            Require(!snapshot.ConnectionHealthy && snapshot.State != "ready" && snapshot.Probes.Count == 0, "Stale probes established readiness.");
            Require(!(bool)Get("_directMode")!, "Stale probes retained direct mode.");
            Require(Get("_preferredContext") is null && Get("_rememberedProfileId") is null && !(bool)Get("_preferredProfileLoaded")!,
                "Stale probes retained or saved a network preference.");
            Require(Supervisor.WaitForEnvironmentRefreshAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(),
                "Stale probes did not schedule topology reconciliation.");
            Require(((HealthRefreshSignal)Get("_healthRefresh")!).WaitAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(),
                "Stale probes did not schedule a fresh health check.");
        }

        internal object? Get(string name) => typeof(EngineSupervisor).GetField(name, PrivateInstance)!.GetValue(Supervisor);
        internal void Set(string name, object? value) => typeof(EngineSupervisor).GetField(name, PrivateInstance)!.SetValue(Supervisor, value);
        public void Dispose() { Set("_engine", null); _inertProcess?.Dispose(); _checker.Dispose(); }
    }

    private sealed class Adapters(bool withTunnel) : INetworkAdapterSource
    {
        internal IReadOnlyCollection<NetworkAdapterInfo> Value = withTunnel ? [Tunnel("A")] : [Physical("A")];
        public IReadOnlyCollection<NetworkAdapterInfo> GetAdapters() => Volatile.Read(ref Value);
        internal static NetworkAdapterInfo Tunnel(string id) => new("Mock VPN " + id, "Mock tunnel", NetworkInterfaceType.Tunnel, OperationalStatus.Up, id, "route-" + id);
        internal static NetworkAdapterInfo Physical(string id) => new("Mock Ethernet", "Mock adapter", NetworkInterfaceType.Ethernet, OperationalStatus.Up, "ethernet", "route-" + id);
    }

    private sealed class Signals : ICompatibilitySignalSource
    {
        public IReadOnlyCollection<string> GetActiveSignals() => [];
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Live HTTP is forbidden in audit regressions.");
    }
}
