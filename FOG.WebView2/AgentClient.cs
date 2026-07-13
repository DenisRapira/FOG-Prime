using FOG.Protocol;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FOG.WebView2;

internal sealed class AgentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Process? _spawnedAgent;
    private bool _legacyAgentStopped;

    public async Task<AgentResponse> SendAsync(string command, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);
        await using var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken);

        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var request = new AgentRequest(command, Guid.NewGuid().ToString("N"));
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        var response = await reader.ReadLineAsync(cancellationToken);

        return response is null
            ? new AgentResponse(false, "degraded", "Agent did not return a response.", request.CorrelationId)
            : JsonSerializer.Deserialize<AgentResponse>(response, Json)
                ?? new AgentResponse(false, "degraded", "Agent returned invalid data.", request.CorrelationId);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendConnectedAsync("shutdown", cancellationToken);
        }
        catch (Exception)
        {
            // The fallback below also covers an unresponsive local agent.
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
        if (await CanConnectAsync(cancellationToken))
        {
            return;
        }

        if (!_legacyAgentStopped)
        {
            _legacyAgentStopped = true;
            await StopLegacyAgentAsync(cancellationToken);
        }

        var agentPath = Path.Combine(AppContext.BaseDirectory, "FOG.Agent.exe");
        if (!File.Exists(agentPath))
        {
            throw new FileNotFoundException("FOG Prime Agent is not installed.", agentPath);
        }

        if (_spawnedAgent is null or { HasExited: true })
        {
            _spawnedAgent = Process.Start(new ProcessStartInfo
            {
                FileName = agentPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await CanConnectAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("FOG Prime Agent did not become available.");
    }

    private static async Task StopLegacyAgentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", AgentProtocol.LegacyPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(250, cancellationToken);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new AgentRequest("stop"), Json));
            await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            // No previous agent is running, or it is already shutting down.
        }
    }

    private static async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(150, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<AgentResponse> SendConnectedAsync(string command, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, cancellationToken);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var request = new AgentRequest(command, Guid.NewGuid().ToString("N"));
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        var line = await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        return line is null
            ? new AgentResponse(false, "degraded", "Agent did not return a response.", request.CorrelationId)
            : JsonSerializer.Deserialize<AgentResponse>(line, Json)
                ?? new AgentResponse(false, "degraded", "Agent returned invalid data.", request.CorrelationId);
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
