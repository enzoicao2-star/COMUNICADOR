using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Comunicador.Controls;
using Comunicador.ViewModels;
using Comunicador.Views;
using Forms = System.Windows.Forms;

namespace Comunicador;

public partial class MainWindow : Window
{
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Dictionary<object, FrameworkElement> _sectionViews =
        new(ReferenceEqualityComparer.Instance);
    private readonly Grid _sectionHost = new();
    private MainViewModel? _viewModel;
    private int _warmupGeneration;
    private int _navigationPauseGeneration;
    private bool _navigationTransitionActive;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Comunicador — recebendo notificações",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true,
        };
        DataContextChanged += OnDataContextChanged;
        ContentRendered += OnContentRendered;
        Closing += OnWindowClosing;
        IsVisibleChanged += OnWindowVisibilityChanged;
        Closed += (_, _) =>
        {
            _trayIcon.Dispose();
            DesconectarViewModel();
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Comunicador", null, (_, _) => RestaurarDaBandeja());
        menu.Items.Add("Encerrar", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestaurarDaBandeja();
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
        var cpuBeforeHide = process.TotalProcessorTime;
        Hide();
        process.Refresh();
        var hideTransitionCpuMs = (process.TotalProcessorTime - cpuBeforeHide).TotalMilliseconds;
        var hiddenWindowVisible = IsVisible;
        var hiddenBackdropVisible = AnimatedBackdrop.IsVisible;
        var hiddenBackdropPaused = AnimatedBackdrop.PauseAnimation;
        var hiddenVisuals = EnumerateVisuals(this).ToArray();
        var hiddenDiagnostics = new
        {
            ui_thread_id = GetCurrentThreadId(),
            animated_background_subscribers = hiddenVisuals.OfType<AnimatedBackground>().Count(control => control.IsRenderingSubscribed),
            nav_background_subscribers = hiddenVisuals.OfType<AnimatedNavBackground>().Count(control => control.IsRenderingSubscribed),
            badge_subscribers = hiddenVisuals.OfType<InteractiveBadge>().Count(control => control.IsRenderingSubscribed),
            animated_image_timers = hiddenVisuals.OfType<AnimatedImage>().Count(control => control.IsPlaybackTimerEnabled),
        };
        await Task.Delay(1000);
        process.Refresh();
        var cpuBeforeHiddenIdle = process.TotalProcessorTime;
        await Task.Delay(1000);
        process.Refresh();
        var hiddenCpuMs = (process.TotalProcessorTime - cpuBeforeHiddenIdle).TotalMilliseconds;
        Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var result = new
        {
            window_ready_ms = Math.Round(readyMs, 2),
            tab_average_ms = Math.Round(samples.Average(), 2),
            tab_p95_ms = Math.Round(samples[(int)Math.Ceiling(samples.Count * .95) - 1], 2),
            tab_max_ms = Math.Round(samples[^1], 2),
            working_set_mb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
            cpu_ms = Math.Round(process.TotalProcessorTime.TotalMilliseconds, 2),
            hide_transition_cpu_ms = Math.Round(hideTransitionCpuMs, 2),
            hidden_cpu_ms = Math.Round(hiddenCpuMs, 2),
            hidden_window_visible = hiddenWindowVisible,
            hidden_backdrop_visible = hiddenBackdropVisible,
            hidden_backdrop_paused = hiddenBackdropPaused,
            hidden_diagnostics = hiddenDiagnostics,
            samples = samples.Select(value => Math.Round(value, 2)).ToArray(),
        };
        var benchmarkPath = Path.GetFullPath(App.BenchmarkFile);
        Directory.CreateDirectory(Path.GetDirectoryName(benchmarkPath)!);
        await File.WriteAllTextAsync(benchmarkPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        _allowExit = true;
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

    private static IEnumerable<DependencyObject> EnumerateVisuals(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in EnumerateVisuals(child)) yield return descendant;
        }
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
            _navigationTransitionActive = true;
            AtualizarAnimacaoFundo();
            MostrarSecaoAtual();
            AnimarSecao();
            _ = RetomarFundoAsync(++_navigationPauseGeneration);
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

    private async Task RetomarFundoAsync(int generation)
    {
        await Task.Delay(230);
        if (generation != _navigationPauseGeneration) return;
        _navigationTransitionActive = false;
        AtualizarAnimacaoFundo();
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

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit || !string.IsNullOrWhiteSpace(App.BenchmarkFile)) return;
        e.Cancel = true;
        Hide();
        _trayIcon.ShowBalloonTip(2500, "Comunicador",
            "O painel continua recebendo mensagens e lembretes em segundo plano.",
            Forms.ToolTipIcon.Info);
    }

    private void RestaurarDaBandeja()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        AtualizarMoldura();
        AtualizarAnimacaoFundo();
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => AtualizarAnimacaoFundo();

    private void AtualizarAnimacaoFundo()
    {
        if (AnimatedBackdrop is not null)
            AnimatedBackdrop.PauseAnimation = _navigationTransitionActive
                || !IsVisible
                || WindowState == WindowState.Minimized;
    }

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
