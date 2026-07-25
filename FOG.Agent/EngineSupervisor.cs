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
    WinDivertServiceManager winDivertService,
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
    private bool _directMode;
    private bool _waitingForTunnel;
    private volatile bool _desiredRunning;

    public async Task<AgentSnapshot> StartBestAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _desiredRunning = true;
            driverBackoff.Reset();
            var tunnel = tunnelDetector.FindActiveTunnel();
            LogTunnelInfo(tunnel, nameof(StartBestAsync));
            if (TransportPolicy.ShouldProbeBeforeEngine(tunnel))
            {
                _lastProbes = await healthChecker.CheckAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(StartBestAsync));
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
                {
                    _directMode = true;
                    _waitingForTunnel = false;
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
            UpdateCompatibility(tunnelDetector.FindActiveTunnel());
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
            if (tunnel is not null)
            {
                if (IsRunning && TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
                {
                    logger.LogInformation("Tunnel {Tunnel} became active and is healthy; releasing WinDivert before health checks", tunnel.Name);
                    await StopInternalAsync(cancellationToken);
                    _directMode = true;
                    _waitingForTunnel = false;
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                if (IsRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} is active but degraded; keeping FOG Engine running for connectivity", tunnel.Name);
                    _directMode = false;
                    _waitingForTunnel = false;
                }
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
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
                {
                    if (IsRunning)
                    {
                        logger.LogInformation("Tunnel {Tunnel} became active and can carry healthy traffic; releasing WinDivert", tunnel.Name);
                        await StopInternalAsync(cancellationToken);
                    }

                    _directMode = true;
                    _waitingForTunnel = false;
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                UpdateCompatibility(tunnel);
                if (_desiredRunning && !IsRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} is active but degraded; starting FOG Engine to restore connectivity", tunnel.Name);
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
                    return await StartBestInternalAsync(cancellationToken);
                }
            }

            UpdateCompatibility();
            if (_desiredRunning && !IsRunning)
            {
                if (driverBackoff.TryGetActivePause(DateTimeOffset.UtcNow, out _))
                {
                    return CreateSnapshot("recovering");
                }

                logger.LogInformation("Restoring FOG Engine automatically");
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
                _lastProbes = await healthChecker.CheckAsync(cancellationToken);
                LogProbeResults(_lastProbes, nameof(RecoverIfNeededAsync));
                if (TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
                {
                    if (IsRunning)
                    {
                        await StopInternalAsync(cancellationToken);
                    }

                    _directMode = true;
                    _waitingForTunnel = false;
                    UpdateCompatibility(tunnel);
                    return CreateSnapshot("ready");
                }

                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility(tunnel);
                if (_desiredRunning && !IsRunning)
                {
                    logger.LogInformation("Tunnel {Tunnel} is active but degraded; starting FOG Engine to recover connectivity", tunnel.Name);
                    return await StartBestInternalAsync(cancellationToken);
                }

                return CreateSnapshot(CurrentState);
            }

            if (_directMode || _waitingForTunnel)
            {
                _directMode = false;
                _waitingForTunnel = false;
                UpdateCompatibility();
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

        _lastProbes = await healthChecker.CheckAsync(cancellationToken);
        LogProbeResults(_lastProbes, nameof(RefreshHealthInternalAsync));
        if (_lastProbes.All(probe => probe.Ok))
        {
            driverBackoff.Reset();
            UpdateCompatibility();
            return CreateSnapshot("ready");
        }

        logger.LogInformation("Connectivity probes are degraded; keeping the running Engine profile unchanged");
        return CreateSnapshot("degraded");
    }

    private async Task<AgentSnapshot> StartBestInternalAsync(CancellationToken cancellationToken)
    {
        UpdateCompatibility();
        if (await DeferToActiveTunnelAsync(cancellationToken) is { } initialTunnelSnapshot)
        {
            return initialTunnelSnapshot;
        }

        await integrityVerifier.VerifyAsync(cancellationToken);
        await StopStaleEnginesAsync(cancellationToken);
        await winDivertService.StopOwnedAsync(cancellationToken);

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
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            if (await DeferToActiveTunnelAsync(cancellationToken) is { } activatedTunnelSnapshot)
            {
                return activatedTunnelSnapshot;
            }

            if (!IsRunning)
            {
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
            CreateSnapshot("recovering");
            _lastProbes = await healthChecker.CheckAsync(cancellationToken);
            if (await DeferToActiveTunnelAsync(cancellationToken) is { } probedTunnelSnapshot)
            {
                return probedTunnelSnapshot;
            }

            if (!TransportPolicy.ShouldRotateProfile(IsRunning, _lastProbes))
            {
                UpdateCompatibility();
                return CreateSnapshot(_lastProbes.All(probe => probe.Ok) ? "ready" : "degraded");
            }

            profileRotation.MovePast(profile.Id, catalog.All);
        }

        await StopInternalAsync(cancellationToken);
        UpdateCompatibility();
        return CreateSnapshot("degraded");
    }

    private async Task<AgentSnapshot?> DeferToActiveTunnelAsync(CancellationToken cancellationToken)
    {
        var tunnel = tunnelDetector.FindActiveTunnel();
        if (tunnel is null || !TransportPolicy.CanUseDirectMode(tunnel, _lastProbes))
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
        UpdateCompatibility(tunnel);
        return CreateSnapshot("ready");
    }

    private void UpdateCompatibility()
    {
        UpdateCompatibility(tunnelDetector.FindActiveTunnel());
    }

    private void UpdateCompatibility(NetworkAdapterInfo? tunnel)
    {
        _activeTunnel = tunnel?.Name;
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

    private void LogProbeResults(IReadOnlyCollection<ProbeResult> probes, string source)
    {
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
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
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
        var instance = new EngineInstance(process, profile.Id);
        _engine = instance;
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.LogDebug("FOG Engine: {Message}", eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            logger.LogWarning("FOG Engine: {Message}", eventArgs.Data);
            if (eventArgs.Data.Contains("windivert: recv failed", StringComparison.OrdinalIgnoreCase))
            {
                RecordDriverConflict(instance, "WinDivert receive failure");
            }
        };
        process.Exited += (_, _) =>
        {
            var code = TryGetExitCode(process);
            if (instance.StopRequested)
            {
                logger.LogDebug("FOG Engine stopped with code {ExitCode}", code);
            }
            else if (code == 232)
            {
                logger.LogWarning("FOG Engine exited with WinDivert code {ExitCode}", code);
                RecordDriverConflict(instance, "WinDivert exit code 232");
            }
            else
            {
                logger.LogWarning("FOG Engine exited unexpectedly with code {ExitCode}", code);
                profileRotation.MovePast(instance.ProfileId, catalog.All);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _activeProfile = profile;
        logger.LogInformation("FOG Engine started with profile {ProfileId}", profile.Id);
    }

    private void RecordDriverConflict(EngineInstance instance, string source)
    {
        if (instance.StopRequested || Interlocked.Exchange(ref instance.DriverConflictRecorded, 1) != 0)
        {
            return;
        }

        var retryAt = driverBackoff.RecordFailure(DateTimeOffset.UtcNow);
        profileRotation.MovePast(instance.ProfileId, catalog.All);
        logger.LogWarning("{Source}; automatic retry scheduled for {RetryAt}", source, retryAt);
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        var instance = _engine;
        if (instance is not null)
        {
            instance.StopRequested = true;
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

    private AgentSnapshot CreateSnapshot(string state)
    {
        var snapshot = new AgentSnapshot(
            state,
            IsRunning,
            !_waitingForTunnel &&
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

    private static string ExpandRuntimePath(string value, string runtimeDirectory)
    {
        return value.Replace("${runtime}", runtimeDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EngineInstance(Process process, string profileId)
    {
        public Process Process { get; } = process;
        public string ProfileId { get; } = profileId;
        public volatile bool StopRequested;
        public int DriverConflictRecorded;
    }
}
