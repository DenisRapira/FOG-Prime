namespace FOG.Agent;

internal sealed class SessionRecoveryPolicy
{
    private DateTimeOffset? _failedSince;
    private DateTimeOffset _nextRecovery;
    private int _failures;

    public bool Observe(bool healthy, DateTimeOffset now)
    {
        if (healthy)
        {
            _failedSince = null;
            _failures = 0;
            return false;
        }

        _failedSince ??= now;
        _failures++;
        // A single slow endpoint must not interrupt an established voice session.
        if (_failures < 3 || now - _failedSince.Value < TimeSpan.FromSeconds(60) || now < _nextRecovery)
        {
            return false;
        }

        _nextRecovery = now + TimeSpan.FromMinutes(5);
        _failedSince = null;
        _failures = 0;
        return true;
    }
}
