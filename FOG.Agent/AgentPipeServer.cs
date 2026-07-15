using FOG.Protocol;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

public sealed class AgentPipeServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly EngineSupervisor _supervisor;
    private readonly AgentStateStore _stateStore;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<AgentPipeServer> _logger;
    private readonly byte[] _sessionToken;

    public AgentPipeServer(
        EngineSupervisor supervisor,
        AgentStateStore stateStore,
        IHostApplicationLifetime applicationLifetime,
        ILogger<AgentPipeServer> logger)
    {
        _supervisor = supervisor;
        _stateStore = stateStore;
        _applicationLifetime = applicationLifetime;
        _logger = logger;

        var token = Environment.GetEnvironmentVariable("FOG_PRIME_SESSION_TOKEN");
        try
        {
            _sessionToken = token is null ? Array.Empty<byte>() : Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            _sessionToken = Array.Empty<byte>();
        }

        if (_sessionToken.Length != 32)
        {
            throw new InvalidOperationException("FOG Agent requires a valid session token.");
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    AgentProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named pipe request failed");
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await ReadLimitedLineAsync(reader, 4096, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var request = line is null
            ? null
            : JsonSerializer.Deserialize<AgentRequest>(line, Json);

        var response = request is null || !IsAuthorized(request.SessionToken)
            ? new AgentResponse(false, "invalid", "Invalid request.", null)
            : await DispatchAsync(request, cancellationToken);

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));

        if (response.Ok && string.Equals(request?.Command, "shutdown", StringComparison.OrdinalIgnoreCase))
        {
            _applicationLifetime.StopApplication();
        }
    }

    private static async Task<string?> ReadLimitedLineAsync(StreamReader reader, int maxLength, CancellationToken cancellationToken)
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

    private async Task<AgentResponse> DispatchAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = request.Command.Trim().ToLowerInvariant() switch
            {
                "ping" => new AgentResponse(true, "ready", "pong", request.CorrelationId),
                "status" => Success("ready", "Status updated.", request, await _supervisor.RefreshHealthAsync(cancellationToken)),
                "start" => Success("ready", "Automatic connection completed.", request, await _supervisor.StartBestAsync(cancellationToken)),
                "stop" => Success("stopped", "Engine stopped.", request, await _supervisor.StopAsync(cancellationToken)),
                "shutdown" => Success("stopped", "Agent stopped.", request, await _supervisor.StopAsync(cancellationToken)),
                "recheck" => Success("ready", "Connection checked.", request, await _supervisor.RefreshHealthAsync(cancellationToken)),
                _ => new AgentResponse(false, "invalid", "Unsupported command.", request.CorrelationId)
            };

            if (response.Snapshot is not null)
            {
                await _stateStore.SaveAsync(response.Snapshot, cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent command {Command} failed", request.Command);
            return new AgentResponse(false, "degraded", "The local runtime is not ready.", request.CorrelationId);
        }
    }

    private static AgentResponse Success(string state, string message, AgentRequest request, AgentSnapshot snapshot)
    {
        return new AgentResponse(true, snapshot.State == "ready" ? state : snapshot.State, message, request.CorrelationId, snapshot);
    }
}
