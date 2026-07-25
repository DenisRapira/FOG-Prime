using System.Net.NetworkInformation;

namespace FOG.Agent;

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status);

public interface INetworkAdapterSource
{
    IReadOnlyCollection<NetworkAdapterInfo> GetAdapters();
}

public sealed class SystemNetworkAdapterSource : INetworkAdapterSource
{
    public IReadOnlyCollection<NetworkAdapterInfo> GetAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(adapter => new NetworkAdapterInfo(
                adapter.Name,
                adapter.Description,
                adapter.NetworkInterfaceType,
                adapter.OperationalStatus))
            .ToArray();
    }
}

public sealed class ExternalTunnelDetector(INetworkAdapterSource adapterSource)
{
    private static readonly string[] BuiltInTunnelMarkers =
    {
        "teredo",
        "isatap",
        "6to4 adapter"
    };

    private static readonly string[] TunnelMarkers =
    {
        "vpn",
        "wireguard",
        "wintun",
        "openvpn",
        "tailscale",
        "zerotier",
        "tap-windows",
        "spacevpn",
        "amnezia",
        "cloudflare warp",
        "protonvpn",
        "nordlynx",
        "mullvad",
        "surfshark",
        "adguard vpn",
        "outline",
        "tun2socks",
        "sing-tun",
        "sing-box",
        "virtual private network",
        "vpn adapter",
        "virtual tunnel",
        "tunnel adapter",
        "tunnel",
        "fortinet ssl vpn",
        "cisco anyconnect",
        "openconnect"
    };

    public NetworkAdapterInfo? FindActiveTunnel()
    {
        return adapterSource.GetAdapters().FirstOrDefault(adapter =>
            adapter.Status == OperationalStatus.Up &&
            !BuiltInTunnelMarkers.Any(marker =>
                adapter.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                adapter.Description.Contains(marker, StringComparison.OrdinalIgnoreCase)) &&
            (adapter.Type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp ||
             TunnelMarkers.Any(marker =>
                 adapter.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                 adapter.Description.Contains(marker, StringComparison.OrdinalIgnoreCase))));
    }
}
