using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;

namespace FOG.Agent;

public sealed class EngineSupervisor(
    IOptions<AgentOptions> options,
    RuntimeIntegrityVerifier integrityVerifier,
    ProfileCatalog catalog,
    ConnectionHealthChecker healthChecker,
    CompatibilityGuard compatibilityGuard,
    ExternalTunnelDetector tunnelDetector,
    DriverConflictBackoff driverBackoff,
    ProfileRotation profileRotation,
    PreferredProfileStore preferredProfileStore,
    WinDivertServiceManager winDivertService,
    EngineSessionLeaseStore sessionLeaseStore,
    ILogger<EngineSupervisor> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EngineInstance? _engine;
    private EngineProfile? _activeProfile;
    private IReadOnlyList<ProbeResult> _lastProbes = Array.Empty<ProbeResult>();
    private AgentSnapshot _latestSnapshot = new(
        "stopped",
        false,
        false,
        null,
        DateTimeOffset.UtcNow,
        Array.Empty<ProbeResult>(),
        null,
        "stopped",
        null);
    private CompatibilityInfo? _compatibility;
    private string? _activeTunnel;
    private string? _activeTunnelIdentity;
    private bool _directMode;
    private bool _waitingForTunnel;
    private volatile bool _desiredRunning;
    private string? _lastTunnelLogSignature;
    private string? _lastProbeLogSignature;
    private DateTimeOffset _lastProbeLogAt;
    private bool _preferredProfileLoaded;
    private string? _rememberedProfileId;
    private bool _startupQualificationPending;
    private int _startupQualificationFailures;
    private DateTimeOffset _nextStartupQualificationAt;
    private readonly HashSet<string> _startupAttemptedProfiles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AgentSnapshot> StartBestAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _desiredRunning = true;
            driverBackoff.Reset();
            if (IsRunning)
            {
                logger.LogInformation("FOG Engine is already running; preserving the active session");
                return CreateSnapshot(CurrentState);
            }

            ResetStartupCycle();

            var tunnel = tunnelDetector.FindActiveTunnel();
            LogTunnelInfo(tunnel, nameof(StartBestAsync));
            if (TransportPolicy.ShouldProbeBeforeEngine(tunnel))
            {
                _lastProbes = await healthChecker.CheckStartupAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(StartBestAsync));
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
                {
                    _directMode = true;
                    _waitingForTunnel = false;
                    CompleteStartupQualification();
                    UpdateCompatibility(tunnel);
                    logger.LogInformation("Tunnel {Tunnel} already carries healthy traffic; WinDivert is not required", tunnel.Name);
                    return CreateSnapshot("ready");
                }

                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility(tunnel);
                logger.LogInformation("Active tunnel {Tunnel} is present but does not deliver all traffic; starting FOG Engine to restore connectivity", tunnel.Name);
                return await StartBestInternalAsync(cancellationToken);
            }

            _directMode = false;
            _waitingForTunnel = false;
            _lastProbes = Array.Empty<ProbeResult>();
            logger.LogInformation("No external tunnel detected; starting FOG Engine without a preliminary network delay");
            return await StartBestInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> StopAsync(CancellationToken cancellationToken)
    {
        _desiredRunning = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _directMode = false;
            _waitingForTunnel = false;
            _compatibility = null;
            _activeTunnel = null;
            _activeTunnelIdentity = null;
            ResetStartupCycle();
            driverBackoff.Reset();
            await StopInternalAsync(cancellationToken);
            return CreateSnapshot("stopped");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return Volatile.Read(ref _latestSnapshot);
        }

        try
        {
            return CreateSnapshot(CurrentState);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> RefreshHealthAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tunnel = tunnelDetector.FindActiveTunnel();
            LogTunnelInfo(tunnel, nameof(RefreshHealthAsync));
            if (tunnel is not null && !IsRunning)
            {
                _lastProbes = await healthChecker.CheckAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(RefreshHealthAsync));
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes, IsRunning))
                {
                    _directMode = true;
                    _waitingForTunnel = false;
                    CompleteStartupQualification();
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility(tunnel);
                if (_desiredRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} failed a fresh direct check; starting FOG Engine", tunnel.Name);
                    ResetStartupCycle();
                    return await StartBestInternalAsync(cancellationToken);
                }

                return CreateSnapshot("degraded");
            }

            UpdateCompatibility(tunnel);
            return await RefreshHealthInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> ReconcileCompatibilityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tunnel = tunnelDetector.FindActiveTunnel();
            LogTunnelInfo(tunnel, nameof(ReconcileCompatibilityAsync));
            if (tunnel is not null)
            {
                var tunnelIdentity = GetTunnelIdentity(tunnel);
                var tunnelChanged = !string.Equals(tunnelIdentity, _activeTunnelIdentity, StringComparison.Ordinal);
                if (tunnelChanged && _desiredRunning && (_directMode || IsRunning || _lastProbes.Count > 0))
                {
                    return await ReevaluateTunnelAsync(tunnel, cancellationToken);
                }

                if (IsStartupQualificationDue())
                {
                    return await RefreshHealthInternalAsync(cancellationToken);
                }

                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes, IsRunning))
                {
                    _directMode = true;
                    _waitingForTunnel = false;
                    CompleteStartupQualification();
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                UpdateCompatibility(tunnel);
                if (_desiredRunning && !IsRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} is active but degraded; starting FOG Engine to restore connectivity", tunnel.Name);
                    ResetStartupCycle();
                    return await StartBestInternalAsync(cancellationToken);
                }

                return CreateSnapshot(CurrentState);
            }

            if (_directMode || _waitingForTunnel)
            {
                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility();
                if (_desiredRunning && !IsRunning)
                {
                    logger.LogInformation("External tunnel closed; starting FOG Engine immediately");
                    ResetStartupCycle();
                    return await StartBestInternalAsync(cancellationToken);
                }
            }

            if (IsStartupQualificationDue())
            {
                return await RefreshHealthInternalAsync(cancellationToken);
            }

            UpdateCompatibility();
            if (_desiredRunning && !IsRunning)
            {
                if (driverBackoff.TryGetActivePause(DateTimeOffset.UtcNow, out _))
                {
                    return CreateSnapshot("recovering");
                }

                logger.LogInformation("Restoring FOG Engine automatically");
                ResetStartupCycle();
                return await StartBestInternalAsync(cancellationToken);
            }

            return CreateSnapshot(CurrentState);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> RecoverIfNeededAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tunnel = tunnelDetector.FindActiveTunnel();
            LogTunnelInfo(tunnel, nameof(RecoverIfNeededAsync));
            if (tunnel is not null)
            {
                _lastProbes = IsRunning
                    ? _startupQualificationPending
                        ? await healthChecker.CheckStartupAsync(cancellationToken)
                        : await healthChecker.CheckAsync(cancellationToken)
                    : await healthChecker.CheckDirectAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(RecoverIfNeededAsync));
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes, IsRunning))
                {
                    _directMode = true;
                    _waitingForTunnel = false;
                    CompleteStartupQualification();
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility(tunnel);
                if (IsRunning)
                {
                    return await ApplyEngineProbeResultsAsync(
                        "Tunnel fallback probes are degraded; keeping the running Engine profile unchanged",
                        cancellationToken);
                }

                if (_desiredRunning && !IsRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} is active but degraded; starting FOG Engine to recover connectivity", tunnel.Name);
                    ResetStartupCycle();
                    return await StartBestInternalAsync(cancellationToken);
                }

                return CreateSnapshot(CurrentState);
            }

            if (_directMode || _waitingForTunnel)
            {
                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility();
                ResetStartupCycle();
                return await StartBestInternalAsync(cancellationToken);
            }

            UpdateCompatibility();
            if (_desiredRunning && !IsRunning)
            {
                if (driverBackoff.TryGetActivePause(DateTimeOffset.UtcNow, out _))
                {
                    return CreateSnapshot("recovering");
                }

                logger.LogWarning("FOG Engine is not running; starting automatic recovery");
                ResetStartupCycle();
                return await StartBestInternalAsync(cancellationToken);
            }

            return IsRunning
                ? await RefreshHealthInternalAsync(cancellationToken)
                : CreateSnapshot("stopped");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AgentSnapshot> RefreshHealthInternalAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            if (_directMode)
            {
                _lastProbes = await healthChecker.CheckAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(RefreshHealthInternalAsync));
                return CreateSnapshot(_lastProbes.All(probe => probe.Ok) ? "ready" : "degraded");
            }

            return CreateSnapshot(_desiredRunning ? "recovering" : "stopped");
        }

        _lastProbes = _startupQualificationPending
            ? await healthChecker.CheckStartupAsync(cancellationToken)
            : await healthChecker.CheckAsync(cancellationToken);
        LogProbeResults(_lastProbes, nameof(RefreshHealthInternalAsync));
        return await ApplyEngineProbeResultsAsync(
            "Connectivity probes are degraded; keeping the running Engine profile unchanged",
            cancellationToken);
    }

    private async Task<AgentSnapshot> ApplyEngineProbeResultsAsync(
        string stableSessionMessage,
        CancellationToken cancellationToken)
    {
        if (_lastProbes.Count > 0 && _lastProbes.All(probe => probe.Ok))
        {
            driverBackoff.Reset();
            if (_activeProfile is { } activeProfile)
            {
                RememberSuccessfulProfile(activeProfile.Id);
            }
            CompleteStartupQualification();
            UpdateCompatibility();
            return CreateSnapshot("ready");
        }

        if (_startupQualificationPending && _activeProfile is { } pendingProfile)
        {
            _startupQualificationFailures++;
            if (_startupQualificationFailures < 2)
            {
                _nextStartupQualificationAt = DateTimeOffset.UtcNow + TransportPolicy.InitialHealthInterval;
                logger.LogInformation(
                    "Profile {ProfileId} readiness check was inconclusive; keeping the live Engine and confirming once more",
                    pendingProfile.Id);
                UpdateCompatibility();
                return CreateSnapshot("recovering");
            }

            _startupAttemptedProfiles.Add(pendingProfile.Id);
            if (TransportPolicy.ShouldRotatePendingProfile(
                IsRunning,
                _startupQualificationFailures,
                _startupAttemptedProfiles.Count,
                catalog.All.Count,
                _lastProbes))
            {
                ForgetPreferredProfile(pendingProfile.Id);
                profileRotation.MovePast(pendingProfile.Id, catalog.All);
                logger.LogWarning(
                    "Profile {ProfileId} failed two background readiness checks; activating one reserve profile",
                    pendingProfile.Id);
                await StopInternalAsync(cancellationToken);
                return await StartBestInternalAsync(cancellationToken);
            }

            logger.LogWarning(
                "All profiles were qualified once without a complete probe set; keeping the last live Engine and stopping rotation");
            CompleteStartupQualification();
            UpdateCompatibility();
            return CreateSnapshot("degraded");
        }

        logger.LogInformation(stableSessionMessage);
        return CreateSnapshot("degraded");
    }

    private void MarkStartupQualificationPending(EngineProfile profile)
    {
        _activeProfile = profile;
        _startupQualificationPending = true;
        _startupQualificationFailures = 0;
        _nextStartupQualificationAt = DateTimeOffset.UtcNow + TransportPolicy.InitialHealthInterval;
        _lastProbes = Array.Empty<ProbeResult>();
    }

    private void CompleteStartupQualification()
    {
        _startupQualificationPending = false;
        _startupQualificationFailures = 0;
        _nextStartupQualificationAt = default;
        _startupAttemptedProfiles.Clear();
    }

    private bool IsStartupQualificationDue() =>
        _startupQualificationPending &&
        IsRunning &&
        DateTimeOffset.UtcNow >= _nextStartupQualificationAt;

    private void ResetStartupCycle()
    {
        CompleteStartupQualification();
        _lastProbes = Array.Empty<ProbeResult>();
    }

    private async Task<AgentSnapshot> StartBestInternalAsync(CancellationToken cancellationToken)
    {
        UpdateCompatibility();
        if (await DeferToActiveTunnelAsync(cancellationToken) is { } initialTunnelSnapshot)
        {
            return initialTunnelSnapshot;
        }

        await integrityVerifier.VerifyAsync(cancellationToken);
        if (TryAdoptSessionEngine() is { } adoptedProfile)
        {
            profileRotation.Prefer(adoptedProfile.Id);
            MarkStartupQualificationPending(adoptedProfile);
            UpdateCompatibility();
            return CreateSnapshot("recovering");
        }

        await StopStaleEnginesAsync(cancellationToken);
        await winDivertService.StopOwnedAsync(cancellationToken);

        LoadPreferredProfile();
        var orderedProfiles = profileRotation.Order(catalog.All);
        foreach (var profile in orderedProfiles)
        {
            logger.LogInformation("Trying profile {ProfileId} with priority {Priority}", profile.Id, profile.Priority);
            await StopInternalAsync(cancellationToken);
            if (await DeferToActiveTunnelAsync(cancellationToken) is { } tunnelSnapshot)
            {
                return tunnelSnapshot;
            }

            StartProfile(profile);
            await Task.Delay(TransportPolicy.EngineLaunchSettleDelay, cancellationToken);
            if (await DeferToActiveTunnelAsync(cancellationToken) is { } activatedTunnelSnapshot)
            {
                return activatedTunnelSnapshot;
            }

            if (!IsRunning)
            {
                ForgetPreferredProfile(profile.Id);
                profileRotation.MovePast(profile.Id, catalog.All);
                if (driverBackoff.TryGetActivePause(DateTimeOffset.UtcNow, out _))
                {
                    UpdateCompatibility();
                    return CreateSnapshot("recovering");
                }

                continue;
            }

            _activeProfile = profile;
            profileRotation.Prefer(profile.Id);
            MarkStartupQualificationPending(profile);
            UpdateCompatibility();
            logger.LogInformation(
                "Profile {ProfileId} is active; readiness qualification will continue in the background",
                profile.Id);
            return CreateSnapshot("recovering");
        }

        if (IsRunning)
        {
            logger.LogWarning("All profiles are degraded; keeping the last live Engine active for manual verification");
            UpdateCompatibility();
            return CreateSnapshot("degraded");
        }

        await StopInternalAsync(cancellationToken);
        UpdateCompatibility();
        return CreateSnapshot("degraded");
    }

    private async Task<AgentSnapshot?> DeferToActiveTunnelAsync(CancellationToken cancellationToken)
    {
        var tunnel = tunnelDetector.FindActiveTunnel();
        if (tunnel is null || !TransportPolicy.CanUseDirectMode(tunnel, _lastProbes, IsRunning))
        {
            if (tunnel is not null)
            {
                logger.LogInformation("Tunnel {Tunnel} is active but not healthy enough for direct mode; keeping FOG Engine active", tunnel.Name);
            }
            return null;
        }

        if (IsRunning)
        {
            logger.LogInformation("Tunnel {Tunnel} became active and can carry healthy traffic; stopping WinDivert immediately", tunnel.Name);
            await StopInternalAsync(cancellationToken);
        }

        _directMode = true;
        _waitingForTunnel = false;
        CompleteStartupQualification();
        UpdateCompatibility(tunnel);
        return CreateSnapshot("ready");
    }

    private async Task<AgentSnapshot> ReevaluateTunnelAsync(
        NetworkAdapterInfo tunnel,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Tunnel topology changed for {Tunnel}; verifying the new route before selecting transport", tunnel.Name);
        if (IsRunning)
        {
            await StopInternalAsync(cancellationToken);
        }

        _directMode = false;
        _waitingForTunnel = false;
        _lastProbes = await healthChecker.CheckStartupAsync(cancellationToken);
        LogProbeResults(_lastProbes, nameof(ReevaluateTunnelAsync));
        if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes, IsRunning))
        {
            _directMode = true;
            CompleteStartupQualification();
            UpdateCompatibility(tunnel);
            logger.LogInformation("Tunnel {Tunnel} passed the direct handoff check", tunnel.Name);
            return CreateSnapshot("ready");
        }

        UpdateCompatibility(tunnel);
        if (_desiredRunning)
        {
            logger.LogInformation("Tunnel {Tunnel} did not pass the direct handoff check; starting FOG Engine", tunnel.Name);
            ResetStartupCycle();
            return await StartBestInternalAsync(cancellationToken);
        }

        return CreateSnapshot("degraded");
    }

    private void UpdateCompatibility()
    {
        UpdateCompatibility(tunnelDetector.FindActiveTunnel());
    }

    private void UpdateCompatibility(NetworkAdapterInfo? tunnel)
    {
        _activeTunnel = tunnel?.Name;
        _activeTunnelIdentity = GetTunnelIdentity(tunnel);
        var detected = compatibilityGuard.Check();
        if (detected.IsActive)
        {
            _compatibility = new CompatibilityInfo(false, detected.Reason, detected.Signal, null);
            return;
        }

        if (tunnel is not null)
        {
            _compatibility = new CompatibilityInfo(false, "externalTunnel", tunnel.Name, null);
            return;
        }

        _compatibility = driverBackoff.TryGetActivePause(DateTimeOffset.UtcNow, out var retryAt)
            ? new CompatibilityInfo(false, "driverRecovery", null, retryAt)
            : null;
    }

    private void LogTunnelInfo(NetworkAdapterInfo? tunnel, string source)
    {
        var signature = tunnel is null
            ? "none"
            : GetTunnelIdentity(tunnel);
        if (string.Equals(signature, _lastTunnelLogSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastTunnelLogSignature = signature;
        if (tunnel is null)
        {
            logger.LogInformation("{Source}: no external tunnel detected", source);
            return;
        }

        logger.LogInformation(
            "{Source}: active external tunnel detected: {TunnelName} ({Description}); type={Type}; status={Status}",
            source,
            tunnel.Name,
            tunnel.Description,
            tunnel.Type,
            tunnel.Status);
    }

    private static string? GetTunnelIdentity(NetworkAdapterInfo? tunnel)
    {
        return tunnel is null
            ? null
            : string.Join('|', tunnel.Name, tunnel.Description, tunnel.Type, tunnel.Status, tunnel.Id, tunnel.ConnectionSignature);
    }

    private void LogProbeResults(IReadOnlyCollection<ProbeResult> probes, string source)
    {
        var now = DateTimeOffset.UtcNow;
        var signature = probes.Count == 0
            ? "none"
            : string.Join('|', probes.Select(probe => $"{probe.Name}:{probe.Ok}:{probe.Detail}"));
        if (string.Equals(signature, _lastProbeLogSignature, StringComparison.Ordinal) &&
            now - _lastProbeLogAt < TimeSpan.FromMinutes(2))
        {
            return;
        }

        _lastProbeLogSignature = signature;
        _lastProbeLogAt = now;
        if (probes.Count == 0)
        {
            logger.LogInformation("{Source}: no probe results available", source);
            return;
        }

        logger.LogInformation(
            "{Source}: probe results: {ProbeDetails}",
            source,
            string.Join(", ", probes.Select(probe =>
                $"{probe.Name}={(probe.Ok ? "ok" : "fail")}({probe.LatencyMs}ms; {probe.Detail})")));
    }

    private void StartProfile(EngineProfile profile)
    {
        var runtimeDirectory = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var executable = Path.GetFullPath(Path.Combine(runtimeDirectory, profile.Executable));
        var relative = Path.GetRelativePath(runtimeDirectory, executable);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || !File.Exists(executable))
        {
            throw new InvalidDataException("Engine executable is outside the trusted runtime directory.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = runtimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(ExpandRuntimePath(argument, runtimeDirectory));
        }

        logger.LogInformation(
            "Starting FOG Engine profile {ProfileId} with {ArgumentCount} arguments; executable={Executable}",
            profile.Id,
            profile.Arguments.Count,
            executable);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Engine process could not be created.");
        var instance = new EngineInstance(
            process,
            profile.Id,
            new DateTimeOffset(process.StartTime.ToUniversalTime()));
        AttachEngine(instance, profile);
        sessionLeaseStore.Save(process, profile.Id, executable);
        logger.LogInformation(
            "FOG Engine started; pid={ProcessId}; profile={ProfileId}",
            process.Id,
            profile.Id);
    }

    private EngineProfile? TryAdoptSessionEngine()
    {
        var lease = sessionLeaseStore.TryLoad();
        if (lease is null)
        {
            return null;
        }

        var profile = catalog.All.FirstOrDefault(candidate =>
            candidate.Id.Equals(lease.ProfileId, StringComparison.OrdinalIgnoreCase));
        var runtimeDirectory = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var expectedPath = profile is null
            ? null
            : Path.GetFullPath(Path.Combine(runtimeDirectory, profile.Executable));
        if (profile is null ||
            expectedPath is null ||
            !expectedPath.Equals(Path.GetFullPath(lease.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        {
            sessionLeaseStore.Clear(lease.ProcessId);
            return null;
        }

        try
        {
            var process = Process.GetProcessById(lease.ProcessId);
            var processStart = process.StartTime.ToUniversalTime().Ticks;
            if (process.HasExited ||
                Math.Abs(processStart - lease.ProcessStartUtcTicks) > TimeSpan.FromSeconds(2).Ticks ||
                !HasExecutablePath(process, expectedPath))
            {
                process.Dispose();
                sessionLeaseStore.Clear(lease.ProcessId);
                profileRotation.MovePast(profile.Id, catalog.All);
                return null;
            }

            var instance = new EngineInstance(
                process,
                profile.Id,
                new DateTimeOffset(new DateTime(processStart, DateTimeKind.Utc)));
            AttachEngine(instance, profile);
            logger.LogInformation(
                "Adopted running FOG Engine after Agent recovery; pid={ProcessId}; profile={ProfileId}; uptime={UptimeMs}ms",
                process.Id,
                profile.Id,
                (long)(DateTimeOffset.UtcNow - instance.StartedAt).TotalMilliseconds);
            return profile;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            sessionLeaseStore.Clear(lease.ProcessId);
            profileRotation.MovePast(profile.Id, catalog.All);
            logger.LogWarning(ex, "Previous Engine session could not be adopted; a reserve profile will be used");
            return null;
        }
    }

    private void AttachEngine(EngineInstance instance, EngineProfile profile)
    {
        var process = instance.Process;
        _engine = instance;
        _activeProfile = profile;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            sessionLeaseStore.Clear(process.Id);
            var code = TryGetExitCode(process);
            var uptime = DateTimeOffset.UtcNow - instance.StartedAt;
            if (instance.StopRequested)
            {
                logger.LogDebug(
                    "FOG Engine stopped; pid={ProcessId}; profile={ProfileId}; exit={ExitCode} ({ExitCodeHex}); uptime={UptimeMs}ms",
                    process.Id,
                    instance.ProfileId,
                    code,
                    FormatExitCode(code),
                    (long)uptime.TotalMilliseconds);
            }
            else if (code == 232)
            {
                logger.LogWarning(
                    "FOG Engine exited with WinDivert failure; pid={ProcessId}; profile={ProfileId}; exit={ExitCode} ({ExitCodeHex}); uptime={UptimeMs}ms",
                    process.Id,
                    instance.ProfileId,
                    code,
                    FormatExitCode(code),
                    (long)uptime.TotalMilliseconds);
                RecordDriverConflict(instance, "WinDivert exit code 232");
            }
            else
            {
                ForgetPreferredProfile(instance.ProfileId);
                logger.LogWarning(
                    "FOG Engine exited unexpectedly; pid={ProcessId}; profile={ProfileId}; exit={ExitCode} ({ExitCodeHex}); uptime={UptimeMs}ms",
                    process.Id,
                    instance.ProfileId,
                    code,
                    FormatExitCode(code),
                    (long)uptime.TotalMilliseconds);
                profileRotation.MovePast(instance.ProfileId, catalog.All);
            }
        };
    }

    private void RecordDriverConflict(EngineInstance instance, string source)
    {
        if (instance.StopRequested || Interlocked.Exchange(ref instance.DriverConflictRecorded, 1) != 0)
        {
            return;
        }

        var retryAt = driverBackoff.RecordFailure(DateTimeOffset.UtcNow);
        ForgetPreferredProfile(instance.ProfileId);
        profileRotation.MovePast(instance.ProfileId, catalog.All);
        logger.LogWarning("{Source}; automatic retry scheduled for {RetryAt}", source, retryAt);
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        var instance = _engine;
        if (instance is not null)
        {
            instance.StopRequested = true;
            sessionLeaseStore.Clear(instance.Process.Id);
        }

        _engine = null;
        _activeProfile = null;

        if (instance is null)
        {
            return;
        }

        try
        {
            if (!instance.Process.HasExited)
            {
                instance.Process.Kill(entireProcessTree: true);
                await instance.Process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
        finally
        {
            instance.Process.Dispose();
            await winDivertService.StopOwnedAsync(CancellationToken.None);
        }
    }

    private async Task StopStaleEnginesAsync(CancellationToken cancellationToken)
    {
        var runtimeDirectory = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var expectedPath = Path.GetFullPath(Path.Combine(runtimeDirectory, "FOG.Engine.exe"));
        foreach (var process in Process.GetProcessesByName("FOG.Engine"))
        {
            using (process)
            {
                if (_engine is not null && process.Id == _engine.Process.Id)
                {
                    continue;
                }

                if (!HasExecutablePath(process, expectedPath))
                {
                    continue;
                }

                try
                {
                    logger.LogWarning("Stopping stale FOG Engine process {ProcessId}", process.Id);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and cleanup.
                }
            }
        }
    }

    private static bool HasExecutablePath(Process process, string expectedPath)
    {
        try
        {
            var actualPath = process.MainModule?.FileName;
            return actualPath is not null &&
                Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private void LoadPreferredProfile()
    {
        if (_preferredProfileLoaded)
        {
            return;
        }

        _preferredProfileLoaded = true;
        var state = preferredProfileStore.TryLoad();
        var profile = state is null
            ? null
            : catalog.All.FirstOrDefault(candidate =>
                candidate.Id.Equals(state.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            if (state is not null)
            {
                preferredProfileStore.Clear();
            }
            return;
        }

        _rememberedProfileId = profile.Id;
        profileRotation.Prefer(profile.Id);
        logger.LogInformation(
            "Using remembered profile {ProfileId}; last verified at {VerifiedAt}",
            profile.Id,
            state!.VerifiedAt);
    }

    private void RememberSuccessfulProfile(string profileId)
    {
        if (string.Equals(_rememberedProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        preferredProfileStore.Save(profileId);
        _rememberedProfileId = profileId;
        _preferredProfileLoaded = true;
        logger.LogInformation("Remembered working profile {ProfileId} for the next launch", profileId);
    }

    private void ForgetPreferredProfile(string profileId)
    {
        if (!string.Equals(_rememberedProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        preferredProfileStore.Clear();
        _rememberedProfileId = null;
        logger.LogInformation("Remembered profile {ProfileId} is no longer ready; reserve selection is enabled", profileId);
    }

    private AgentSnapshot CreateSnapshot(string state)
    {
        var snapshot = new AgentSnapshot(
            state,
            IsRunning,
            !_waitingForTunnel &&
                !_startupQualificationPending &&
                (IsRunning || _directMode) &&
                _lastProbes.Count > 0 &&
                _lastProbes.All(probe => probe.Ok),
            _activeProfile?.Id,
            DateTimeOffset.UtcNow,
            _lastProbes,
            _compatibility,
            _directMode ? "direct" : _waitingForTunnel ? "tunnel-wait" : IsRunning ? "engine" : "stopped",
            _activeTunnel);
        Volatile.Write(ref _latestSnapshot, snapshot);
        return snapshot;
    }

    private string CurrentState =>
        _waitingForTunnel ? "recovering" :
        _directMode ? (_lastProbes.Count > 0 && _lastProbes.All(probe => probe.Ok) ? "ready" : "degraded") :
        _startupQualificationPending ? "recovering" :
        !IsRunning ? (_desiredRunning ? "recovering" : "stopped") :
        _lastProbes.Count > 0 && _lastProbes.All(probe => probe.Ok) ? "ready" :
        "degraded";

    private bool IsRunning
    {
        get
        {
            try
            {
                return _engine is { Process.HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string FormatExitCode(int? exitCode) =>
        exitCode is { } value ? $"0x{unchecked((uint)value):X8}" : "unknown";

    private static string ExpandRuntimePath(string value, string runtimeDirectory)
    {
        return value.Replace("${runtime}", runtimeDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EngineInstance(Process process, string profileId, DateTimeOffset startedAt)
    {
        public Process Process { get; } = process;
        public string ProfileId { get; } = profileId;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public volatile bool StopRequested;
        public int DriverConflictRecorded;
    }
}
