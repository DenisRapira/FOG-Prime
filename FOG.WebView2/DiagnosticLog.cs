using System.IO;
using System.Text;

namespace FOG.WebView2;

internal static class DiagnosticLog
{
    private const long MaximumBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FOG Prime",
        "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "fog-prime.log");
    private static readonly string PreviousLogPath = Path.Combine(LogDirectory, "fog-prime.previous.log");
    private static int _sessionStarted;

    public static void BeginSession()
    {
        if (Interlocked.Exchange(ref _sessionStarted, 1) != 0)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath))
                {
                    File.Move(LogPath, PreviousLogPath, overwrite: true);
                }

                File.WriteAllText(
                    LogPath,
                    $"{DateTimeOffset.UtcNow:O} [UI] Session started; version={GetVersion()}; os={Environment.OSVersion.VersionString}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never prevent the UI from starting.
        }
    }

    public static void Write(string component, string message, Exception? exception = null)
    {
        try
        {
            BeginSession();
            lock (Sync)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaximumBytes)
                {
                    File.WriteAllText(
                        LogPath,
                        $"{DateTimeOffset.UtcNow:O} [UI] Log size limit reached; earlier entries were removed.{Environment.NewLine}",
                        new UTF8Encoding(false));
                }

                var detail = exception is null ? message : $"{message}: {exception}";
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.UtcNow:O} [{Clean(component)}] {Clean(detail)}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never affect the connection workflow.
        }
    }

    private static string GetVersion() =>
        typeof(DiagnosticLog).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString().Trim();
    }
}
