using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace FOG.Agent;

public sealed class EngineSupervisor(
    IOptions<AgentOptions> options,
    RuntimeIntegrityVerifier integrityVerifier,
    ProfileCatalog catalog,
    ConnectionHealthChecker healthChecker,
    ILogger<EngineSupervisor> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _engineProcess;
    private EngineProfile? _activeProfile;
    private IReadOnlyList<ProbeResult> _lastProbes = Array.Empty<ProbeResult>();
    private volatile bool _desiredRunning;

    public async Task<AgentSnapshot> StartBestAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _desiredRunning = true;
            if (IsRunning)
            {
                _lastProbes = await healthChecker.CheckStableAsync(cancellationToken);
                if (AllHealthy(_lastProbes))
                {
                    return CreateSnapshot("ready");
                }
            }

            return await StartBestInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _desiredRunning = false;
            await StopInternalAsync(cancellationToken);
            return CreateSnapshot("stopped");
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
            return await RefreshHealthInternalAsync(cancellationToken);
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
            if (!_desiredRunning)
            {
                return CreateSnapshot("stopped");
            }

            if (_desiredRunning && !IsRunning)
            {
                logger.LogWarning("FOG Engine is not running; starting automatic recovery");
                return await StartBestInternalAsync(cancellationToken);
            }

            return await RefreshHealthInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AgentSnapshot> StartBestInternalAsync(CancellationToken cancellationToken)
    {
        await integrityVerifier.VerifyAsync(cancellationToken);
        await StopStaleEnginesAsync(cancellationToken);

        EngineProfile? bestProfile = null;
        var bestScore = -1;
        foreach (var profile in catalog.All)
        {
            await StopInternalAsync(cancellationToken);
            StartProfile(profile);
            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
            _lastProbes = await healthChecker.CheckStableAsync(cancellationToken);

            var score = IsRunning ? _lastProbes.Count(probe => probe.Ok) : -1;
            if (score > bestScore)
            {
                bestScore = score;
                bestProfile = profile;
            }

            if (IsRunning && AllHealthy(_lastProbes))
            {
                return CreateSnapshot("ready");
            }
        }

        await StopInternalAsync(cancellationToken);
        var selected = bestProfile ?? catalog.All[0];
        StartProfile(selected);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
        _lastProbes = await healthChecker.CheckStableAsync(cancellationToken);
        return CreateSnapshot(IsRunning && AllHealthy(_lastProbes) ? "ready" : "degraded");
    }

    private async Task<AgentSnapshot> RefreshHealthInternalAsync(CancellationToken cancellationToken)
    {
        _lastProbes = await healthChecker.CheckAsync(cancellationToken);
        return CreateSnapshot(IsRunning && AllHealthy(_lastProbes) ? "ready" : IsRunning ? "degraded" : "stopped");
    }

    private void StartProfile(EngineProfile profile)
    {
        var runtimeDirectory = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
        var executable = Path.GetFullPath(Path.Combine(runtimeDirectory, profile.Executable));
        var relative = Path.GetRelativePath(runtimeDirectory, executable);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || !File.Exists(executable))
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

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Engine process could not be created.");
        _engineProcess = process;
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
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.LogWarning("FOG Engine: {Message}", eventArgs.Data);
            }
        };
        var processId = process.Id;
        process.Exited += (_, _) => logger.LogWarning("FOG Engine process {ProcessId} exited", processId);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _activeProfile = profile;
        logger.LogInformation("FOG Engine started with profile {ProfileId}", profile.Id);
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        var process = _engineProcess;
        _engineProcess = null;
        _activeProfile = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task StopStaleEnginesAsync(CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName("FOG.Engine"))
        {
            using (process)
            {
                if (_engineProcess is not null && process.Id == _engineProcess.Id)
                {
                    continue;
                }

                try
                {
                    logger.LogWarning("Stopping stale FOG Engine process {ProcessId}", process.Id);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and cleanup.
                }
            }
        }
    }

    private AgentSnapshot CreateSnapshot(string state) => new(
        state,
        IsRunning,
        _lastProbes.Count > 0 && AllHealthy(_lastProbes),
        _activeProfile?.Id,
        DateTimeOffset.UtcNow,
        _lastProbes);

    private bool IsRunning => _engineProcess is { HasExited: false };

    private static bool AllHealthy(IReadOnlyList<ProbeResult> probes) =>
        probes.Count > 0 && probes.All(probe => probe.Ok);

    private static string ExpandRuntimePath(string value, string runtimeDirectory) =>
        value.Replace("${runtime}", runtimeDirectory, StringComparison.OrdinalIgnoreCase);
}
