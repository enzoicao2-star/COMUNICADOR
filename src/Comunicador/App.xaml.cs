using System.Diagnostics;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        Logger.Info("Comunicador iniciando.");

        BenchmarkFile = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--benchmark-file=", StringComparison.OrdinalIgnoreCase))?[17..];

        _mainViewModel = new MainViewModel();
        _mainViewModel.Start();

        var window = new MainWindow { DataContext = _mainViewModel };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
