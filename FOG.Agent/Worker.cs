namespace FOG.Agent;

public sealed class Worker(
    AgentPipeServer pipeServer,
    EngineSupervisor supervisor,
    AgentStateStore stateStore,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FOG Prime Agent started");
        var pipeTask = pipeServer.RunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await supervisor.RecoverIfNeededAsync(stoppingToken);
                await stateStore.SaveAsync(snapshot, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent health refresh failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }

        await pipeTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await supervisor.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
