using FOG.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace FOG.Agent;

public sealed class ConnectionHealthChecker
{
    public async Task<IReadOnlyList<ProbeResult>> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new Task<ProbeResult>[]
        {
            CheckDnsAsync(cancellationToken),
            CheckTcpAsync("gateway.discord.gg", 443, cancellationToken),
            CheckHttpAsync("https://discord.com/api/v10/gateway", cancellationToken),
            CheckHttpAsync("https://cdn.discordapp.com", cancellationToken, acceptAnyHttpStatus: true)
        };
        return await Task.WhenAll(checks);
    }

    private static async Task<ProbeResult> CheckDnsAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync("discord.com", cancellationToken);
            return new ProbeResult("dns", addresses.Length > 0, (int)watch.ElapsedMilliseconds, $"{addresses.Length} addresses");
        }
        catch (Exception ex)
        {
            return new ProbeResult("dns", false, (int)watch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeResult> CheckTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return new ProbeResult("gateway", client.Connected, (int)watch.ElapsedMilliseconds, $"{host}:{port}");
        }
        catch (Exception ex)
        {
            return new ProbeResult("gateway", false, (int)watch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeResult> CheckHttpAsync(string uri, CancellationToken cancellationToken, bool acceptAnyHttpStatus = false)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("FOG-Prime-Agent/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var ok = response.IsSuccessStatusCode || acceptAnyHttpStatus;
            return new ProbeResult(new Uri(uri).Host, ok, (int)watch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ProbeResult(new Uri(uri).Host, false, (int)watch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }
}
