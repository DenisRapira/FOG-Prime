using FOG.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace FOG.Agent;

public sealed class ConnectionHealthChecker : IDisposable
{
    internal static readonly TimeSpan StartupAttemptTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan DirectFailureConfirmationDelay = TimeSpan.FromMilliseconds(250);
    private static readonly string[] GatewayHosts = { "gateway.discord.gg" };
    private static readonly string[] DiscordApiEndpoints =
    {
        "https://discord.com/api/v10/gateway"
    };
    private static readonly string[] DiscordCdnEndpoints =
    {
        "https://cdn.discordapp.com/embed/avatars/0.png"
    };
    private static readonly string[] YouTubeEndpoints =
    {
        "https://www.youtube.com/generate_204"
    };

    private readonly HttpClient _httpClient;
    private long _evidenceSinceTicks = DateTimeOffset.UtcNow.Ticks;
    private readonly bool _observeDesktop;
    private readonly Func<string, int, CancellationToken, TimeSpan?, Task<ProbeAttempt>> _tcpProbe;

    public ConnectionHealthChecker() : this(new HttpClientHandler
    {
        UseProxy = false,
        Proxy = null,
        AllowAutoRedirect = false
    }, observeDesktop: true)
    {
    }

    internal ConnectionHealthChecker(HttpMessageHandler handler,
        Func<string, int, CancellationToken, TimeSpan?, Task<ProbeAttempt>>? tcpProbe = null,
        bool observeDesktop = false)
    {
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _tcpProbe = tcpProbe ?? CheckGatewayAttemptAsync;
        _observeDesktop = observeDesktop;
    }
    private int _gatewayOffset;
    private int _discordApiOffset;
    private int _discordCdnOffset;
    private int _youtubeOffset;

    public async Task<IReadOnlyList<ProbeResult>> CheckAsync(CancellationToken cancellationToken)
    {
        var discordChecks = new Task<ProbeResult>[]
        {
            CheckStableAsync("gateway", attempt => _tcpProbe(
                Select(GatewayHosts, Volatile.Read(ref _gatewayOffset) + attempt),
                443,
                cancellationToken, ConfirmationTimeout)),
            CheckStableAsync("discord.com", attempt => CheckHttpAttemptAsync(
                Select(DiscordApiEndpoints, Volatile.Read(ref _discordApiOffset) + attempt),
                cancellationToken)),
            CheckStableAsync("cdn.discordapp.com", attempt => CheckHttpAttemptAsync(
                Select(DiscordCdnEndpoints, Volatile.Read(ref _discordCdnOffset) + attempt),
                cancellationToken))
        };

        var otherChecks = new Task<ProbeResult>[]
        {
            CheckStableAsync("dns", attempt => CheckDnsAttemptAsync(attempt, cancellationToken)),
            CheckStableAsync("www.youtube.com", attempt => CheckHttpAttemptAsync(
                Select(YouTubeEndpoints, Volatile.Read(ref _youtubeOffset) + attempt),
                cancellationToken))
        };

        var results = await Task.WhenAll(discordChecks.Concat(otherChecks));
        Interlocked.Increment(ref _gatewayOffset);
        Interlocked.Increment(ref _discordApiOffset);
        Interlocked.Increment(ref _discordCdnOffset);
        Interlocked.Increment(ref _youtubeOffset);
        return WithDesktopEvidence(results);
    }

    public async Task<IReadOnlyList<ProbeResult>> CheckStartupAsync(CancellationToken cancellationToken, bool confirm = false)
    {
        var timeout = confirm ? ConfirmationTimeout : StartupAttemptTimeout;
        var checks = new Task<ProbeResult>[]
        {
            CheckOnceAsync("gateway", () => _tcpProbe(
                Select(GatewayHosts, Volatile.Read(ref _gatewayOffset)),
                443,
                cancellationToken,
                timeout)),
            CheckOnceAsync("discord.com", () => CheckHttpAttemptAsync(
                Select(DiscordApiEndpoints, Volatile.Read(ref _discordApiOffset)),
                cancellationToken,
                timeout)),
            CheckOnceAsync("cdn.discordapp.com", () => CheckHttpAttemptAsync(
                Select(DiscordCdnEndpoints, Volatile.Read(ref _discordCdnOffset)),
                cancellationToken,
                timeout)),
            CheckOnceAsync("www.youtube.com", () => CheckHttpAttemptAsync(
                Select(YouTubeEndpoints, Volatile.Read(ref _youtubeOffset)),
                cancellationToken,
                timeout))
        };

        var results = await Task.WhenAll(checks);
        Interlocked.Increment(ref _gatewayOffset);
        Interlocked.Increment(ref _discordApiOffset);
        Interlocked.Increment(ref _discordCdnOffset);
        Interlocked.Increment(ref _youtubeOffset);
        return WithDesktopEvidence(results);
    }

    private IReadOnlyList<ProbeResult> WithDesktopEvidence(ProbeResult[] results)
    {
        // Only current-session, recent evidence can veto a synthetic success.
        var since = new DateTimeOffset(Interlocked.Read(ref _evidenceSinceTicks), TimeSpan.Zero);
        if (!_observeDesktop) return results;
        var desktop = DiscordGatewayEvidence.Read(since);
        var combined = results.ToList();
        if (desktop.Gateway is { } gateway)
            combined.Add(new ProbeResult("discord-client", gateway, 0,
                gateway ? "Recent desktop Gateway READY observed" : "Desktop Gateway did not receive OP_HELLO"));
        if (desktop.VoiceIdle)
            combined.Add(new ProbeResult("discord-voice-idle", true, 0, "Desktop voice channel closed"));
        else if (desktop.Voice is { } voice)
            combined.Add(new ProbeResult("discord-voice", voice, 0,
                voice ? "Desktop RTC_CONNECTED observed; audibility not verified" : "Voice WebSocket HELLO timed out"));
        return combined;
    }

    internal void BeginProfileObservation() =>
        Interlocked.Exchange(ref _evidenceSinceTicks, DateTimeOffset.UtcNow.Ticks);

    public async Task<IReadOnlyList<ProbeResult>> CheckDirectAsync(CancellationToken cancellationToken)
    {
        var first = await CheckStartupAsync(cancellationToken);
        if (first.All(result => result.Ok))
        {
            return first;
        }

        await Task.Delay(DirectFailureConfirmationDelay, cancellationToken);
        return await CheckStartupAsync(cancellationToken, confirm: true);
    }

    private static async Task<ProbeResult> CheckOnceAsync(
        string name,
        Func<Task<ProbeAttempt>> attemptFactory)
    {
        var result = await attemptFactory();
        return new ProbeResult(name, result.Ok, result.LatencyMs, $"{(result.Ok ? 1 : 0)}/1; {result.Detail}");
    }

    internal static async Task<ProbeResult> CheckStableAsync(
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

            if (successes >= 1 || attempts - successes >= 2)
            {
                break;
            }
        }

        return new ProbeResult(
            name,
            successes >= 1,
            attempts == 0 ? 0 : totalLatency / attempts,
            $"{successes}/{attempts}; {lastDetail}");
    }

    private static async Task<ProbeAttempt> CheckDnsAttemptAsync(int attempt, CancellationToken cancellationToken)
    {
        var host = attempt % 2 == 0 ? "discord.com" : "youtube.com";
        var watch = Stopwatch.StartNew();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, deadline.Token);
            return new ProbeAttempt(addresses.Length > 0, (int)watch.ElapsedMilliseconds, $"{host}: {addresses.Length} addresses");
        }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{host}: {ex.GetType().Name}");
        }
    }

    private static async Task<ProbeAttempt> CheckGatewayAttemptAsync(
        string host,
        int port,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout ?? ConfirmationTimeout);
            using var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            await socket.ConnectAsync(new Uri($"wss://{host}:{port}/?v=10&encoding=json"), deadline.Token);
            var valid = await ValidateGatewaySessionAsync(socket, deadline.Token);
            socket.Abort();
            return new ProbeAttempt(valid, (int)watch.ElapsedMilliseconds,
                $"{host}: WebSocket HELLO + heartbeat ACK valid={valid}; synthetic session only");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{host}: {ex.GetType().Name}");
        }
    }

    internal static async Task<bool> ValidateGatewaySessionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var hello = await ReadGatewayMessageAsync(socket, cancellationToken);
        if (hello is null || !IsGatewayHello(hello)) return false;
        // No Discord credentials or account access. Confirm both directions on this connection.
        await socket.SendAsync(new ArraySegment<byte>("{\"op\":1,\"d\":null}"u8.ToArray()),
            WebSocketMessageType.Text, true, cancellationToken);
        var acknowledgement = await ReadGatewayMessageAsync(socket, cancellationToken);
        if (acknowledgement is null) return false;
        try
        {
            using var document = JsonDocument.Parse(acknowledgement);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.Number &&
                op.TryGetInt32(out var code) && code == 11;
        }
        catch (JsonException) { return false; }
    }

    private static async Task<byte[]?> ReadGatewayMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var length = 0;
        // Bound fragmentation too, including a peer sending empty non-final frames.
        for (var frames = 0; frames < 64 && length < buffer.Length; frames++)
        {
            var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancellationToken);
            if (received.MessageType != WebSocketMessageType.Text) return null;
            length += received.Count;
            if (received.EndOfMessage) return buffer[..length];
        }
        return null;
    }

    internal async Task<ProbeAttempt> CheckHttpAttemptAsync(
        string uri,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("FOG-Prime-Agent/1.0");
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? ConfirmationTimeout);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            var status = (int)response.StatusCode;
            var endpoint = new Uri(uri);
            var ok = IsExpectedHttpStatus(endpoint, response.StatusCode);
            if (ok && endpoint.AbsolutePath == "/api/v10/gateway")
            {
                // Bound the body even when the server omits Content-Length.
                await using var body = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                var bytes = new byte[4097];
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await body.ReadAsync(bytes.AsMemory(count), timeoutSource.Token);
                    if (read == 0) break;
                    count += read;
                }
                ok = count <= 4096 && IsGatewayResponse(bytes.AsMemory(0, count));
            }
            if (ok && endpoint.Host == "cdn.discordapp.com")
            {
                await using var body = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                var header = new byte[8];
                await body.ReadExactlyAsync(header, timeoutSource.Token);
                ok = header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            }
            return new ProbeAttempt(ok, (int)watch.ElapsedMilliseconds, $"{endpoint.Host}: HTTP {status}; expected response={ok}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{new Uri(uri).Host}: deadline exceeded ({(timeout ?? ConfirmationTimeout).TotalSeconds}s)");
        }
        catch (Exception ex)
        {
            return new ProbeAttempt(false, (int)watch.ElapsedMilliseconds, $"{new Uri(uri).Host}: {ex.GetType().Name}");
        }
    }

    internal static bool IsExpectedHttpStatus(Uri endpoint, HttpStatusCode status)
    {
        if (endpoint.AbsolutePath == "/generate_204")
        {
            return status == HttpStatusCode.NoContent;
        }
        if (endpoint.AbsolutePath == "/api/v10/gateway")
        {
            return status == HttpStatusCode.OK;
        }
        return endpoint.Host == "cdn.discordapp.com" && endpoint.AbsolutePath == "/embed/avatars/0.png" &&
            status == HttpStatusCode.OK;
    }

    internal static bool IsGatewayResponse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(url.GetString(), UriKind.Absolute, out var gateway) &&
                gateway.Scheme == "wss" && gateway.Host == "gateway.discord.gg" &&
                gateway.IsDefaultPort && gateway.UserInfo.Length == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsGatewayHello(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.Number && op.TryGetInt32(out var code) && code == 10 &&
                root.TryGetProperty("d", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("heartbeat_interval", out var interval) && interval.ValueKind == JsonValueKind.Number &&
                interval.TryGetDouble(out var milliseconds) && milliseconds > 0 && double.IsFinite(milliseconds);
        }
        catch (JsonException) { return false; }
    }

    private static string Select(IReadOnlyList<string> values, int index)
    {
        return values[Math.Abs(index % values.Count)];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    internal sealed record ProbeAttempt(bool Ok, int LatencyMs, string Detail);
}
