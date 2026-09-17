namespace FOG.Agent;

internal sealed class HealthRefreshSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        // Multiple startup notifications coalesce into one pending health check.
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan interval, CancellationToken cancellationToken) =>
        _signal.WaitAsync(interval, cancellationToken);
}
