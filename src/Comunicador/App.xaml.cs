using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Comunicador.Services;
using Comunicador.ViewModels;
using Comunicador.Views;

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

        var smokeArgument = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--smoke-test=", StringComparison.OrdinalIgnoreCase));
        if (smokeArgument is not null)
        {
            try
            {
                var marker = smokeArgument["--smoke-test=".Length..];
                var smokeWindow = new Window
                {
                    Content = new ComputadoresView(),
                    Width = 1,
                    Height = 1,
                    Left = -10000,
                    Top = -10000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Opacity = 0,
                };
                smokeWindow.ContentRendered += (_, _) =>
                {
                    File.WriteAllText(marker, "Comunicador WPF iniciado.");
                    smokeWindow.Close();
                    Shutdown(0);
                };
                MainWindow = smokeWindow;
                smokeWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error($"Falha no teste de abertura: {ex}");
                Shutdown(1);
            }
            return;
        }

        var startupSmokeArgument = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--startup-smoke-test=", StringComparison.OrdinalIgnoreCase));
        if (startupSmokeArgument is not null)
        {
            try
            {
                var marker = startupSmokeArgument["--startup-smoke-test=".Length..];
                var smokeWindow = new MainWindow
                {
                    Width = 760,
                    Height = 600,
                    Left = -10000,
                    Top = -10000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Opacity = 0,
                };
                MainWindow = smokeWindow;
                smokeWindow.ContentRendered += (_, _) =>
                {
                    try
                    {
                        smokeWindow.NavegarPara(new System.Windows.Controls.Grid());
                        var onlyOneWindow = Current.Windows.OfType<Window>().Count() == 1;
                        var openedInsideMainWindow = smokeWindow.TelaAninhadaAtiva;
                        smokeWindow.NavegarVoltar();
                        var returnedToMainScreen = !smokeWindow.TelaAninhadaAtiva;
                        smokeWindow.UpdateLayout();
                        if (!onlyOneWindow || !openedInsideMainWindow || !returnedToMainScreen)
                            throw new InvalidOperationException("A navegação não permaneceu em uma única janela principal.");
                        File.WriteAllText(marker, "Primeiro quadro, troca de tela e Voltar validados com uma única janela.");
                        smokeWindow.Close();
                        Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Falha no teste da abertura completa: {ex}");
                        Shutdown(1);
                    }
                };
                smokeWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error($"Falha no teste da abertura completa: {ex}");
                Shutdown(1);
            }
            return;
        }

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

        try
        {
            _mainViewModel = new MainViewModel();

            var window = new MainWindow { DataContext = _mainViewModel };
#if TEST_BUILD
            window.Title = "Comunicador — Teste";
#endif
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
            // Deixa o WPF desenhar a primeira tela antes de iniciar descoberta,
            // sincronização na nuvem e monitoramento em segundo plano.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(_mainViewModel.Start));
            var resumo = _mainViewModel.ConsumeUpdateSummary();
            if (!string.IsNullOrWhiteSpace(resumo)) window.MostrarResumoAtualizacao(resumo);
        }
        catch (Exception ex)
        {
            Logger.Error($"Falha ao abrir o Comunicador: {ex}");
            MessageBox.Show($"Não foi possível abrir o Comunicador:\n\n{ex.Message}\n\nDetalhes em: {Storage.AppPaths.LogFile}",
                "Comunicador", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private bool ClaimSingleInstance()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
#if TEST_BUILD
        var prefix = @"Local\Comunicador.Teste." + user;
#else
        var prefix = @"Local\Comunicador." + user;
#endif
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
