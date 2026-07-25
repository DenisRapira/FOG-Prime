namespace FOG.Agent;

public sealed class DriverConflictBackoff
{
    private readonly object _sync = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private DateTimeOffset? _pauseUntil;

    public DateTimeOffset RecordFailure(DateTimeOffset now)
    {
        lock (_sync)
        {
            while (_failures.TryPeek(out var oldest) && now - oldest > TimeSpan.FromMinutes(5))
            {
                _failures.Dequeue();
            }

            _failures.Enqueue(now);
            var delay = _failures.Count switch
            {
                1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(10),
                _ => TimeSpan.FromMinutes(1)
            };

            _pauseUntil = now + delay;
            return _pauseUntil.Value;
        }
    }

    public bool TryGetActivePause(DateTimeOffset now, out DateTimeOffset pauseUntil)
    {
        lock (_sync)
        {
            if (_pauseUntil is { } current && current > now)
            {
                pauseUntil = current;
                return true;
            }

            pauseUntil = default;
            return false;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _failures.Clear();
            _pauseUntil = null;
        }
    }
}
