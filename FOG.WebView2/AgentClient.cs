using FOG.Protocol;
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

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (await CanConnectAsync(cancellationToken))
        {
            return;
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
}
