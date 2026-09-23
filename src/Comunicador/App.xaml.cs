using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Comunicador.Services;
using Comunicador.ViewModels;

namespace Comunicador;

public partial class App : Application
{
    internal static readonly Stopwatch StartupWatch = Stopwatch.StartNew();
    internal static string? BenchmarkFile { get; private set; }
    private MainViewModel? _mainViewModel;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationListener;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        Logger.Info("Comunicador iniciando.");

        BenchmarkFile = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--benchmark-file=", StringComparison.OrdinalIgnoreCase))?[17..];

        _mainViewModel = new MainViewModel();
        _mainViewModel.Start();

        var window = new MainWindow { DataContext = _mainViewModel };
        if (e.Args.Any(arg => arg.Equals("--monitor=2", StringComparison.OrdinalIgnoreCase)))
        {
            var secondary = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => !screen.Primary);
            if (secondary is not null)
            {
                var bounds = secondary.WorkingArea;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = bounds.Left + Math.Max(0, (bounds.Width - window.Width) / 2);
                window.Top = bounds.Top + Math.Max(0, (bounds.Height - window.Height) / 2);
            }
        }
        MainWindow = window;
        window.Show();
    }

    private bool ClaimSingleInstance()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var prefix = @"Local\Comunicador." + user;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".activate");
        _instanceMutex = new Mutex(false, prefix + ".instance");
        try { _ownsInstanceMutex = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
        if (!_ownsInstanceMutex)
        {
            _activationEvent.Set();
            return false;
        }

        _activationCancellation = new CancellationTokenSource();
        var cancellation = _activationCancellation.Token;
        var activationEvent = _activationEvent;
        _activationListener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { activationEvent, cancellation.WaitHandle };
            while (!cancellation.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) != 0) break;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (MainWindow is MainWindow window) window.RestaurarDaBandeja();
                }));
            }
        }, cancellation);
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        try { _activationListener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _activationCancellation?.Dispose();
        _activationEvent?.Dispose();
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _mainViewModel?.Dispose();
        Logger.Info("Comunicador encerrado.");
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error($"Exceção não tratada (UI): {e.Exception}");
        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{e.Exception.Message}\n\nDetalhes em: {Storage.AppPaths.LogFile}",
            "Comunicador", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Error($"Exceção não tratada (background): {e.ExceptionObject}");
    }
}
