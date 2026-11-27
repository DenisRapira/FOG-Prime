namespace FOG.Agent;

internal sealed class SnapshotCache<T>(Func<T> collect, TimeSpan lifetime, TimeProvider? clock = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private T? _value;
    private bool _hasValue;
    private long _collectedAt;

    public T Read()
    {
        lock (_sync)
        {
            if (!_hasValue || _clock.GetElapsedTime(_collectedAt) >= lifetime)
            {
                var value = collect();
                _value = value;
                _collectedAt = _clock.GetTimestamp();
                _hasValue = true;
            }
            return _value!;
        }
    }

    public void Invalidate()
    {
        lock (_sync) _hasValue = false;
    }
}
