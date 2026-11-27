namespace FOG.WebView2;

// Called only on the UI thread. Restore remains immediate during a pending suspend.
internal sealed class BrowserSleepController(Func<Task<bool>> suspend, Action resume) : IDisposable
{
    private Task? _transition;
    private long _revision;
    private bool _disposed;
    private bool _restoreFailed;
    internal bool WantsSleep { get; private set; }

    internal Task SetMinimizedAsync(bool minimized)
    {
        if (_disposed) return Task.CompletedTask;
        if (_transition is not null && WantsSleep == minimized && !_restoreFailed && !_transition.IsFaulted) return _transition;
        WantsSleep = minimized;
        _revision++;
        if (!minimized)
        {
            ResumeBrowser();
            if (_transition is null || _transition.IsCompleted) _transition = Task.CompletedTask;
            return _transition;
        }
        if (_transition is null || _transition.IsCompleted) _transition = ReconcileAsync();
        return _transition;
    }

    private async Task ReconcileAsync()
    {
        while (!_disposed)
        {
            var revision = _revision;
            if (WantsSleep) await suspend();
            else { ResumeBrowser(); return; }
            if (_disposed) return;
            if (revision == _revision) return;
        }
    }

    private void ResumeBrowser()
    {
        try { resume(); _restoreFailed = false; }
        catch { _restoreFailed = true; throw; }
    }

    public void Dispose()
    {
        _disposed = true;
        _revision++;
    }
}
