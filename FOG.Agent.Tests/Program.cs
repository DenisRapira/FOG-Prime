using FOG.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

if (args is ["--check-update", var currentVersion])
{
    using var checker = new FOG.WebView2.UpdateChecker();
    var update = await checker.CheckAsync(currentVersion, CancellationToken.None);
    Console.WriteLine(update is null ? "no-update" : $"{update.Version} {update.PageUri}");
    return update is null ? 2 : 0;
}

var tests = new (string Name, Action Run)[]
{
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
    ("starts Engine without a normal-network preflight", AssertPreflightPolicy),
    ("allows automatic start to outlive slow probes", AssertAgentCommandTimeouts),
    ("detects newer GitHub release versions", AssertReleaseVersionComparison),
    ("rejects untrusted release links", AssertReleaseLinkValidation),
    ("keeps a running profile during degraded probes", AssertDegradedProbePolicy),
    ("rotates a degraded profile during startup", AssertStartupReadinessPolicy),
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

static void AssertStartupReadinessPolicy()
{
    var healthy = new[] { new FOG.Protocol.ProbeResult("gateway", true, 40, "ok") };
    var degraded = new[] { new FOG.Protocol.ProbeResult("discord.com", false, 3000, "timeout") };
    Assert(!TransportPolicy.ShouldRotateAtStartup(true, healthy), "A ready startup profile was selected for rotation.");
    Assert(TransportPolicy.ShouldRotateAtStartup(true, degraded), "A degraded startup profile blocked reserve selection.");
    Assert(TransportPolicy.ShouldRotateAtStartup(false, healthy), "A terminated startup profile was accepted.");
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
        ConnectionHealthChecker.StartupAttemptTimeout <= TimeSpan.FromSeconds(2),
        "The startup probe can still block profile selection for too long.");
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

internal sealed class FakeAdapterSource(params NetworkAdapterInfo[] adapters) : INetworkAdapterSource
{
    public IReadOnlyCollection<NetworkAdapterInfo> GetAdapters() => adapters;
}
