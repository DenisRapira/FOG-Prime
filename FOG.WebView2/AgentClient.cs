using FOG.Protocol;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.WebView2;

internal sealed class AgentClient
{
    private const string AgentHashResource = "FOG.WebView2.TrustedAgent.sha256";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _startupGate = new(1, 1);
    private readonly string _sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _agentPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "FOG.Agent.exe"));
    private readonly string _enginePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "runtime", "FOG.Engine.exe"));
    private Process? _spawnedAgent;
    private bool _sessionPrepared;

    public async Task<AgentResponse> SendAsync(string command, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);
        return await SendConnectedAsync(command, cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendConnectedAsync("shutdown", cancellationToken);
        }
        catch (Exception ex)
        {
            // Process cleanup below also covers an unresponsive local Agent.
            DiagnosticLog.Write("UI", "Agent did not accept the graceful shutdown request", ex);
        }

        if (_spawnedAgent is { HasExited: false } spawned)
        {
            try
            {
                await spawned.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                TryTerminate(spawned);
            }
        }

        TerminateOwnedProcesses("FOG.Engine", _enginePath);
        TerminateOwnedProcesses("FOG.Agent", _agentPath);
        _spawnedAgent?.Dispose();
        _spawnedAgent = null;
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        await _startupGate.WaitAsync(cancellationToken);
        try
        {
            if (_spawnedAgent is { HasExited: false } && await CanAuthenticateAsync(cancellationToken))
            {
                return;
            }

            if (_spawnedAgent is { HasExited: false })
            {
                throw new TimeoutException("FOG Prime Agent is busy and will be checked again.");
            }

            if (!_sessionPrepared)
            {
                _sessionPrepared = true;
                await StopLegacyAgentsAsync(cancellationToken);
                TerminateOwnedProcesses("FOG.Engine", _enginePath);
                TerminateOwnedProcesses("FOG.Agent", _agentPath);
                await Task.Delay(300, cancellationToken);
            }

            await VerifyAgentIntegrityAsync(_agentPath, cancellationToken);
            DiagnosticLog.Write("UI", "Agent integrity check passed");
            if (_spawnedAgent is null or { HasExited: true })
            {
                _spawnedAgent?.Dispose();
                var startInfo = new ProcessStartInfo
                {
                    FileName = _agentPath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };
                startInfo.Environment["FOG_PRIME_SESSION_TOKEN"] = _sessionToken;
                _spawnedAgent = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("FOG Prime Agent could not be started.");
                AttachDiagnostics(_spawnedAgent);
                DiagnosticLog.Write("UI", $"Agent process started; pid={_spawnedAgent.Id}");
            }

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (_spawnedAgent.HasExited)
                {
                    DiagnosticLog.Write("UI", $"Agent exited during startup; code={_spawnedAgent.ExitCode}");
                    throw new InvalidOperationException($"FOG Prime Agent exited with code {_spawnedAgent.ExitCode}.");
                }

                if (await CanAuthenticateAsync(cancellationToken))
                {
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }

            if (_spawnedAgent is { } timedOutAgent)
            {
                TryTerminate(timedOutAgent);
                timedOutAgent.Dispose();
                _spawnedAgent = null;
            }

            throw new TimeoutException("FOG Prime Agent did not become available.");
        }
        finally
        {
            _startupGate.Release();
        }
    }

    private static async Task StopLegacyAgentsAsync(CancellationToken cancellationToken)
    {
        foreach (var pipeName in new[] { AgentProtocol.LegacyPipeName, AgentProtocol.LegacyPipeNameV1 })
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(250, cancellationToken);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new AgentRequest("shutdown"), Json));
                await reader.ReadLineAsync(cancellationToken).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                // No older Agent is running, or it is already shutting down.
            }
        }
    }

    private async Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendConnectedAsync("ping", cancellationToken, 300);
            return response.Ok && response.Message == "pong";
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private Task<AgentResponse> SendConnectedAsync(string command, CancellationToken cancellationToken) =>
        SendConnectedAsync(command, cancellationToken, 3000);

    private async Task<AgentResponse> SendConnectedAsync(
        string command,
        CancellationToken cancellationToken,
        int connectTimeoutMs)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            AgentProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(connectTimeoutMs, cancellationToken);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var request = new AgentRequest(command, Guid.NewGuid().ToString("N"), _sessionToken);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        var responseTimeout = GetResponseTimeout(command);
        var line = await reader.ReadLineAsync(cancellationToken).AsTask()
            .WaitAsync(responseTimeout, cancellationToken);
        var response = line is null
            ? new AgentResponse(false, "degraded", "Agent did not return a response.", request.CorrelationId)
            : JsonSerializer.Deserialize<AgentResponse>(line, Json)
                ?? new AgentResponse(false, "degraded", "Agent returned invalid data.", request.CorrelationId);

        if (!string.Equals(response.CorrelationId, request.CorrelationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Agent returned a mismatched response.");
        }

        return response;
    }

    internal static TimeSpan GetResponseTimeout(string command) =>
        command.Equals("start", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("recheck", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(90)
            : command.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
              command.Equals("shutdown", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(10);

    private static void AttachDiagnostics(Process process)
    {
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                DiagnosticLog.Write("Agent", eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                DiagnosticLog.Write("Agent", eventArgs.Data);
            }
        };
        process.Exited += (_, _) => DiagnosticLog.Write("UI", $"Agent process exited; code={TryGetExitCode(process)}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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

    private static async Task VerifyAgentIntegrityAsync(string agentPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(agentPath))
        {
            throw new FileNotFoundException("FOG Prime Agent is not installed.", agentPath);
        }

        await using var resource = typeof(AgentClient).Assembly.GetManifestResourceStream(AgentHashResource)
            ?? throw new InvalidDataException("Trusted Agent hash is missing from this build.");
        using var reader = new StreamReader(resource, Encoding.ASCII);
        var expected = (await reader.ReadToEndAsync(cancellationToken)).Trim();
        if (expected.Length != 64)
        {
            throw new InvalidDataException("Trusted Agent hash is invalid.");
        }

        await using var stream = File.OpenRead(agentPath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("FOG Prime Agent integrity check failed.");
        }
    }

    private static void TerminateOwnedProcesses(string processName, string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (HasExecutablePath(process, expectedPath))
                {
                    TryTerminate(process);
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

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process exited between enumeration and cleanup.
        }
    }
}
