using FOG.Protocol;

namespace FOG.Agent;

public sealed class Worker(
    AgentPipeServer pipeServer,
    EngineSupervisor supervisor,
    RuntimeIntegrityVerifier integrityVerifier,
    AgentStateStore stateStore,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly object _stateSync = new();
    private string? _lastStateSignature;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FOG Prime Agent started");
        await integrityVerifier.VerifyAsync(stoppingToken);
        await Task.WhenAll(
            pipeServer.RunAsync(stoppingToken),
            RunCompatibilityLoopAsync(stoppingToken),
            RunHealthLoopAsync(stoppingToken));
    }

    private async Task RunCompatibilityLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await supervisor.ReconcileCompatibilityAsync(cancellationToken);
                if (HasStateChanged(snapshot))
                {
                    await stateStore.SaveAsync(snapshot, cancellationToken);
                    LogSnapshot("state changed", snapshot);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compatibility reconciliation failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task RunHealthLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            try
            {
                var snapshot = await supervisor.RecoverIfNeededAsync(cancellationToken);
                await stateStore.SaveAsync(snapshot, cancellationToken);
                if (HasStateChanged(snapshot))
                {
                    LogSnapshot("health changed", snapshot);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent health refresh failed");
            }
        }
    }

    private bool HasStateChanged(AgentSnapshot snapshot)
    {
        var signature = GetStateSignature(snapshot);
        lock (_stateSync)
        {
            if (signature == _lastStateSignature)
            {
                return false;
            }

            _lastStateSignature = signature;
            return true;
        }
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
            snapshot.ActiveTunnel);
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
