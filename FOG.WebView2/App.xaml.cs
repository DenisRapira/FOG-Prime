using System.Configuration;
using System.Data;
using System.Windows;

namespace FOG.WebView2;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(false, @"Local\FOG.Prime.Desktop");
        try
        {
            _ownsInstance = _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsInstance = true;
        }
        if (!_ownsInstance)
        {
            MessageBox.Show("FOG уже запущен. Используйте открытое окно приложения.", "FOG", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DiagnosticLog.BeginSession();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Write("UI", "Unhandled dispatcher exception", e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Write("UI", "Unhandled application exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticLog.Write("UI", "Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
