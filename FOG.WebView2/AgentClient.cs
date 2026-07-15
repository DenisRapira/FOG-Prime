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
        catch (Exception)
        {
            // Process cleanup below also covers an unresponsive local agent.
        }

        if (_spawnedAgent is { HasExited: false } spawned)
        {
            try
            {
                await spawned.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                TryTerminate(spawned);
            }
        }

        TerminateByName("FOG.Engine");
        TerminateByName("FOG.Agent");
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

            if (_spawnedAgent is { HasExited: false } unresponsiveAgent)
            {
                TryTerminate(unresponsiveAgent);
                unresponsiveAgent.Dispose();
                _spawnedAgent = null;
            }

            if (!_sessionPrepared)
            {
                _sessionPrepared = true;
                await StopLegacyAgentsAsync(cancellationToken);
                TerminateByName("FOG.Engine");
                TerminateByName("FOG.Agent");
                await Task.Delay(300, cancellationToken);
            }

            var agentPath = Path.Combine(AppContext.BaseDirectory, "FOG.Agent.exe");
            await VerifyAgentIntegrityAsync(agentPath, cancellationToken);

            if (_spawnedAgent is null or { HasExited: true })
            {
                _spawnedAgent?.Dispose();
                var startInfo = new ProcessStartInfo
                {
                    FileName = agentPath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.Environment["FOG_PRIME_SESSION_TOKEN"] = _sessionToken;
                _spawnedAgent = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("FOG Prime Agent could not be started.");
            }

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (_spawnedAgent.HasExited)
                {
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
                await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(250, cancellationToken);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new AgentRequest("shutdown"), Json));
                await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                // No older agent is running, or it is already shutting down.
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
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private Task<AgentResponse> SendConnectedAsync(string command, CancellationToken cancellationToken) =>
        SendConnectedAsync(command, cancellationToken, 3000);

    private async Task<AgentResponse> SendConnectedAsync(string command, CancellationToken cancellationToken, int connectTimeoutMs)
    {
        await using var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(connectTimeoutMs, cancellationToken);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var request = new AgentRequest(command, Guid.NewGuid().ToString("N"), _sessionToken);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        var line = await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
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

    private static void TerminateByName(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                TryTerminate(process);
            }
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
