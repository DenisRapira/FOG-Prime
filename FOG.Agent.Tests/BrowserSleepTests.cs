using FOG.WebView2;

internal static class BrowserSleepTests
{
    internal static void RestoreDuringSuspend() => RestoreAsync().GetAwaiter().GetResult();

    private static async Task RestoreAsync()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumes = 0;
        using var controller = new BrowserSleepController(() => pending.Task, () => resumes++);
        var transition = controller.SetMinimizedAsync(true);
        Require(!transition.IsCompleted && controller.WantsSleep, "Suspend did not start.");
        var restored = controller.SetMinimizedAsync(false);
        Require(resumes == 1 && !controller.WantsSleep, "Restore waited for the old suspend request.");
        pending.SetResult(true);
        await Task.WhenAll(transition, restored).WaitAsync(TimeSpan.FromSeconds(2));
        Require(resumes == 2, "A late suspend was not followed by resume.");
    }

    internal static void CoalescesAndResuspends() => CoalescesAsync().GetAwaiter().GetResult();

    private static async Task CoalescesAsync()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suspends = 0;
        var resumes = 0;
        using var controller = new BrowserSleepController(() => ++suspends == 1 ? pending.Task : Task.FromResult(true), () => resumes++);
        var transition = controller.SetMinimizedAsync(true);
        for (var i = 0; i < 100; i++) Require(ReferenceEquals(transition, controller.SetMinimizedAsync(true)), "Duplicate sleep request was not coalesced.");
        Require(suspends == 1, "More than one suspend was in flight.");
        _ = controller.SetMinimizedAsync(false);
        _ = controller.SetMinimizedAsync(true);
        pending.SetResult(true);
        await transition.WaitAsync(TimeSpan.FromSeconds(2));
        Require(suspends == 2 && resumes == 1 && controller.WantsSleep, "Final minimized intent was lost.");
        await controller.SetMinimizedAsync(false);
        Require(resumes == 2, "Completed sleep did not restore.");
    }

    internal static void DisposalAndFailure() => DisposalAsync().GetAwaiter().GetResult();

    private static async Task DisposalAsync()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var controller = new BrowserSleepController(() => { calls++; return pending.Task; }, () => calls++);
        var transition = controller.SetMinimizedAsync(true);
        controller.Dispose();
        controller.Dispose();
        pending.SetResult(true);
        await transition.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.SetMinimizedAsync(false);
        Require(calls == 1, "Disposed browser was accessed.");

        var fail = true;
        using var retry = new BrowserSleepController(() => fail ? Task.FromException<bool>(new InvalidOperationException("fixture")) : Task.FromResult(false), () => { });
        try { await retry.SetMinimizedAsync(true); throw new Exception("Expected failure was swallowed."); }
        catch (InvalidOperationException) { }
        fail = false;
        await retry.SetMinimizedAsync(true);
        await retry.SetMinimizedAsync(false);
        Require(!retry.WantsSleep, "Best-effort sleep rejection blocked restore.");

        var restoreCalls = 0;
        using var recover = new BrowserSleepController(() => Task.FromResult(true), () =>
        {
            if (++restoreCalls == 1) throw new InvalidOperationException("resume fixture");
        });
        await recover.SetMinimizedAsync(true);
        try { await recover.SetMinimizedAsync(false); throw new Exception("Expected restore error was swallowed."); }
        catch (InvalidOperationException) { }
        await recover.SetMinimizedAsync(false);
        Require(restoreCalls == 2 && !recover.WantsSleep, "A failed restore suppressed its retry.");

        using var cancelled = new BrowserSleepController(() => Task.FromCanceled<bool>(new CancellationToken(true)), () => { });
        try { await cancelled.SetMinimizedAsync(true); throw new Exception("Expected cancelled suspend was swallowed."); }
        catch (OperationCanceledException) { }
        await cancelled.SetMinimizedAsync(false);
        Require(!cancelled.WantsSleep, "Cancelled suspension blocked restore.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
