using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace FOG.Agent;

public sealed class WinDivertServiceManager(
    IOptions<AgentOptions> options,
    ILogger<WinDivertServiceManager> logger)
{
    private const string ServiceName = "WinDivert";

    public async Task StopOwnedAsync(CancellationToken cancellationToken)
    {
        var expectedPath = Path.GetFullPath(Path.Combine(
            options.Value.ResolveFromBase(options.Value.RuntimeDirectory),
            "WinDivert64.sys"));
        var imagePath = ReadImagePath();
        if (!IsOwnedImagePath(imagePath, expectedPath))
        {
            return;
        }

        try
        {
            if (!ServiceExists(ServiceName))
            {
                logger.LogDebug("WinDivert service is not installed; nothing to stop.");
                return;
            }

            using var service = new ServiceController(ServiceName);
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status != ServiceControllerStatus.StopPending)
                {
                    service.Stop();
                }

                await Task.Run(
                    () => service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8)),
                    cancellationToken);
            }

            await DeleteServiceEntryAsync(cancellationToken);
            logger.LogInformation("Owned WinDivert driver service stopped and removed");
        }
        catch (Exception ex) when (ex is InvalidOperationException or
            Win32Exception or
            System.TimeoutException or
            System.ServiceProcess.TimeoutException)
        {
            if (ex is InvalidOperationException || ex is Win32Exception)
            {
                logger.LogDebug(ex, "WinDivert driver service stop was skipped because the service is unavailable.");
            }
            else
            {
                logger.LogWarning(ex, "Owned WinDivert driver service did not stop cleanly");
            }
        }
    }

    public static bool IsOwnedImagePath(string? imagePath, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        var candidate = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));
        if (candidate.StartsWith(@"\??\", StringComparison.Ordinal) ||
            candidate.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            candidate = candidate[4..];
        }

        try
        {
            return Path.GetFullPath(candidate).Equals(
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? ReadImagePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            return key?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            var services = ServiceController.GetServices();
            try
            {
                return services.Any(service => string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                foreach (var service in services) service.Dispose();
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task DeleteServiceEntryAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("delete");
        startInfo.ArgumentList.Add(ServiceName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows service manager could not be started.");
        await process.WaitForExitAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        if (process.ExitCode is not 0 and not 1072)
        {
            throw new Win32Exception(process.ExitCode, "WinDivert service entry could not be removed.");
        }
    }
}
