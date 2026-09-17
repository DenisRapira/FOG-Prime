using FOG.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

if (args is ["--lifetime-child"])
{
    Thread.Sleep(Timeout.Infinite);
    return 0;
}
if (args is ["--lifetime-owner"])
{
    using var job = new FOG.WebView2.ProcessLifetimeJob();
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--lifetime-child")
    {
        UseShellExecute = false, CreateNoWindow = true
    })!;
    job.Attach(child);
    Console.WriteLine(child.Id);
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    return 0;
}

if (args is ["--check-network"])
{
    using var checker = new ConnectionHealthChecker();
    var results = await checker.CheckStartupAsync(CancellationToken.None, confirm: true);
    foreach (var result in results) Console.WriteLine($"{result.Name}: ok={result.Ok}; {result.Detail}; {result.LatencyMs}ms");
    return results.All(result => result.Ok) ? 0 : 1;
}

if (args is ["--check-update", var currentVersion])
{
    using var checker = new FOG.WebView2.UpdateChecker();
    var update = await checker.CheckAsync(currentVersion, CancellationToken.None);
    Console.WriteLine(update is null ? "no-update" : $"{update.Version} {update.PageUri}");
    return update is null ? 2 : 0;
}

var tests = new (string Name, Action Run)[]
{
    ("validates session-specific IPC names", AssertSessionPipeNames),
    ("validates bounded Gateway response bodies", AssertGatewayBodies),
    ("requires Discord WebSocket HELLO", AssertGatewayHello),
    ("requires heartbeat acknowledgement on the same socket", AssertGatewaySession),
    ("bounds gateway receive and cancellation", AssertGatewayBounds),
    ("uses only fresh desktop gateway evidence", AssertDesktopEvidence),
    ("separates voice from main Gateway and screen sharing", AssertVoiceEvidence),
    ("preserves connected Discord on ancillary HTTP failure", AssertConnectedProfilePreserved),
    ("returns to best observed profile after a bounded search", AssertBestProfileSearch),
    ("requires voice recovery after changing profiles", AssertVoiceSearchRequirement),
    ("wakes startup health checks without periodic timer delay", AssertHealthRefreshSignal),
    ("prioritizes scoped FOG bootstrap strategy", AssertBootstrapStrategy),
    ("rejects a mismatched named pipe server", AssertPipeServerIdentity),
    ("terminates children after owner crash", AssertCrashCleanup),
    ("requires endpoint-specific HTTP success", AssertHttpExpectations),
    ("probes stable service identities across cycles", AssertServiceProbeIdentities),
    ("confirms slow endpoints without masking blocked API", AssertSlowProbeConfirmation),
    ("accepts recovered transient probes", AssertTransientProbeRecovery),
    ("bounds persistent session recovery", AssertSessionRecovery),
    ("detects EasyAntiCheat", () => AssertBlocked("EasyAntiCheat")),
    ("detects EasyAntiCheat EOS executable", () => AssertBlocked("EasyAntiCheat_EOS.exe")),
    ("detects protected game launcher", () => AssertBlocked("start_protected_game")),
    ("ignores unrelated processes", AssertUnrelatedProcessesAreAllowed),
    ("detects active VPN adapters", AssertActiveVpnDetected),
    ("detects WARP Ethernet adapters", AssertWarpVpnDetected),
    ("detects generic virtual tunnel adapters", AssertGenericTunnelDetected),
    ("ignores Windows built-in transition tunnels", AssertBuiltInTunnelIgnored),
    ("ignores inactive VPN adapters", AssertInactiveVpnIgnored),
    ("preserves VPN signal during protected game", AssertIndependentVpnSignal),
    ("uses direct mode only with an active healthy VPN", AssertDirectModePolicy),
    ("does not promote Engine probes to VPN readiness", AssertEngineProbeIsolation),
    ("does not remember Engine success through VPN", AssertVpnMemoryIsolation),
    ("checks direct VPN mode more frequently", AssertAdaptiveHealthIntervals),
    ("starts Engine without a normal-network preflight", AssertPreflightPolicy),
    ("allows automatic start to outlive slow probes", AssertAgentCommandTimeouts),
    ("restarts an unresponsive Agent after bounded retries", AssertAgentRestartPolicy),
    ("detects newer GitHub release versions", AssertReleaseVersionComparison),
    ("rejects untrusted release links", AssertReleaseLinkValidation),
    ("keeps a running profile during degraded probes", AssertDegradedProbePolicy),
    ("qualifies startup profiles in the background", AssertBackgroundQualificationPolicy),
    ("keeps a profile stable through a long session", AssertLongSessionStability),
    ("authenticates a recovered Engine session", AssertEngineSessionLease),
    ("persists the last working profile", AssertPreferredProfilePersistence),
    ("bounds the fast startup probe", AssertFastStartupProbe),
    ("rotates failed connection profiles", AssertProfileRotation),
    ("applies bounded driver backoff", AssertDriverBackoff),
    ("resets driver backoff after recovery", AssertDriverBackoffReset),
    ("scopes driver cleanup to the current package", AssertOwnedDriverPath)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} compatibility tests passed.");
return failures == 0 ? 0 : 1;

static void AssertSessionPipeNames()
{
    Assert(FOG.Protocol.AgentProtocol.IsSessionPipeName("fog-prime-session-" + Guid.NewGuid().ToString("N")), "Session name rejected");
    foreach (var name in new string?[] { null, "", FOG.Protocol.AgentProtocol.PipeName, "fog-prime-session-" + new string('z', 32) })
    {
        Assert(!FOG.Protocol.AgentProtocol.IsSessionPipeName(name), "Invalid session name accepted");
    }
}

static void AssertGatewayBodies()
{
    foreach (var (body, valid) in new[] {
        ("{\"url\":\"wss://gateway.discord.gg\"}", true),
        ("{\"url\":\"ws://gateway.discord.gg\"}", false),
        ("{\"url\":\"wss://gateway.discord.gg.attacker.invalid\"}", false),
        ("{\"url\":\"wss://user@gateway.discord.gg\"}", false),
        ("{\"url\":123}", false), ("[]", false), ("<html>blocked</html>", false),
        (new string('x', 5000), false)
    })
    {
        using var checker = new ConnectionHealthChecker(new FakeHttpHandler(body));
        var result = checker.CheckHttpAttemptAsync("https://discord.com/api/v10/gateway", CancellationToken.None).GetAwaiter().GetResult();
        Assert(result.Ok == valid, "Unexpected Gateway response classification");
    }
}

static void AssertDesktopEvidence()
{
    var now = DateTimeOffset.Now;
    var since = now.AddMinutes(-1);
    string Line(DateTimeOffset time, string message) => $"[{time:yyyy-MM-dd HH:mm:ss.fff}] [info] [GatewaySocket] {message}\n";
    var failure = Line(now.AddSeconds(-10), "did not receive OP_HELLO in time");
    Assert(DiscordGatewayEvidence.Parse(failure, since, now) == false, "Fresh failure ignored");
    Assert(DiscordGatewayEvidence.Parse(failure + Line(now.AddSeconds(-1), "[READY]"), since, now) == true, "Recovery ignored");
    Assert(DiscordGatewayEvidence.Parse(failure, now, now) is null, "Old session affected new Agent");
    Assert(DiscordGatewayEvidence.Parse(Line(now.AddMinutes(-3), "[READY]"), now.AddHours(-1), now) is null, "Expired evidence accepted");
    Assert(DiscordGatewayEvidence.Parse(Line(now.AddMinutes(1), "[READY]"), since, now) is null, "Future evidence accepted");
    Assert(DiscordGatewayEvidence.Parse("bad log\n[READY]", since, now) is null, "Malformed log accepted");
}

static void AssertHealthRefreshSignal()
{
    var signal = new HealthRefreshSignal();
    signal.Request();
    signal.Request();
    var queued = signal.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
    Assert(queued.IsCompletedSuccessfully && queued.Result, "Queued startup signal waited for periodic timer");
    Assert(!signal.WaitAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(), "Duplicate notifications caused repeated probes");
    using var cancellation = new CancellationTokenSource();
    var waiting = signal.WaitAsync(TimeSpan.FromSeconds(20), cancellation.Token);
    Assert(!waiting.IsCompleted, "Idle scheduler is spinning");
    signal.Request();
    Assert(waiting.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(), "Startup did not wake waiting worker");
    var stopped = signal.WaitAsync(TimeSpan.FromSeconds(20), cancellation.Token);
    cancellation.Cancel();
    try { stopped.GetAwaiter().GetResult(); Assert(false, "Shutdown did not cancel scheduled wait"); }
    catch (OperationCanceledException) { }
    signal.Request();
    Assert(signal.WaitAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult(), "Cancelled wait swallowed future signal");
}

static void AssertVoiceEvidence()
{
    var now = DateTimeOffset.Now;
    var since = now.AddMinutes(-1);
    string Line(int seconds, string value) => $"[{now.AddSeconds(seconds):yyyy-MM-dd HH:mm:ss.fff}] [info] {value}\n";
    var failure = Line(-20, "[RTCControlSocket(default)] did not receive OP_HELLO in time");
    Assert(DiscordGatewayEvidence.ParseVoice(failure, since, now) == false, "Voice timeout ignored");
    Assert(DiscordGatewayEvidence.Parse(failure, since, now) is null, "Voice timeout contaminated main Gateway");
    var stream = Line(-10, "[RTCConnection(1, stream)] RTC_CONNECTING => RTC_CONNECTED");
    Assert(DiscordGatewayEvidence.ParseVoice(failure + stream, since, now) == false, "Screen sharing masked failed voice");
    var control = Line(-9, "[RTCControlSocket(default)] [READY]");
    Assert(DiscordGatewayEvidence.ParseVoice(failure + control, since, now) == false, "Control-plane READY counted as voice media");
    var media = Line(-8, "[RTCConnection(1, default)] RTC_CONNECTING => RTC_CONNECTED");
    Assert(DiscordGatewayEvidence.ParseVoice(failure + media, since, now) == true, "Media connection ignored");
    Assert(DiscordGatewayEvidence.ParseVoice(media, now, now) is null, "Old-profile media used");
    var close = Line(-6, "[RTCControlSocket(default)] CLOSE");
    Assert(DiscordGatewayEvidence.IsVoiceIdle(failure + close, since, now), "Voice leave ignored");
    Assert(!DiscordGatewayEvidence.IsVoiceIdle(close + Line(-5, "[RTCControlSocket(default)] [CONNECT]"), since, now), "Reconnect mistaken for leave");
}

static FOG.Protocol.ProbeResult Probe(string name, bool ok) => new(name, ok, 1, "test");

static void AssertConnectedProfilePreserved()
{
    var probes = new[] { Probe("gateway", true), Probe("discord-client", true), Probe("www.youtube.com", true), Probe("discord.com", false) };
    Assert(ProfileSearch.PreserveConnectedClient(probes), "Regression: HTTP timeout evicted connected desktop");
    Assert(!ProfileSearch.PreserveConnectedClient(probes.Append(Probe("discord-voice", false)).ToArray()), "Real voice failure masked");
    Assert(!ProfileSearch.PreserveConnectedClient(probes.Where(p => p.Name != "discord-client").ToArray()), "Synthetic Gateway alone counted as desktop");
}

static void AssertBestProfileSearch()
{
    var search = new ProfileSearch();
    search.Record("first", new[] { Probe("gateway", true), Probe("www.youtube.com", true), Probe("discord.com", false) });
    search.Record("last", new[] { Probe("gateway", false), Probe("www.youtube.com", true), Probe("discord.com", false) });
    Assert(search.Best("last") == "first", "Search retained worse final candidate");
    search.Record("last", new[] { Probe("gateway", true), Probe("www.youtube.com", true) });
    Assert(search.Best("last") == "last", "Tie caused needless restart");
    search.Record("first", new[] { Probe("gateway", false), Probe("www.youtube.com", false) });
    Assert(search.Best("first") == "last", "Stale earlier success retained");
    search.Reset();
    Assert(search.Best("last") is null, "Old network scores leaked into new search");
}

static void AssertVoiceSearchRequirement()
{
    var search = new ProfileSearch();
    search.IncludeVoiceRequirement(new[] { Probe("discord-voice", false) });
    var next = search.IncludeVoiceRequirement(new[] { Probe("gateway", true) });
    Assert(next.Any(p => p.Name == "discord-voice" && !p.Ok), "Changed profile hid unknown voice state");
    var ready = search.IncludeVoiceRequirement(new[] { Probe("discord-voice", true) });
    Assert(ready.All(p => p.Ok), "Confirmed voice rejected");
    var idle = search.IncludeVoiceRequirement(new[] { Probe("discord-voice-idle", true) });
    Assert(!search.VoiceRequired && idle.All(p => p.Ok), "Leaving channel caused endless voice repair");
}

static void AssertVpnMemoryIsolation()
{
    Assert(TransportPolicy.CanRememberEngineProfile(null), "Direct Engine result rejected");
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo("VPN", "WireGuard Tunnel", System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,
            System.Net.NetworkInformation.OperationalStatus.Up)));
    Assert(!TransportPolicy.CanRememberEngineProfile(detector.FindActiveTunnel()), "VPN result can contaminate Engine memory");
}

static void AssertGatewaySession()
{
    foreach (var (reply, valid) in new[] { ("{\"op\":11}", true), ("{\"op\":10}", false), ("[]", false), ("bad", false) })
    {
        using var socket = new FakeGatewaySocket("{\"op\":10,\"d\":{\"heartbeat_interval\":41250}}", reply);
        var ok = ConnectionHealthChecker.ValidateGatewaySessionAsync(socket, CancellationToken.None).GetAwaiter().GetResult();
        Assert(ok == valid, "Incorrect ACK classification");
        Assert(socket.Sent == "{\"op\":1,\"d\":null}", "Heartbeat payload incorrect");
    }
    using var invalid = new FakeGatewaySocket("{\"op\":11}");
    Assert(!ConnectionHealthChecker.ValidateGatewaySessionAsync(invalid, CancellationToken.None).GetAwaiter().GetResult(), "ACK before HELLO accepted");
    Assert(invalid.Sent is null, "Sent heartbeat before HELLO");
}

static void AssertGatewayBounds()
{
    using var oversized = new FakeGatewaySocket(new string('x', 9000));
    Assert(!ConnectionHealthChecker.ValidateGatewaySessionAsync(oversized, CancellationToken.None).GetAwaiter().GetResult(), "Oversized HELLO accepted");
    using var stalled = new FakeGatewaySocket("{\"op\":10,\"d\":{\"heartbeat_interval\":41250}}");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    try
    {
        ConnectionHealthChecker.ValidateGatewaySessionAsync(stalled, timeout.Token).GetAwaiter().GetResult();
        Assert(false, "Missing ACK must not succeed");
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
}

static void AssertGatewayHello()
{
    foreach (var (body, expected) in new[] {
        ("{\"op\":10,\"d\":{\"heartbeat_interval\":41250}}", true),
        ("{\"op\":0,\"d\":{\"heartbeat_interval\":41250}}", false),
        ("{\"op\":\"10\",\"d\":{\"heartbeat_interval\":41250}}", false),
        ("{\"op\":10,\"d\":{\"heartbeat_interval\":-1}}", false),
        ("{}", false), ("[]", false), ("<html>blocked</html>", false)
    })
    {
        Assert(ConnectionHealthChecker.IsGatewayHello(System.Text.Encoding.UTF8.GetBytes(body)) == expected, "Invalid HELLO classification");
    }
}

static void AssertBootstrapStrategy()
{
    using var resource = typeof(ProfileCatalog).Assembly.GetManifestResourceStream("FOG.Agent.TrustedProfiles.json")!;
    var profiles = System.Text.Json.JsonSerializer.Deserialize<FOG.Protocol.EngineProfile[]>(resource,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    var profile = profiles.OrderBy(item => item.Priority).First();
    Assert(profile.Id == "fog-connect-1102", "Hotfix strategy is not the first candidate");
    Assert(profiles.Length == 4, "Unexpected expansion of automatic search");
    Assert(!profile.Arguments.Any(argument => argument.StartsWith("--ip-id=")), "Bootstrap profile forces a global IP-ID mode");
    var rules = new List<List<string>> { new() };
    foreach (var argument in profile.Arguments)
    {
        if (argument == "--new") rules.Add(new());
        else rules[^1].Add(argument);
    }
    var downloadIndex = rules.FindIndex(rule => rule.Any(value => value.Contains("stable.dl2.discordapp.net")));
    var genericIndex = rules.FindIndex(rule => rule.Contains("--dpi-desync=fake,hostfakesplit"));
    Assert(downloadIndex >= 0 && genericIndex > downloadIndex, "Download-specific rule must precede generic Discord rule");
    Assert(rules[downloadIndex].Contains("--dpi-desync=hostfakesplit"), "Missing download transport strategy");
    var voice = rules.Single(rule => rule.Contains("--filter-l7=discord,stun"));
    Assert(voice.Contains("--dpi-desync-repeats=5") &&
        voice.Contains("--dpi-desync-fake-discord=${runtime}\\ACTIVE_DISCORD_UDP.bin") &&
        voice.Contains("--dpi-desync-fake-stun=${runtime}\\ACTIVE_DISCORD_UDP.bin"), "Upstream voice parameters differ");
    var tls = rules[genericIndex];
    Assert(tls.Where(value => value.StartsWith("--dpi-desync-fake-tls=")).SequenceEqual(new[] {
        "--dpi-desync-fake-tls=${runtime}\\tls_clienthello_sochi_park.bin", "--dpi-desync-fake-tls=${runtime}\\stun2.bin" }),
        "TLS payload order differs from upstream");
    Assert(tls.Contains("--dpi-desync-hostfakesplit-mod=host=mail.ru,altorder=1"), "Incorrect TLS host split");
    Assert(rules.Any(rule => rule.Contains("--hostlist-domains=discord.media") && rule.Contains("--dpi-desync-repeats=7") &&
        rule.Contains("--dpi-desync-split-seqovl=681")), "Media TLS rule missing");
    Assert(profile.Arguments.All(argument => !argument.Contains("--ipset") && !argument.Contains("--dpi-desync-any-protocol")),
        "Bootstrap strategy broadens interception beyond target services");
}

static void AssertPipeServerIdentity()
{
    var name = "fog-test-" + Guid.NewGuid().ToString("N");
    using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
        System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var accept = server.WaitForConnectionAsync(timeout.Token);
    using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut);
    client.Connect(3000);
    accept.GetAwaiter().GetResult();
    FOG.WebView2.PipeServerIdentity.Verify(client, Environment.ProcessId);
    try
    {
        FOG.WebView2.PipeServerIdentity.Verify(client, int.MaxValue);
        throw new Exception("Mismatched server was trusted");
    }
    catch (InvalidDataException) { }
}

static void AssertCrashCleanup()
{
    using var owner = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--lifetime-owner")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
    })!;
    Process? child = null;
    try
    {
        var line = owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        child = Process.GetProcessById(int.Parse(line!));
        owner.Kill(entireProcessTree: false);
        Assert(owner.WaitForExit(5000), "Owner did not exit");
        Assert(child.WaitForExit(5000), "Job child survived owner termination");
    }
    finally
    {
        if (!owner.HasExited) owner.Kill(entireProcessTree: true);
        if (child is not null)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            child.Dispose();
        }
    }
}

static void AssertHttpExpectations()
{
    var api = new Uri("https://discord.com/api/v10/gateway");
    var youtube = new Uri("https://www.youtube.com/generate_204");
    var cdn = new Uri("https://cdn.discordapp.com/embed/avatars/0.png");
    foreach (var status in new[] { 200, 204, 301, 401, 403, 404, 429, 500 })
    {
        Assert(ConnectionHealthChecker.IsExpectedHttpStatus(api, (System.Net.HttpStatusCode)status) == (status == 200), $"API status {status}");
        Assert(ConnectionHealthChecker.IsExpectedHttpStatus(youtube, (System.Net.HttpStatusCode)status) == (status == 204), $"YouTube status {status}");
        Assert(ConnectionHealthChecker.IsExpectedHttpStatus(cdn, (System.Net.HttpStatusCode)status) == (status == 200), $"CDN status {status}");
    }
}

static HttpResponseMessage HealthyProbeResponse(HttpRequestMessage request)
{
    var uri = request.RequestUri!;
    if (uri.AbsolutePath == "/generate_204") return new(System.Net.HttpStatusCode.NoContent);
    if (uri.Host == "discord.com") return new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent("{\"url\":\"wss://gateway.discord.gg\"}")
    };
    Assert(uri.AbsoluteUri == "https://cdn.discordapp.com/embed/avatars/0.png", "Unexpected media/root probe");
    return new(System.Net.HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
    };
}

static void AssertServiceProbeIdentities()
{
    var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
    using var checker = new ConnectionHealthChecker(new ScenarioHttpHandler((request, token) =>
    {
        seen.Add(request.RequestUri!.Host);
        return Task.FromResult(HealthyProbeResponse(request));
    }), (host, port, token, timeout) =>
    {
        Assert(host == "gateway.discord.gg" && port == 443, "Gateway was replaced by an unrelated host");
        return Task.FromResult(new ConnectionHealthChecker.ProbeAttempt(true, 1, host));
    });
    for (var i = 0; i < 4; i++)
    {
        var results = checker.CheckStartupAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(results.Count == 4 && results.All(probe => probe.Ok), "Healthy service set rejected");
    }
    Assert(!seen.Contains("media.discordapp.net") && !seen.Contains("canary.discord.com"), "Non-equivalent fallback was queried");
    Assert(!seen.Contains("youtubei.googleapis.com") && !seen.Contains("m.youtube.com"), "YouTube health switched to a non-equivalent endpoint");
    using var blocked = new ConnectionHealthChecker(new ScenarioHttpHandler((request, token) =>
        Task.FromResult(request.RequestUri!.Host == "discord.com"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) : HealthyProbeResponse(request))),
        (host, port, token, timeout) => Task.FromResult(new ConnectionHealthChecker.ProbeAttempt(true, 1, host)));
    var failed = blocked.CheckStartupAsync(CancellationToken.None).GetAwaiter().GetResult();
    Assert(!failed.Single(probe => probe.Name == "discord.com").Ok, "Blocked primary API was hidden");
    Assert(failed.Single(probe => probe.Name == "www.youtube.com").Ok, "Discord failure hid YouTube result");
    using var invalidPng = new ConnectionHealthChecker(new FakeHttpHandler("<html>blocked</html>"));
    Assert(!invalidPng.CheckHttpAttemptAsync("https://cdn.discordapp.com/embed/avatars/0.png", CancellationToken.None).GetAwaiter().GetResult().Ok,
        "HTML instead of a CDN image was accepted");
}

static void AssertSlowProbeConfirmation()
{
    using var checker = new ConnectionHealthChecker(new ScenarioHttpHandler(async (request, token) =>
    {
        await Task.Delay(2200, token);
        return HealthyProbeResponse(request);
    }));
    const string endpoint = "https://discord.com/api/v10/gateway";
    Assert(!checker.CheckHttpAttemptAsync(endpoint, CancellationToken.None, ConnectionHealthChecker.StartupAttemptTimeout).GetAwaiter().GetResult().Ok,
        "Fast deadline not applied");
    Assert(checker.CheckHttpAttemptAsync(endpoint, CancellationToken.None, ConnectionHealthChecker.ConfirmationTimeout).GetAwaiter().GetResult().Ok,
        "Slow but valid API response rejected");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try
    {
        checker.CheckHttpAttemptAsync(endpoint, cancelled.Token).GetAwaiter().GetResult();
        throw new Exception("User cancellation swallowed");
    }
    catch (OperationCanceledException) { }
}

static void AssertTransientProbeRecovery()
{
    var recovered = ConnectionHealthChecker.CheckStableAsync("test", attempt =>
        Task.FromResult(new ConnectionHealthChecker.ProbeAttempt(attempt == 1, 1, "attempt"))).GetAwaiter().GetResult();
    Assert(recovered.Ok, "Successful retry was rejected");
    var failed = ConnectionHealthChecker.CheckStableAsync("test", attempt =>
        Task.FromResult(new ConnectionHealthChecker.ProbeAttempt(false, 1, "attempt"))).GetAwaiter().GetResult();
    Assert(!failed.Ok, "Repeated failures accepted");
}

static void AssertSessionRecovery()
{
    var policy = new SessionRecoveryPolicy();
    var now = DateTimeOffset.UtcNow;
    Assert(!policy.Observe(false, now), "First failure restarted session");
    Assert(!policy.Observe(false, now.AddSeconds(30)), "Early restart");
    Assert(policy.Observe(false, now.AddSeconds(60)), "Persistent failure not recovered");
    for (var i = 1; i < 10; i++)
    {
        Assert(!policy.Observe(false, now.AddSeconds(60 + i * 20)), "Recovery ignored cooldown");
    }
    Assert(!policy.Observe(true, now.AddSeconds(300)), "Healthy session restarted");
    Assert(!policy.Observe(false, now.AddSeconds(400)), "Healthy observation did not reset failures");
}

static void AssertBlocked(string processName)
{
    var guard = new CompatibilityGuard(new FakeSignalSource(processName));
    var result = guard.Check();
    Assert(result.IsActive, $"{processName} was not detected.");
    Assert(result.Reason == "protectedGame", "Unexpected compatibility reason.");
}

static void AssertUnrelatedProcessesAreAllowed()
{
    var guard = new CompatibilityGuard(new FakeSignalSource("Discord", "steam", "explorer", "FOG.Engine"));
    Assert(!guard.Check().IsActive, "An unrelated process activated compatibility mode.");
}

static void AssertActiveVpnDetected()
{
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo(
            "SpaceVPN",
            "Wintun Userspace Tunnel",
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
            System.Net.NetworkInformation.OperationalStatus.Up)));
    Assert(detector.FindActiveTunnel()?.Name == "SpaceVPN", "Active VPN adapter was not detected.");
}

static void AssertInactiveVpnIgnored()
{
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo(
            "WireGuard",
            "WireGuard Tunnel",
            System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,
            System.Net.NetworkInformation.OperationalStatus.Down)));
    Assert(detector.FindActiveTunnel() is null, "Inactive VPN adapter activated tunnel mode.");
}

static void AssertWarpVpnDetected()
{
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo(
            "Cloudflare WARP",
            "Cloudflare WARP Interface Tunnel",
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
            System.Net.NetworkInformation.OperationalStatus.Up)));
    Assert(detector.FindActiveTunnel()?.Name == "Cloudflare WARP", "WARP adapter was not detected.");
}

static void AssertGenericTunnelDetected()
{
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo(
            "Private Link",
            "Virtual Tunnel Adapter",
            (System.Net.NetworkInformation.NetworkInterfaceType)53,
            System.Net.NetworkInformation.OperationalStatus.Up)));
    Assert(detector.FindActiveTunnel()?.Name == "Private Link", "Generic tunnel adapter was not detected.");
}

static void AssertBuiltInTunnelIgnored()
{
    var detector = new ExternalTunnelDetector(new FakeAdapterSource(
        new NetworkAdapterInfo(
            "Teredo Tunneling Pseudo-Interface",
            "Microsoft Teredo Tunneling Adapter",
            System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,
            System.Net.NetworkInformation.OperationalStatus.Up)));
    Assert(detector.FindActiveTunnel() is null, "A built-in Windows transition tunnel was treated as a VPN.");
}

static void AssertIndependentVpnSignal()
{
    var snapshot = new FOG.Protocol.AgentSnapshot(
        "ready",
        false,
        true,
        null,
        DateTimeOffset.UtcNow,
        Array.Empty<FOG.Protocol.ProbeResult>(),
        new FOG.Protocol.CompatibilityInfo(false, "protectedGame", "EasyAntiCheat_EOS", null),
        "direct",
        "SpaceVPN");

    Assert(snapshot.Compatibility?.Reason == "protectedGame", "Protected game signal was lost.");
    Assert(snapshot.ActiveTunnel == "SpaceVPN", "VPN signal was lost while a protected game was active.");
    Assert(!snapshot.EngineRunning, "The Engine must remain stopped in direct VPN mode.");
}

static void AssertDirectModePolicy()
{
    var healthy = new[] { new FOG.Protocol.ProbeResult("gateway", true, 10, "ok") };
    var unhealthy = new[] { new FOG.Protocol.ProbeResult("gateway", false, 10, "timeout") };
    var tunnel = new NetworkAdapterInfo(
        "SpaceVPN",
        "Wintun Userspace Tunnel",
        System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,
        System.Net.NetworkInformation.OperationalStatus.Up);

    Assert(!TransportPolicy.CanUseDirectMode(null, healthy), "A normal network was incorrectly allowed to skip FOG Engine.");
    Assert(TransportPolicy.CanUseDirectMode(tunnel, healthy), "A healthy active VPN was not allowed to use direct mode.");
    Assert(!TransportPolicy.CanUseDirectMode(tunnel, unhealthy), "An unhealthy VPN was incorrectly marked ready.");
}

static void AssertEngineProbeIsolation()
{
    var healthy = new[] { new FOG.Protocol.ProbeResult("gateway", true, 10, "ok") };
    var tunnel = new NetworkAdapterInfo(
        "kosmos-tun",
        "sing-tun Tunnel",
        (System.Net.NetworkInformation.NetworkInterfaceType)53,
        System.Net.NetworkInformation.OperationalStatus.Up);

    Assert(
        !TransportPolicy.CanUseDirectMode(tunnel, healthy, engineRunning: true),
        "Health measured through FOG Engine incorrectly disabled the Engine for a stale VPN adapter.");
}

static void AssertAdaptiveHealthIntervals()
{
    Assert(
        TransportPolicy.GetHealthInterval("direct") < TransportPolicy.GetHealthInterval("engine"),
        "Direct VPN mode is not checked faster than a stable Engine session.");
    Assert(
        TransportPolicy.InitialHealthInterval <= TransportPolicy.DirectHealthInterval,
        "The first transition check is delayed longer than the direct-mode interval.");
    Assert(
        TransportPolicy.GetHealthInterval("engine", "recovering") == TransportPolicy.InitialHealthInterval,
        "Background startup qualification is not scheduled promptly.");
}

static void AssertPreflightPolicy()
{
    var tunnel = new NetworkAdapterInfo(
        "SpaceVPN",
        "Wintun Userspace Tunnel",
        System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,
        System.Net.NetworkInformation.OperationalStatus.Up);

    Assert(!TransportPolicy.ShouldProbeBeforeEngine(null), "A normal network still delays Engine startup with a preflight.");
    Assert(TransportPolicy.ShouldProbeBeforeEngine(tunnel), "An active VPN must be checked before direct mode is accepted.");
}

static void AssertAgentCommandTimeouts()
{
    Assert(FOG.WebView2.AgentClient.GetResponseTimeout("start") == TimeSpan.FromSeconds(90), "Start timeout is too short for profile rotation.");
    Assert(FOG.WebView2.AgentClient.GetResponseTimeout("recheck") == TimeSpan.FromSeconds(90), "Recheck timeout is too short for stable probes.");
    Assert(FOG.WebView2.AgentClient.GetResponseTimeout("status") == TimeSpan.FromSeconds(10), "Status timeout must remain bounded.");
    Assert(FOG.WebView2.AgentClient.GetResponseTimeout("shutdown") == TimeSpan.FromSeconds(30), "Shutdown timeout is too short for an in-flight probe.");
}

static void AssertAgentRestartPolicy()
{
    Assert(!FOG.WebView2.AgentClient.ShouldRestartAfterAuthenticationFailure(1), "Agent restarts after a single transient IPC failure.");
    Assert(!FOG.WebView2.AgentClient.ShouldRestartAfterAuthenticationFailure(2), "Agent restarts before the bounded retry threshold.");
    Assert(FOG.WebView2.AgentClient.ShouldRestartAfterAuthenticationFailure(3), "An unresponsive Agent is never restarted.");
}

static void AssertReleaseVersionComparison()
{
    Assert(FOG.WebView2.UpdateChecker.IsNewerVersion("v0.1.8", "0.1.7"), "A newer release was not detected.");
    Assert(!FOG.WebView2.UpdateChecker.IsNewerVersion("v0.1.7", "0.1.7"), "The current release was treated as newer.");
    Assert(!FOG.WebView2.UpdateChecker.IsNewerVersion("invalid", "0.1.7"), "An invalid release tag was accepted.");
}

static void AssertReleaseLinkValidation()
{
    Assert(
        FOG.WebView2.UpdateChecker.IsTrustedReleaseUri(new Uri("https://github.com/DenisRapira/FOG-Prime/releases/tag/v0.1.8")),
        "The official release page was rejected.");
    Assert(
        !FOG.WebView2.UpdateChecker.IsTrustedReleaseUri(new Uri("https://example.com/DenisRapira/FOG-Prime/releases/tag/v9")),
        "A non-GitHub release host was accepted.");
    Assert(
        !FOG.WebView2.UpdateChecker.IsTrustedReleaseUri(new Uri("https://github.com/another/project/releases/tag/v9")),
        "A different GitHub repository was accepted.");
}

static void AssertDegradedProbePolicy()
{
    var degraded = new[] { new FOG.Protocol.ProbeResult("gateway", false, 5000, "timeout") };
    Assert(!TransportPolicy.ShouldRotateProfile(true, degraded), "A live Engine was selected for restart after a probe timeout.");
    Assert(TransportPolicy.ShouldRotateProfile(false, degraded), "A terminated Engine was not selected for profile recovery.");
}

static void AssertBackgroundQualificationPolicy()
{
    var healthy = new[] { new FOG.Protocol.ProbeResult("gateway", true, 40, "ok") };
    var degraded = new[] { new FOG.Protocol.ProbeResult("discord.com", false, 3000, "timeout") };
    Assert(
        !TransportPolicy.ShouldRotatePendingProfile(true, 1, 0, 3, degraded),
        "A single inconclusive check restarted a live Engine.");
    Assert(
        TransportPolicy.ShouldRotatePendingProfile(true, 2, 1, 3, degraded),
        "Two failed readiness checks did not enable an unused reserve profile.");
    Assert(
        !TransportPolicy.ShouldRotatePendingProfile(true, 2, 3, 3, degraded),
        "Startup qualification retried a profile after every profile was attempted.");
    Assert(
        !TransportPolicy.ShouldRotatePendingProfile(true, 2, 1, 3, healthy),
        "A healthy startup profile was selected for rotation.");
    Assert(
        !TransportPolicy.ShouldRotatePendingProfile(false, 2, 1, 3, degraded),
        "Background qualification tried to rotate an Engine that already exited.");
}

static void AssertLongSessionStability()
{
    var profiles = new[]
    {
        new FOG.Protocol.EngineProfile("primary", 10, "FOG.Engine.exe", new[] { "a" }),
        new FOG.Protocol.EngineProfile("reserve", 20, "FOG.Engine.exe", new[] { "b" })
    };
    var rotation = new ProfileRotation();

    for (var check = 0; check < 10_000; check++)
    {
        var probe = new[]
        {
            new FOG.Protocol.ProbeResult(
                "gateway",
                check % 7 != 0,
                check % 7 == 0 ? 5000 : 40,
                check % 7 == 0 ? "timeout" : "ok")
        };
        if (TransportPolicy.ShouldRotateProfile(true, probe))
        {
            rotation.MovePast("primary", profiles);
        }
    }

    Assert(rotation.Order(profiles)[0].Id == "primary", "Transient probes changed the active profile during a long session.");

    var terminated = new[] { new FOG.Protocol.ProbeResult("gateway", false, 0, "engine-exited") };
    if (TransportPolicy.ShouldRotateProfile(false, terminated))
    {
        rotation.MovePast("primary", profiles);
    }

    Assert(rotation.Order(profiles)[0].Id == "reserve", "An actual Engine exit did not select the reserve profile.");
}

static void AssertEngineSessionLease()
{
    var directory = Path.Combine(Path.GetTempPath(), $"fog-lease-test-{Guid.NewGuid():N}");
    var leasePath = Path.Combine(directory, "engine-session.json");
    try
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var store = new EngineSessionLeaseStore(
            leasePath,
            key,
            NullLogger<EngineSessionLeaseStore>.Instance);
        using var process = Process.GetCurrentProcess();
        var executable = Environment.ProcessPath ?? process.MainModule?.FileName
            ?? throw new InvalidOperationException("The test process path is unavailable.");

        store.Save(process, "fog2-balanced", executable);
        var lease = store.TryLoad()
            ?? throw new InvalidOperationException("A valid Engine session lease was rejected.");
        Assert(lease.ProcessId == process.Id, "The recovered Engine PID changed.");
        Assert(lease.ProfileId == "fog2-balanced", "The recovered Engine profile changed.");

        var tampered = JsonNode.Parse(File.ReadAllText(leasePath))?.AsObject()
            ?? throw new InvalidOperationException("The Engine session lease is invalid JSON.");
        tampered["profileId"] = "fog2-compat";
        File.WriteAllText(leasePath, tampered.ToJsonString());
        Assert(store.TryLoad() is null, "A tampered Engine session lease was accepted.");
        Assert(!File.Exists(leasePath), "A tampered Engine session lease was not removed.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void AssertPreferredProfilePersistence()
{
    var directory = Path.Combine(Path.GetTempPath(), $"fog-profile-test-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "preferred-profile.json");
    try
    {
        var store = new PreferredProfileStore(path, NullLogger<PreferredProfileStore>.Instance);
        store.Save("discord-adaptive-tls");

        var reloaded = new PreferredProfileStore(path, NullLogger<PreferredProfileStore>.Instance).TryLoad();
        Assert(reloaded?.ProfileId == "discord-adaptive-tls", "The working profile was not restored after restart.");

        store.Save("fog-voice-prime");
        Assert(store.TryLoad()?.ProfileId == "fog-voice-prime", "A newly working profile did not replace the old preference.");
        store.Save("fog-voice-prime", "network-a:profiles-v2");
        Assert(store.TryLoad("network-a:profiles-v2") is not null, "Matching network lost its preference");
        Assert(store.TryLoad("network-b:profiles-v2") is null, "Profile leaked across network contexts");
        store.Save("fog-voice-prime", "network-a:profiles-v2");
        Assert(store.TryLoad("network-a:profiles-v3") is null, "Changed strategies reused stale preference");
        store.Save("fog-voice-prime");
        var expired = JsonNode.Parse(File.ReadAllText(path))!;
        expired["verifiedAt"] = DateTimeOffset.UtcNow.AddDays(-8);
        File.WriteAllText(path, expired.ToJsonString());
        Assert(store.TryLoad() is null, "Expired preference was reused");
        store.Clear();
        Assert(store.TryLoad() is null, "A failed preferred profile was not cleared.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void AssertFastStartupProbe()
{
    Assert(
        TransportPolicy.EngineLaunchSettleDelay <= TimeSpan.FromMilliseconds(350),
        "The Engine launch path waits too long before returning control to the client.");
    Assert(
        ConnectionHealthChecker.StartupAttemptTimeout <= TimeSpan.FromSeconds(2),
        "The startup probe can still block profile selection for too long.");
    Assert(
        ConnectionHealthChecker.DirectFailureConfirmationDelay <= TimeSpan.FromMilliseconds(500),
        "Direct-mode failure confirmation adds too much delay.");
}

static void AssertProfileRotation()
{
    var profiles = new[]
    {
        new FOG.Protocol.EngineProfile("primary", 10, "FOG.Engine.exe", new[] { "a" }),
        new FOG.Protocol.EngineProfile("reserve-a", 20, "FOG.Engine.exe", new[] { "b" }),
        new FOG.Protocol.EngineProfile("reserve-b", 30, "FOG.Engine.exe", new[] { "c" })
    };
    var rotation = new ProfileRotation();
    Assert(rotation.Order(profiles)[0].Id == "primary", "Primary profile must be first initially.");
    rotation.MovePast("primary", profiles);
    Assert(rotation.Order(profiles)[0].Id == "reserve-a", "First reserve profile was not selected.");
    rotation.MovePast("reserve-a", profiles);
    Assert(rotation.Order(profiles)[0].Id == "reserve-b", "Second reserve profile was not selected.");
}

static void AssertDriverBackoff()
{
    var backoff = new DriverConflictBackoff();
    var now = DateTimeOffset.Parse("2026-07-22T00:00:00Z");
    Assert(backoff.RecordFailure(now) == now.AddSeconds(2), "First delay must be 2 seconds.");
    Assert(backoff.RecordFailure(now.AddSeconds(1)) == now.AddSeconds(11), "Second delay must be 10 seconds.");
    Assert(backoff.RecordFailure(now.AddSeconds(2)) == now.AddMinutes(1).AddSeconds(2), "Third delay must be 1 minute.");
    Assert(backoff.TryGetActivePause(now.AddSeconds(30), out _), "Backoff must still be active.");
}

static void AssertDriverBackoffReset()
{
    var backoff = new DriverConflictBackoff();
    var now = DateTimeOffset.UtcNow;
    backoff.RecordFailure(now);
    backoff.Reset();
    Assert(!backoff.TryGetActivePause(now, out _), "Backoff remained active after reset.");
}

static void AssertOwnedDriverPath()
{
    var expected = @"C:\Apps\FOG Prime\runtime\WinDivert64.sys";
    Assert(
        WinDivertServiceManager.IsOwnedImagePath(@"\??\C:\Apps\FOG Prime\runtime\WinDivert64.sys", expected),
        "The current package driver path was not recognized.");
    Assert(
        !WinDivertServiceManager.IsOwnedImagePath(@"\??\C:\Other\WinDivert64.sys", expected),
        "An unrelated WinDivert service was accepted for cleanup.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class FakeSignalSource(params string[] signals) : ICompatibilitySignalSource
{
    public IReadOnlyCollection<string> GetActiveSignals() => signals;
}

internal sealed class FakeGatewaySocket(params string[] messages) : System.Net.WebSockets.WebSocket
{
    private readonly Queue<byte[]> _messages = new(messages.Select(System.Text.Encoding.UTF8.GetBytes));
    private int _offset;
    public string? Sent { get; private set; }
    public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;
    public override void Abort() { }
    public override void Dispose() { }
    public override Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
    public override Task CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
    public override Task SendAsync(ArraySegment<byte> buffer, System.Net.WebSockets.WebSocketMessageType type, bool end, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Sent = System.Text.Encoding.UTF8.GetString(buffer);
        return Task.CompletedTask;
    }
    public override async Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
    {
        if (_messages.Count == 0) await Task.Delay(Timeout.Infinite, token);
        token.ThrowIfCancellationRequested();
        var message = _messages.Peek();
        var count = Math.Min(Math.Min(buffer.Count, 17), message.Length - _offset);
        message.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
        _offset += count;
        var end = _offset == message.Length;
        if (end) { _messages.Dequeue(); _offset = 0; }
        return new System.Net.WebSockets.WebSocketReceiveResult(count, System.Net.WebSockets.WebSocketMessageType.Text, end);
    }
}

internal sealed class FakeAdapterSource(params NetworkAdapterInfo[] adapters) : INetworkAdapterSource
{
    public IReadOnlyCollection<NetworkAdapterInfo> GetAdapters() => adapters;
}

internal sealed class FakeHttpHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
}

internal sealed class ScenarioHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
