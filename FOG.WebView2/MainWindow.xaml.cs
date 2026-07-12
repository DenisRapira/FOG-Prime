using FOG.Protocol;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace FOG.WebView2;

public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AgentClient _agentClient = new();
    private bool _primeStarted;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FOG Prime",
            "WebView2Profile");

        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await Browser.EnsureCoreWebView2Async(environment);
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("FOG.WebView2.wwwroot.index.html")
            ?? throw new InvalidOperationException("Embedded interface was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Browser.NavigateToString(await reader.ReadToEndAsync());
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
                    break;
                case "runPrime":
                    await RunPrimeAsync(autoStart: true);
                    break;
                case "close":
                    Close();
                    break;
            }
        }
        catch
        {
            await PostAsync("prime", new PrimePayload("needsAttention", "Нужна помощь", "FOG Prime не смог завершить проверку.", 100, null, Array.Empty<CheckItem>()));
        }
    }

    private async Task RunPrimeAsync(bool autoStart)
    {
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
            response = await _agentClient.SendAsync(autoStart ? "start" : "recheck", CancellationToken.None);
        }
        catch
        {
            await PostAsync("prime", new PrimePayload("needsAttention", "Нужна помощь", "Локальный сервис FOG Prime не готов.", 100, diagnostics, Array.Empty<CheckItem>()));
            return;
        }

        var snapshot = response.Snapshot;
        var checks = snapshot?.Probes.Select(probe => new CheckItem(probe.Name, probe.Ok ? "ok" : "fail", probe.Detail)).ToArray()
            ?? Array.Empty<CheckItem>();
        var ready = response.Ok && snapshot is { EngineRunning: true, ConnectionHealthy: true };
        await PostAsync("prime", new PrimePayload(
            ready ? "ready" : "needsAttention",
            ready ? "Все готово" : "Нужна помощь",
            ready ? "Discord доступен. Можно закрыть это окно." : "Не удалось подтвердить соединение. Попробуйте еще раз.",
            100,
            diagnostics,
            checks));
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
