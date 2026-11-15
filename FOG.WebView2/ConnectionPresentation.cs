using FOG.Protocol;

namespace FOG.WebView2;

public sealed record ConnectionView(string Status, string Title, string Message, int Progress);

public sealed class ConnectionPresentation(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long? _unavailableSince;
    private bool _wasReady;

    public void Reset()
    {
        _unavailableSince = null;
        _wasReady = false;
    }

    public ConnectionView Present(AgentSnapshot? snapshot)
    {
        if (snapshot is { State: "ready", ConnectionHealthy: true } &&
            (snapshot.EngineRunning || !string.IsNullOrWhiteSpace(snapshot.ActiveTunnel)))
        {
            _unavailableSince = null;
            _wasReady = true;
            var tunnel = !string.IsNullOrWhiteSpace(snapshot.ActiveTunnel);
            return new(tunnel ? "vpnReady" : "ready", tunnel ? "Подключено через VPN" : "Подключено",
                "Discord и YouTube доступны.", 100);
        }

        _unavailableSince ??= _clock.GetTimestamp();
        if (_clock.GetElapsedTime(_unavailableSince.Value) >= TimeSpan.FromMinutes(1))
            return new("needsAttention", "Не удалось подключиться", "Соединение пока недоступно.", 0);

        return _wasReady || snapshot?.State == "recovering"
            ? new("recovering", "Восстанавливаем соединение", "Повторяем подключение.", 0)
            : new("starting", "Подключаемся", "Это займёт немного времени.", 0);
    }
}
