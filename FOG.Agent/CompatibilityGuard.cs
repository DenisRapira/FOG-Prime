using System.Diagnostics;

namespace FOG.Agent;

public sealed record CompatibilityBlock(bool IsActive, string? Signal, string? Reason)
{
    public static CompatibilityBlock None { get; } = new(false, null, null);
}

public interface ICompatibilitySignalSource
{
    IReadOnlyCollection<string> GetActiveSignals();
}

public sealed class SystemCompatibilitySignalSource : ICompatibilitySignalSource
{
    public IReadOnlyCollection<string> GetActiveSignals()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    names.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the snapshot was being collected.
                }
            }
        }

        return names;
    }
}

public sealed class CompatibilityGuard(ICompatibilitySignalSource signalSource)
{
    private static readonly HashSet<string> ProtectedGameSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat",
        "EasyAntiCheat_EOS",
        "EasyAntiCheat_EOS_Setup",
        "EasyAntiCheat_Setup",
        "EasyAntiCheatLauncher",
        "EACLauncher",
        "start_protected_game"
    };

    public CompatibilityBlock Check()
    {
        foreach (var signal in signalSource.GetActiveSignals())
        {
            if (ProtectedGameSignals.Contains(Normalize(signal)))
            {
                return new CompatibilityBlock(true, signal, "protectedGame");
            }
        }

        return CompatibilityBlock.None;
    }

    public static string Normalize(string processName)
    {
        var name = Path.GetFileName(processName.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }
}
