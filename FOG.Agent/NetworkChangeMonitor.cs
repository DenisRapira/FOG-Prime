using System.Net.NetworkInformation;

namespace FOG.Agent;

internal sealed class NetworkChangeMonitor : IDisposable
{
    private readonly Action _notify;
    private int _disposed;

    public NetworkChangeMonitor(Action notify)
    {
        _notify = notify;
        NetworkChange.NetworkAddressChanged += AddressChanged;
        NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
    }

    private void AddressChanged(object? sender, EventArgs args) => Notify();
    private void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Notify();
    private void Notify()
    {
        if (Volatile.Read(ref _disposed) == 0) _notify();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        NetworkChange.NetworkAddressChanged -= AddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
    }
}
