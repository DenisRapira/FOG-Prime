using FOG.Protocol;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;

namespace FOG.WebView2;

public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AgentClient _agentClient = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly SemaphoreSlim _primeGate = new(1, 1);
    private readonly CancellationTokenSource _monitorCancellation = new();
    private bool _primeStarted;
    private bool _monitorStarted;
    private bool _shutdownComplete;
    private bool _shutdownInProgress;
    private bool _autoStartRequested;
    private Uri? _pendingUpdatePage;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        _autoStartRequested = false;
        _monitorCancellation.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            await _agentClient.ShutdownAsync(timeout.Token);
        }
        finally
        {
            if (Browser.CoreWebView2 is not null)
            {
                Browser.CoreWebView2.NavigationCompleted -= OnInitialNavigationCompleted;
                Browser.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                Browser.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                Browser.CoreWebView2.PermissionRequested -= OnPermissionRequested;
                Browser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }

            Browser.Dispose();
            _updateChecker.Dispose();
            _monitorCancellation.Dispose();
            _shutdownComplete = true;
            Close();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FOG Prime",
                "WebView2Profile");

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.NavigationCompleted += OnInitialNavigationCompleted;
            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            Browser.CoreWebView2.PermissionRequested += OnPermissionRequested;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("FOG.WebView2.wwwroot.index.html")
                ?? throw new InvalidOperationException("Embedded interface was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Browser.NavigateToString(await reader.ReadToEndAsync());
            DiagnosticLog.Write("UI", "WebView2 interface initialized");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("UI", "WebView2 initialization failed", ex);
            MessageBox.Show(
                "Не удалось запустить интерфейс FOG Prime. Установите Microsoft Edge WebView2 Runtime и повторите запуск.",
                "FOG Prime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var command = document.RootElement.GetProperty("command").GetString();

            switch (command)
            {
                case "ready" when !_primeStarted:
                    _primeStarted = true;
                    await RunPrimeAsync(autoStart: !IsAutoStartDisabled());
                    _ = CheckForUpdatesAsync(_monitorCancellation.Token);
                    break;
                case "runPrime":
                    await RunPrimeAsync(autoStart: true);
                    break;
                case "close":
                    Close();
                    break;
                case "openUpdate" when _pendingUpdatePage is not null:
                    OpenUpdatePage(_pendingUpdatePage);
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("UI", "Interface command failed", ex);
            await PostAsync("prime", new PrimePayload("needsAttention", "Нужна помощь", "FOG Prime не смог завершить проверку.", 100, null, Array.Empty<CheckItem>()));
        }
    }

    private static void OnInitialNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 webView)
        {
            return;
        }

        webView.NavigationCompleted -= OnInitialNavigationCompleted;
        webView.NavigationStarting += OnNavigationStarting;
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
    }

    private async Task RunPrimeAsync(bool autoStart)
    {
        if (!await _primeGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            _autoStartRequested |= autoStart;
            await PostAsync("prime", new PrimePayload("checking", "Проверяем систему", "Это займет несколько секунд.", 16, null, Array.Empty<CheckItem>()));
            var diagnostics = BuildDiagnostics();

            if (diagnostics.Summary.Fail > 0)
            {
                await PostAsync("prime", new PrimePayload("needsAttention", "Нужна помощь", "Не удалось подготовить компоненты FOG Prime.", 100, diagnostics, Array.Empty<CheckItem>()));
                return;
            }

            await PostAsync("prime", new PrimePayload("starting", "Настраиваем соединение", "Подбираем рабочий режим автоматически.", 42, diagnostics, Array.Empty<CheckItem>()));

            AgentResponse response;
            try
            {
                response = await _agentClient.SendAsync(autoStart ? "start" : "recheck", _monitorCancellation.Token);
            }
            catch (Exception ex) when (!_monitorCancellation.IsCancellationRequested)
            {
                DiagnosticLog.Write("UI", "Initial Agent command failed", ex);
                await PostAsync("prime", new PrimePayload("recovering", "Восстанавливаем связь", "FOG запустит резервный вариант автоматически.", 68, diagnostics, Array.Empty<CheckItem>()));
                StartMonitor();
                return;
            }

            await RenderSnapshotAsync(response.Snapshot, diagnostics);
            StartMonitor();
        }
        finally
        {
            _primeGate.Release();
        }
    }

    private void StartMonitor()
    {
        if (_monitorStarted)
        {
            return;
        }

        _monitorStarted = true;
        _ = MonitorAsync(_monitorCancellation.Token);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var response = await _agentClient.SendAsync("status", cancellationToken);
                if (_autoStartRequested && response.Snapshot?.State == "stopped")
                {
                    DiagnosticLog.Write("UI", "Agent is stopped while automatic mode is requested; sending start again");
                    response = await _agentClient.SendAsync("start", cancellationToken);
                }
                await RenderSnapshotAsync(response.Snapshot, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("UI", "Agent status request failed", ex);
                await PostAsync("prime", new PrimePayload("recovering", "Восстанавливаем связь", "FOG уже пробует резервный вариант.", 68, null, Array.Empty<CheckItem>()));
            }
        }
    }

    private async Task RenderSnapshotAsync(AgentSnapshot? snapshot, DiagnosticsPayload? diagnostics)
    {
        var checks = snapshot?.Probes.Select(probe => new CheckItem(probe.Name, probe.Ok ? "ok" : "fail", probe.Detail)).ToArray()
            ?? Array.Empty<CheckItem>();
        var ready = snapshot is { ConnectionHealthy: true };
        var engineActive = snapshot is { EngineRunning: true };
        var tunnelActive = !string.IsNullOrWhiteSpace(snapshot?.ActiveTunnel);

        await PostAsync("vpn", new VpnPayload(tunnelActive, ready));

        if (ready)
        {
            await PostAsync("prime", new PrimePayload(
                tunnelActive ? "vpnReady" : "ready",
                tunnelActive ? "VPN и связь готовы" : "Все готово",
                tunnelActive
                    ? "FOG не меняет VPN и проверяет доступ автоматически."
                    : "FOG работает. Можно пользоваться связью.",
                100,
                diagnostics,
                checks));
            return;
        }

        await PostAsync("prime", new PrimePayload(
            engineActive ? "checking" : "recovering",
            tunnelActive ? "Проверяем соединение" : "Подключаем",
            tunnelActive
                ? "Проверяем доступ через активное подключение."
                : engineActive
                ? "Проверяем доступ. Обычно это занимает несколько секунд."
                : snapshot?.State == "recovering"
                ? "Подбираем рабочее подключение."
                : "Почти готово.",
            72,
            diagnostics,
            checks));
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.8";
            var update = await _updateChecker.CheckAsync(currentVersion, cancellationToken);
            if (update is null)
            {
                return;
            }

            _pendingUpdatePage = update.PageUri;
            await PostAsync("update", new UpdatePayload(update.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The window is closing.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // Update availability must never block the connection workflow.
            DiagnosticLog.Write("UI", "Update check failed", ex);
        }
    }

    private static void OpenUpdatePage(Uri page)
    {
        if (!UpdateChecker.IsTrustedReleaseUri(page))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The default browser is unavailable; FOG itself remains operational.
        }
    }

    private static bool IsAutoStartDisabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("FOG_PRIME_NO_AUTOSTART"), "1", StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticsPayload BuildDiagnostics()
    {
        var checks = new List<CheckItem>();
        var baseDirectory = AppContext.BaseDirectory;
        AddCheck(checks, "Windows", OperatingSystem.IsWindows(), "Supported platform.", "FOG Prime runs on Windows only.");
        AddCheck(checks, "WebView2", HasWebView2Runtime(out var version), version, "WebView2 Runtime is missing.");
        AddCheck(checks, "Privileges", IsAdministrator(), "Administrator session.", "Administrator permission is required.");
        AddCheck(checks, "FOG Agent", File.Exists(Path.Combine(baseDirectory, "FOG.Agent.exe")), "Local Agent found.", "FOG.Agent.exe is missing.");
        AddCheck(checks, "Runtime manifest", File.Exists(Path.Combine(baseDirectory, "runtime.manifest.json")), "Integrity manifest found.", "Runtime manifest is missing.");
        AddCheck(checks, "FOG Engine", File.Exists(Path.Combine(baseDirectory, "runtime", "FOG.Engine.exe")), "Engine runtime found.", "FOG Engine runtime is missing.");

        var summary = new Summary(
            checks.Count(check => check.Status == "ok"),
            checks.Count(check => check.Status == "warn"),
            checks.Count(check => check.Status == "fail"),
            DateTimeOffset.Now.ToString("HH:mm:ss"));

        return new DiagnosticsPayload(baseDirectory, summary, checks);
    }

    private async Task PostAsync(string type, object payload)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var envelope = JsonSerializer.Serialize(new { type, payload }, _json);
        await Dispatcher.InvokeAsync(() => Browser.CoreWebView2.PostWebMessageAsJson(envelope));
    }

    private static void AddCheck(List<CheckItem> checks, string name, bool ok, string detail, string failDetail)
    {
        checks.Add(new CheckItem(name, ok ? "ok" : "fail", ok ? detail : failDetail));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool HasWebView2Runtime(out string version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception ex)
        {
            version = ex.Message;
            return false;
        }
    }
}

public sealed record PrimePayload(
    string Status,
    string Title,
    string Message,
    int Progress,
    DiagnosticsPayload? Diagnostics,
    IReadOnlyList<CheckItem> DiscordChecks);

public sealed record DiagnosticsPayload(string RootDir, Summary Summary, IReadOnlyList<CheckItem> Checks);

public sealed record Summary(int Ok, int Warn, int Fail, string CheckedAt);

public sealed record CheckItem(string Name, string Status, string Detail);

public sealed record VpnPayload(bool Active, bool ConnectionHealthy);

public sealed record UpdatePayload(string Version);
