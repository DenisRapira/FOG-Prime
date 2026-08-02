using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FOG.Agent;

public sealed class EngineSessionLeaseStore
{
    private const int LeaseVersion = 1;
    private readonly string _leasePath;
    private readonly byte[]? _sessionKey;
    private readonly ILogger<EngineSessionLeaseStore> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public EngineSessionLeaseStore(
        IOptions<AgentOptions> options,
        ILogger<EngineSessionLeaseStore> logger)
        : this(
            Path.Combine(options.Value.ResolveDataDirectory(), "engine-session.json"),
            ReadSessionKey(),
            logger)
    {
    }

    internal EngineSessionLeaseStore(
        string leasePath,
        byte[]? sessionKey,
        ILogger<EngineSessionLeaseStore> logger)
    {
        _logger = logger;
        _leasePath = Path.GetFullPath(leasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_leasePath)!);
        _sessionKey = sessionKey?.ToArray();
    }

    public EngineSessionLease? TryLoad()
    {
        if (_sessionKey is null || !File.Exists(_leasePath))
        {
            return null;
        }

        try
        {
            var lease = JsonSerializer.Deserialize<EngineSessionLease>(File.ReadAllText(_leasePath), _json);
            if (lease is null ||
                lease.Version != LeaseVersion ||
                lease.ProcessId <= 0 ||
                lease.ProcessStartUtcTicks <= 0 ||
                string.IsNullOrWhiteSpace(lease.ProfileId) ||
                string.IsNullOrWhiteSpace(lease.ExecutablePath) ||
                string.IsNullOrWhiteSpace(lease.Signature) ||
                !VerifySignature(lease))
            {
                Clear();
                return null;
            }

            return lease;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Engine session lease could not be read");
            Clear();
            return null;
        }
    }

    public void Save(Process process, string profileId, string executablePath)
    {
        if (_sessionKey is null)
        {
            return;
        }

        try
        {
            var unsigned = new EngineSessionLease(
                LeaseVersion,
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                profileId,
                Path.GetFullPath(executablePath),
                string.Empty);
            var lease = unsigned with { Signature = ComputeSignature(unsigned) };
            var temporaryPath = _leasePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(lease, _json));
            File.Move(temporaryPath, _leasePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Engine session lease could not be saved");
        }
    }

    public void Clear(int? processId = null)
    {
        try
        {
            if (processId is not null && File.Exists(_leasePath))
            {
                var lease = JsonSerializer.Deserialize<EngineSessionLease>(File.ReadAllText(_leasePath), _json);
                if (lease is not null && lease.ProcessId != processId)
                {
                    return;
                }
            }

            File.Delete(_leasePath);
            File.Delete(_leasePath + ".tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex, "Engine session lease cleanup was deferred");
        }
    }

    private bool VerifySignature(EngineSessionLease lease)
    {
        byte[] actual;
        try
        {
            actual = Convert.FromHexString(lease.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromHexString(ComputeSignature(lease with { Signature = string.Empty }));
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private string ComputeSignature(EngineSessionLease lease)
    {
        var payload = string.Join(
            '\n',
            lease.Version,
            lease.ProcessId,
            lease.ProcessStartUtcTicks,
            lease.ProfileId,
            Path.GetFullPath(lease.ExecutablePath));
        return Convert.ToHexString(HMACSHA256.HashData(_sessionKey!, Encoding.UTF8.GetBytes(payload)));
    }

    private static byte[]? ReadSessionKey()
    {
        var token = Environment.GetEnvironmentVariable("FOG_PRIME_SESSION_TOKEN");
        if (token is null || token.Length != 64)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public sealed record EngineSessionLease(
    int Version,
    int ProcessId,
    long ProcessStartUtcTicks,
    string ProfileId,
    string ExecutablePath,
    string Signature);
