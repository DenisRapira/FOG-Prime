using FOG.Protocol;
using System.Diagnostics;

namespace FOG.Agent;

public sealed class Worker(
    AgentPipeServer pipeServer,
    EngineSupervisor supervisor,
    RuntimeIntegrityVerifier integrityVerifier,
    AgentStateStore stateStore,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private string? _lastStateSignature;
    private long _lastStateSavedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FOG Prime Agent started");
        await integrityVerifier.VerifyAsync(stoppingToken);
        using var networkChanges = new NetworkChangeMonitor(supervisor.NotifyNetworkChanged);
        await Task.WhenAll(
            pipeServer.RunAsync(stoppingToken),
            RunCompatibilityLoopAsync(stoppingToken),
            RunHealthLoopAsync(stoppingToken));
    }

    private async Task RunCompatibilityLoopAsync(CancellationToken cancellationToken)
    {
        var nextDelay = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await supervisor.WaitForEnvironmentRefreshAsync(nextDelay, cancellationToken))
                    await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                var snapshot = await supervisor.ReconcileCompatibilityAsync(cancellationToken);
                nextDelay = TransportPolicy.GetCompatibilityInterval(snapshot.State);
                await PublishSnapshotAsync(snapshot, "state changed", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compatibility reconciliation failed");
                nextDelay = TransportPolicy.DegradedHealthInterval;
            }
        }
    }

    private async Task RunHealthLoopAsync(CancellationToken cancellationToken)
    {
        var nextDelay = TransportPolicy.InitialHealthInterval;
        while (!cancellationToken.IsCancellationRequested)
        {
            await supervisor.WaitForHealthRefreshAsync(nextDelay, cancellationToken);
            try
            {
                var snapshot = await supervisor.RecoverIfNeededAsync(cancellationToken);
                nextDelay = TransportPolicy.GetHealthInterval(snapshot.TransportMode, snapshot.State);
                await PublishSnapshotAsync(snapshot, "health changed", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent health refresh failed");
                nextDelay = TransportPolicy.DirectHealthInterval;
            }
        }
    }

    private async Task PublishSnapshotAsync(AgentSnapshot snapshot, string source, CancellationToken cancellationToken)
    {
        var signature = GetStateSignature(snapshot);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var changed = signature != _lastStateSignature;
            if (!TransportPolicy.ShouldPersistSnapshot(changed, Stopwatch.GetElapsedTime(_lastStateSavedAt))) return;
            await stateStore.SaveAsync(snapshot, cancellationToken);
            _lastStateSignature = signature;
            _lastStateSavedAt = Stopwatch.GetTimestamp();
            if (changed) LogSnapshot(source, snapshot);
        }
        finally { _stateGate.Release(); }
    }

    private static string GetStateSignature(AgentSnapshot snapshot)
    {
        return string.Join(
            '|',
            snapshot.State,
            snapshot.EngineRunning,
            snapshot.ConnectionHealthy,
            snapshot.ActiveProfile,
            snapshot.Compatibility?.Reason,
            snapshot.Compatibility?.Signal,
            snapshot.Compatibility?.RetryAt,
            snapshot.TransportMode,
            snapshot.ActiveTunnel,
            snapshot.GameFilterEnabled,
            string.Join(',', snapshot.Probes.Select(probe => probe.Name + "=" + probe.Ok)));
    }

    private void LogSnapshot(string source, AgentSnapshot snapshot)
    {
        var probes = snapshot.Probes.Count == 0
            ? "none"
            : string.Join(", ", snapshot.Probes.Select(probe =>
                $"{probe.Name}={(probe.Ok ? "ok" : "fail")}({probe.LatencyMs}ms; {probe.Detail})"));
        logger.LogInformation(
            "Agent {Source}: state={State}; mode={Mode}; engine={EngineRunning}; healthy={Healthy}; profile={Profile}; tunnel={Tunnel}; probes=[{Probes}]",
            source,
            snapshot.State,
            snapshot.TransportMode,
            snapshot.EngineRunning,
            snapshot.ConnectionHealthy,
            snapshot.ActiveProfile ?? "none",
            snapshot.ActiveTunnel ?? "none",
            probes);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await supervisor.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
