using FOG.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FOG.Agent;

public sealed class ConnectionHealthChecker : IDisposable
{
    private static readonly string[] GatewayHosts = { "gateway.discord.gg", "discord.com" };
    private static readonly string[] DiscordApiEndpoints =
    {
        "https://discord.com/api/v10/gateway",
        "https://canary.discord.com/api/v10/gateway"
    };
    private static readonly string[] DiscordCdnEndpoints =
    {
        "https://cdn.discordapp.com",
        "https://media.discordapp.net"
    };
    private static readonly string[] YouTubeEndpoints =
    {
        "https://www.youtube.com/generate_204",
        "https://m.youtube.com/generate_204",
        "https://youtubei.googleapis.com/generate_204"
    };

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        UseProxy = false,
        Proxy = null
    })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };
    private int _gatewayOffset;
    private int _discordApiOffset;
    private int _discordCdnOffset;
    private int _youtubeOffset;

    public async Task<IReadOnlyList<ProbeResult>> CheckAsync(CancellationToken cancellationToken)
    {
        var discordChecks = new Task<ProbeResult>[]
        {
            CheckStableAsync("gateway", attempt => CheckTcpAttemptAsync(
                Select(GatewayHosts, Volatile.Read(ref _gatewayOffset) + attempt),
                443,
                cancellationToken)),
            CheckStableAsync("discord.com", attempt => CheckHttpAttemptAsync(
                Select(DiscordApiEndpoints, Volatile.Read(ref _discordApiOffset) + attempt),
                cancellationToken)),
            CheckStableAsync("cdn.discordapp.com", attempt => CheckHttpAttemptAsync(
                Select(DiscordCdnEndpoints, Volatile.Read(ref _discordCdnOffset) + attempt),
                cancellationToken))
        };

        var discordResults = await Task.WhenAll(discordChecks);
        Interlocked.Increment(ref _gatewayOffset);
        Interlocked.Increment(ref _discordApiOffset);
        Interlocked.Increment(ref _discordCdnOffset);

        if (!discordResults.All(result => result.Ok))
        {
            return discordResults;
        }

        var otherChecks = new Task<ProbeResult>[]
        {
            CheckStableAsync("dns", attempt => CheckDnsAttemptAsync(attempt, cancellationToken)),
            CheckStableAsync("www.youtube.com", attempt => CheckHttpAttemptAsync(
                Select(YouTubeEndpoints, Volatile.Read(ref _youtubeOffset) + attempt),
                cancellationToken))
        };

        var otherResults = await Task.WhenAll(otherChecks);
        Interlocked.Increment(ref _youtubeOffset);
        return discordResults.Concat(otherResults).ToArray();
    }

    private static async Task<ProbeResult> CheckStableAsync(
        string name,
        Func<int, Task<ProbeAttempt>> attemptFactory)
    {
        var successes = 0;
        var attempts = 0;
        var totalLatency = 0;
        var lastDetail = "No response";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await attemptFactory(attempt);
            attempts++;
            totalLatency += result.LatencyMs;
            lastDetail = result.Detail;
            if (result.Ok)
            {
                successes++;
            }

            if (successes >= 2 || attempts - successes >= 2)
            {
                break;
            }
        }

        return new ProbeResult(
            name,
            successes >= 2,
            attempts == 0 ? 0 : totalLatency / attempts,
            $"{successes}/{attempts}; {lastDetail}");
    }

    private static async Task<ProbeAttempt> CheckDnsAttemptAsync(int attempt, CancellationToken cancellationToken)
    {
        var host = attempt % 2 == 0 ? "discord.com" : "youtube.com";
        var watch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return new ProbeAttempt(addresses.Length > 0, (int)watch.ElapsedMilliseconds, $"{host}: {addresses.Length} addresses");
        }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{host}: {ex.GetType().Name}");
        }
    }

    private static async Task<ProbeAttempt> CheckTcpAttemptAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            return new ProbeAttempt(client.Connected, (int)watch.ElapsedMilliseconds, $"{host}:{port}");
        }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{host}: {ex.GetType().Name}");
        }
    }

    private async Task<ProbeAttempt> CheckHttpAttemptAsync(string uri, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("FOG-Prime-Agent/1.0");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var status = (int)response.StatusCode;
            return new ProbeAttempt(status < 500, (int)watch.ElapsedMilliseconds, $"{new Uri(uri).Host}: HTTP {status}");
        }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{new Uri(uri).Host}: {ex.GetType().Name}");
        }
    }

    private static string Select(IReadOnlyList<string> values, int index)
    {
        return values[Math.Abs(index % values.Count)];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ProbeAttempt(bool Ok, int LatencyMs, string Detail);
}
