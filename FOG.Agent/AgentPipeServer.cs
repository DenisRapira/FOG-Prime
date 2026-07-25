using FOG.Protocol;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

public sealed class AgentPipeServer(
    EngineSupervisor supervisor,
    AgentStateStore stateStore,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AgentPipeServer> logger)
{
    private const int MaxConnections = 4;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly byte[] _sessionToken = ReadSessionToken();
    private readonly SemaphoreSlim _connectionSlots = new(MaxConnections, MaxConnections);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                await _connectionSlots.WaitAsync(cancellationToken);
                pipe = new NamedPipeServerStream(
                    AgentProtocol.PipeName,
                    PipeDirection.InOut,
                    MaxConnections,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = ServeConnectionAsync(pipe, cancellationToken);
                pipe = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (pipe is not null)
                {
                    pipe.Dispose();
                    _connectionSlots.Release();
                }
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _connectionSlots.Release();
                logger.LogError(ex, "Named pipe request failed");
            }
        }
    }

    private async Task ServeConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The Agent is shutting down.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Named pipe request failed");
            }
            finally
            {
                _connectionSlots.Release();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await ReadLimitedLineAsync(reader, 4096, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var request = line is null
            ? null
            : JsonSerializer.Deserialize<AgentRequest>(line, Json);

        var response = request is null || !IsAuthorized(request.SessionToken)
            ? new AgentResponse(false, "invalid", "Invalid request.", null)
            : await DispatchAsync(request, cancellationToken);

        if (response.Snapshot is not null)
        {
            await stateStore.SaveAsync(response.Snapshot, cancellationToken);
        }

        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));
        }
        catch (IOException)
        {
            logger.LogDebug("Named pipe client disconnected before receiving a response");
        }
        finally
        {
            try
            {
                await writer.DisposeAsync();
            }
            catch (IOException)
            {
                logger.LogDebug("Named pipe writer closed after the client disconnected");
            }
        }

        if (response.Ok && string.Equals(request?.Command, "shutdown", StringComparison.OrdinalIgnoreCase))
        {
            applicationLifetime.StopApplication();
        }
    }

    private static async Task<string?> ReadLimitedLineAsync(
        StreamReader reader,
        int maxLength,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder(Math.Min(maxLength, 512));
        var buffer = new char[256];
        while (result.Length <= maxLength)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return result.Length == 0 ? null : result.ToString();
            }

            var newline = Array.IndexOf(buffer, '\n', 0, read);
            var length = newline >= 0 ? newline : read;
            if (length > 0 && buffer[length - 1] == '\r')
            {
                length--;
            }

            if (result.Length + length > maxLength)
            {
                return null;
            }

            result.Append(buffer, 0, length);
            if (newline >= 0)
            {
                return result.ToString();
            }
        }

        return null;
    }

    private bool IsAuthorized(string? token)
    {
        if (token is null)
        {
            return false;
        }

        try
        {
            var candidate = Convert.FromHexString(token);
            return candidate.Length == _sessionToken.Length &&
                CryptographicOperations.FixedTimeEquals(candidate, _sessionToken);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ReadSessionToken()
    {
        try
        {
            var token = Environment.GetEnvironmentVariable("FOG_PRIME_SESSION_TOKEN");
            Environment.SetEnvironmentVariable("FOG_PRIME_SESSION_TOKEN", null);
            var bytes = token is null ? Array.Empty<byte>() : Convert.FromHexString(token);
            return bytes.Length == 32
                ? bytes
                : throw new InvalidOperationException("FOG Agent requires a valid session token.");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("FOG Agent requires a valid session token.", ex);
        }
    }

    private async Task<AgentResponse> DispatchAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Command.Trim().ToLowerInvariant() switch
            {
                "ping" => new AgentResponse(true, "ready", "pong", request.CorrelationId),
                "status" => Success("ready", "Status updated.", request, await supervisor.GetSnapshotAsync(cancellationToken)),
                "start" => Success("ready", "Automatic connection completed.", request, await supervisor.StartBestAsync(cancellationToken)),
                "stop" => Success("stopped", "Engine stopped.", request, await supervisor.StopAsync(cancellationToken)),
                "shutdown" => Success("stopped", "Agent stopped.", request, await supervisor.StopAsync(cancellationToken)),
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
