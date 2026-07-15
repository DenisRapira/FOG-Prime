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
            CheckHttpAsync("discord-api", "https://discord.com/api/v10/gateway", cancellationToken),
            CheckHttpAsync("discord-cdn", "https://cdn.discordapp.com", cancellationToken, acceptAnyHttpStatus: true),
            CheckHttpAsync("youtube-web", "https://www.youtube.com/generate_204", cancellationToken),
            CheckHttpAsync("youtube-cdn", "https://i.ytimg.com/generate_204", cancellationToken)
        };
        return await Task.WhenAll(checks);
    }

    public async Task<IReadOnlyList<ProbeResult>> CheckStableAsync(CancellationToken cancellationToken)
    {
        var rounds = new List<IReadOnlyList<ProbeResult>>(3)
        {
            await CheckAsync(cancellationToken)
        };

        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        rounds.Add(await CheckAsync(cancellationToken));
        if (rounds.All(round => round.All(probe => probe.Ok)))
        {
            return MergeRounds(rounds);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
        rounds.Add(await CheckAsync(cancellationToken));
        return MergeRounds(rounds);
    }

    private static IReadOnlyList<ProbeResult> MergeRounds(IReadOnlyList<IReadOnlyList<ProbeResult>> rounds)
    {
        return rounds
            .SelectMany(round => round)
            .GroupBy(probe => probe.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                var probes = group.ToArray();
                var successes = probes.Count(probe => probe.Ok);
                var representative = probes.LastOrDefault(probe => probe.Ok) ?? probes[^1];
                var latency = (int)probes.Select(probe => probe.LatencyMs).Order().ElementAt(probes.Length / 2);
                return new ProbeResult(group.Key, successes >= 2, latency, representative.Detail);
            })
            .ToArray();
    }

    private static async Task<ProbeResult> CheckDnsAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync("discord.com", cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
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
            await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            return new ProbeResult("gateway", client.Connected, (int)watch.ElapsedMilliseconds, $"{host}:{port}");
        }
        catch (Exception ex)
        {
            return new ProbeResult("gateway", false, (int)watch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeResult> CheckHttpAsync(
        string name,
        string uri,
        CancellationToken cancellationToken,
        bool acceptAnyHttpStatus = false)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(4),
                PooledConnectionLifetime = TimeSpan.Zero,
                UseProxy = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("FOG-Prime-Agent/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var ok = response.IsSuccessStatusCode || acceptAnyHttpStatus;
            return new ProbeResult(name, ok, (int)watch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ProbeResult(name, false, (int)watch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }
}
