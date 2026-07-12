using FOG.Protocol;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FOG.Agent;

public sealed class AgentStateStore
{
    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AgentStateStore(IOptions<AgentOptions> options)
    {
        var directory = options.Value.ResolveDataDirectory();
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "state.json");
    }

    public async Task SaveAsync(AgentSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _statePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, _json), cancellationToken);
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
