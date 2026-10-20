using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Comunicador.Services;
using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Storage;
using Comunicador.ViewModels;
using Comunicador.Views;

namespace Comunicador;

public partial class App : Application
{
    internal static readonly Stopwatch StartupWatch = Stopwatch.StartNew();
    internal static string? BenchmarkFile { get; private set; }
    internal static bool StartedInBackground { get; private set; }
    private MainViewModel? _mainViewModel;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationListener;
    private bool _ownsInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
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
                };
                MainWindow = smokeWindow;
                smokeWindow.Loaded += (_, _) =>
                {
                    try
                    {
                        smokeWindow.UpdateLayout();
                        smokeWindow.NavegarPara(new MensagensView());
                        smokeWindow.UpdateLayout();
                        var destino = new DestinoMonitor(
                            new Computador { Id = "smoke-monitor", Nome = "Prévia" },
                            new MonitorInfo { Index = 0, Name = "Monitor", Width = 1920, Height = 1080 });
                        var monitorView = new DefinirMonitorView(destino, null!);
                        smokeWindow.NavegarPara(monitorView);
                        smokeWindow.UpdateLayout();
                        if (monitorView.MonitorCanvas.ActualWidth <= 0
                            || monitorView.MonitorCanvas.ActualHeight <= 0)
                            throw new InvalidOperationException("A prévia do monitor não foi dimensionada.");
                        smokeWindow.NavegarVoltar();
                        var onlyOneWindow = Current.Windows.OfType<Window>().Count() == 1;
                        var openedInsideMainWindow = smokeWindow.TelaAninhadaAtiva;
                        smokeWindow.NavegarVoltar();
                        smokeWindow.UpdateLayout();
                        var returnedToMainScreen = !smokeWindow.TelaAninhadaAtiva;
                        if (!onlyOneWindow || !openedInsideMainWindow || !returnedToMainScreen)
                            throw new InvalidOperationException("A navegação não permaneceu em uma única janela principal.");
                        File.WriteAllText(marker, "Layout inicial, troca de tela e Voltar validados com uma única janela.");
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

        StartedInBackground = e.Args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase));

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

        SplashWindow? splash = null;
        try
        {
            if (!StartedInBackground)
            {
                splash = new SplashWindow();
                splash.ReportarProgresso("Carregando preferências locais...", 4);
                splash.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            var settings = SettingsStore.Load();
            splash?.ReportarProgresso("Preferências carregadas", 12);
            StartupManager.Aplicar(settings.IniciarComWindows);
            _mainViewModel = new MainViewModel(settings);
            splash?.ReportarProgresso("Dados locais carregados", 32);

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

            if (StartedInBackground)
            {
                window.PrepareForBackgroundStartup();
                window.Show();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(_mainViewModel.Start));
                return;
            }

            window.PrepareForSplashStartup();
            window.Show();
            await window.FirstContentRenderedTask;
            splash?.ReportarProgresso("Preparando os serviços do painel...", 48);

            _mainViewModel.Start();
            splash?.ReportarProgresso("Serviços locais iniciados", 60);

            await window.PrepararSecoesParaInicioAsync((loaded, total, section) =>
            {
                var progress = 60 + loaded * 35d / total;
                splash?.ReportarProgresso($"Preparando {section} ({loaded} de {total})...", progress);
            });

            await Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            splash?.ReportarProgresso("Tudo pronto", 100);
            await Task.Delay(180);

            var revealTask = window.RevelarAposInicializacaoAsync();
            if (splash is not null)
            {
                await splash.AnimateExitAsync();
                splash = null;
            }
            await revealTask;
            window.Activate();

            var resumo = _mainViewModel.ConsumeUpdateSummary();
            if (!string.IsNullOrWhiteSpace(resumo)) window.MostrarResumoAtualizacao(resumo);
        }
        catch (Exception ex)
        {
            splash?.Close();
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
