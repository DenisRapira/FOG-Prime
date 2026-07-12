using FOG.Protocol;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

public sealed class AgentPipeServer(EngineSupervisor supervisor, ILogger<AgentPipeServer> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    AgentProtocol.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Named pipe request failed");
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        var request = line is null ? null : JsonSerializer.Deserialize<AgentRequest>(line, Json);

        var response = request is null
            ? new AgentResponse(false, "invalid", "Invalid request.", null)
            : await DispatchAsync(request, cancellationToken);

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));
    }

    private async Task<AgentResponse> DispatchAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Command.Trim().ToLowerInvariant() switch
            {
                "status" => Success("ready", "Status updated.", request, await supervisor.RefreshHealthAsync(cancellationToken)),
                "start" => Success("ready", "Automatic connection completed.", request, await supervisor.StartBestAsync(cancellationToken)),
                "stop" => Success("stopped", "Engine stopped.", request, await supervisor.StopAsync(cancellationToken)),
                "recheck" => Success("ready", "Connection checked.", request, await supervisor.RefreshHealthAsync(cancellationToken)),
                _ => new AgentResponse(false, "invalid", "Unsupported command.", request.CorrelationId)
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent command {Command} failed", request.Command);
            return new AgentResponse(false, "degraded", "The local runtime is not ready.", request.CorrelationId);
        }
    }

    private static AgentResponse Success(string state, string message, AgentRequest request, AgentSnapshot snapshot)
    {
        return new AgentResponse(true, snapshot.State == "ready" ? state : snapshot.State, message, request.CorrelationId, snapshot);
    }
}
