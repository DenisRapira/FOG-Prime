using Microsoft.Extensions.Options;

namespace FOG.Agent;

public sealed class RuntimeIntegrityVerifier(IOptions<AgentOptions> options) : IDisposable
{
    private const string TrustedManifestResource = "FOG.Agent.TrustedRuntimeManifest.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VerifiedRuntimeLease? _lease;
    private bool _disposed;

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lease is not null) return;
            var trustedBytes = await ReadTrustedManifestAsync(cancellationToken);
            var manifestPath = options.Value.ResolveFromBase(options.Value.ManifestPath);
            var runtimeRoot = options.Value.ResolveFromBase(options.Value.RuntimeDirectory);
            _lease = await VerifiedRuntimeLease.AcquireAsync(runtimeRoot, manifestPath, trustedBytes, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try { _disposed = true; _lease?.Dispose(); _lease = null; }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]> ReadTrustedManifestAsync(CancellationToken cancellationToken)
    {
        await using var resource = typeof(RuntimeIntegrityVerifier).Assembly.GetManifestResourceStream(TrustedManifestResource)
            ?? throw new InvalidDataException("Trusted runtime manifest is missing from this build.");
        using var memory = new MemoryStream();
        await resource.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}
