using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Comunicador.ViewModels;
using Comunicador.Views;

namespace Comunicador;

public partial class MainWindow : Window
{
    private readonly Dictionary<object, FrameworkElement> _sectionViews =
        new(ReferenceEqualityComparer.Instance);
    private readonly Grid _sectionHost = new();
    private MainViewModel? _viewModel;
    private int _warmupGeneration;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ContentRendered += OnContentRendered;
        Closed += (_, _) => DesconectarViewModel();
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        if (string.IsNullOrWhiteSpace(App.BenchmarkFile) || _viewModel is null) return;

        await Task.Delay(900);
        var readyMs = App.StartupWatch.Elapsed.TotalMilliseconds;
        var samples = new List<double>();
        var sections = new[] { "mensagens", "lembretes", "historico", "logs", "configuracoes", "computadores" };
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var section in sections)
            {
                var watch = Stopwatch.StartNew();
                _viewModel.NavegarCommand.Execute(section);
                await Dispatcher.InvokeAsync(() => SectionContent.UpdateLayout(), DispatcherPriority.Render);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        samples.Sort();
        var process = Process.GetCurrentProcess();
        var result = new
        {
            window_ready_ms = Math.Round(readyMs, 2),
            tab_average_ms = Math.Round(samples.Average(), 2),
            tab_p95_ms = Math.Round(samples[(int)Math.Ceiling(samples.Count * .95) - 1], 2),
            tab_max_ms = Math.Round(samples[^1], 2),
            working_set_mb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
            cpu_ms = Math.Round(process.TotalProcessorTime.TotalMilliseconds, 2),
            samples = samples.Select(value => Math.Round(value, 2)).ToArray(),
        };
        var benchmarkPath = Path.GetFullPath(App.BenchmarkFile);
        Directory.CreateDirectory(Path.GetDirectoryName(benchmarkPath)!);
        await File.WriteAllTextAsync(benchmarkPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimarEntrada(TopNavigation, -12, 30);
        MostrarSecaoAtual();
        AnimarSecao();
        AtualizarMoldura();
        _ = PreaquecerSecoesAsync(++_warmupGeneration);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DesconectarViewModel();
        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        PrepararSecoes(_viewModel);
        MostrarSecaoAtual();
    }

    private void PrepararSecoes(MainViewModel vm)
    {
        // Mantemos as views no mesmo host. Assim elas carregam apenas uma vez e
        // trocar de aba não reinicia templates, efeitos e animações dos cards.
        _sectionViews.Clear();
        _sectionHost.Children.Clear();
        SectionContent.Content = _sectionHost;
        AdicionarSecao(vm.SecaoAtual, Visibility.Visible);
    }

    private void DesconectarViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _warmupGeneration++;
        _sectionHost.Children.Clear();
        _sectionViews.Clear();
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SecaoAtual)) return;
        Dispatcher.BeginInvoke(() =>
        {
            AnimatedBackdrop.PauseAnimation = true;
            MostrarSecaoAtual();
            AnimarSecao();
            _ = RetomarFundoAsync();
        }, DispatcherPriority.Render);
    }

    private void MostrarSecaoAtual()
    {
        if (_viewModel is null) return;
        if (!_sectionViews.TryGetValue(_viewModel.SecaoAtual, out var view))
            view = AdicionarSecao(_viewModel.SecaoAtual, Visibility.Visible);
        foreach (var item in _sectionViews.Values)
        {
            item.Visibility = ReferenceEquals(item, view) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private FrameworkElement AdicionarSecao(object viewModel, Visibility visibility)
    {
        if (_sectionViews.TryGetValue(viewModel, out var existing)) return existing;
        FrameworkElement view = viewModel switch
        {
            ComputadoresViewModel => new ComputadoresView(),
            MensagensViewModel => new MensagensView(),
            LembretesViewModel => new LembretesView(),
            HistoricoViewModel => new HistoricoView(),
            LogsViewModel => new LogsView(),
            ConfiguracoesViewModel => new ConfiguracoesView(),
            _ => throw new InvalidOperationException("Seção desconhecida."),
        };
        view.DataContext = viewModel;
        view.Visibility = visibility;
        _sectionViews[viewModel] = view;
        _sectionHost.Children.Add(view);
        return view;
    }

    private async Task PreaquecerSecoesAsync(int generation)
    {
        await Task.Delay(180);
        if (_viewModel is null || generation != _warmupGeneration) return;
        var sections = new object[] { _viewModel.Computadores, _viewModel.Mensagens, _viewModel.Lembretes,
            _viewModel.Historico, _viewModel.Logs, _viewModel.Configuracoes };
        foreach (var section in sections)
        {
            if (generation != _warmupGeneration) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_sectionViews.ContainsKey(section)) return;
                var view = AdicionarSecao(section, Visibility.Hidden);
                view.UpdateLayout();
                view.Visibility = Visibility.Collapsed;
            }, DispatcherPriority.Background);
            await Task.Delay(35);
        }
    }

    private async Task RetomarFundoAsync()
    {
        await Task.Delay(230);
        if (AnimatedBackdrop is not null) AnimatedBackdrop.PauseAnimation = false;
    }

    private void AnimarSecao()
    {
        var reduzido = _viewModel?.Configuracoes.ReduzirMovimento == true;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        SectionContent.BeginAnimation(OpacityProperty, new DoubleAnimation(reduzido ? .72 : .25, 1,
            TimeSpan.FromMilliseconds(reduzido ? 260 : 175))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        });
        SectionTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(reduzido ? 2 : 8, 0, TimeSpan.FromMilliseconds(reduzido ? 300 : 210))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        SectionScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(reduzido ? .998 : .992, 1, TimeSpan.FromMilliseconds(reduzido ? 280 : 195))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        SectionScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(reduzido ? .998 : .992, 1, TimeSpan.FromMilliseconds(reduzido ? 280 : 195))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
    }

    private static void AnimarEntrada(FrameworkElement element, double fromY, int delayMs)
    {
        var transform = element.RenderTransform as TranslateTransform;
        if (transform is null || transform.IsFrozen)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        var delay = TimeSpan.FromMilliseconds(delayMs);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(340))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e) => AtualizarMoldura();

    private void AtualizarMoldura()
    {
        if (WindowFrame is null || MaximizeButton is null || MaximizeIcon is null) return;
        var maximizada = WindowState == WindowState.Maximized;
        WindowFrame.CornerRadius = maximizada ? new CornerRadius(0) : new CornerRadius(13);
        MaximizeIcon.Data = Geometry.Parse(maximizada
            ? "M 4,1.5 L 12.5,1.5 L 12.5,10 L 10,10 M 1.5,4 L 10,4 L 10,12.5 L 1.5,12.5 Z"
            : "M 2,2 L 12,2 L 12,12 L 2,12 Z");
    }
}
