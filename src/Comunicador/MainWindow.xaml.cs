using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private void OnBackgroundMouseMove(object sender, MouseEventArgs e)
        => AnimatedBackdrop.SetPointer(e.GetPosition(AnimatedBackdrop));

    private void OnBackgroundMouseLeave(object sender, MouseEventArgs e)
        => AnimatedBackdrop.ClearPointer();

    private sealed record NavigationFrame(object? Content, Action? OnExit, bool Locked, bool BackAllowed);
    internal bool TelaAninhadaAtiva => _navigationStack.Count > 0;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Dictionary<object, FrameworkElement> _sectionViews =
        new(ReferenceEqualityComparer.Instance);
    private readonly Grid _sectionHost = new();
    private readonly Stack<NavigationFrame> _navigationStack = new();
    private MainViewModel? _viewModel;
    private Action? _currentNavigationCleanup;
    private bool _navigationLocked;
    private bool _currentBackAllowed = true;
    private int _warmupGeneration;
    private int _navigationPauseGeneration;
    private bool _navigationTransitionActive;
    private bool _janelaFoiReativada;
    private bool _verificandoAtualizacao;
    private bool _dialogoAtualizacaoAberto;
    private bool _atualizacaoEmAndamento;
    private bool _lendoProgressoAtualizacao;
    private bool _spinnerAtualizacaoVisivel;
    private readonly DispatcherTimer _temporizadorAtualizacao = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string? _versaoRecusada;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        _temporizadorAtualizacao.Tick += AtualizarProgressoPainel;
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
        Deactivated += (_, _) => _janelaFoiReativada = true;
        Activated += OnMainWindowActivated;
        Closed += (_, _) =>
        {
            _trayIcon.Dispose();
            _temporizadorAtualizacao.Stop();
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
        if (_viewModel is null) return;
        _ = Services.PanelUpdateService.ReportHealthyStartupAsync();
        if (string.IsNullOrWhiteSpace(App.BenchmarkFile))
        {
            // Aguarda o ícone inicial sair antes de abrir qualquer diálogo.
            await Task.Delay(300);
            await VerificarAtualizacaoAoIniciarAsync(_viewModel);
            return;
        }

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

    private async Task VerificarAtualizacaoAoIniciarAsync(MainViewModel viewModel)
    {
        if (_verificandoAtualizacao || _dialogoAtualizacaoAberto || _atualizacaoEmAndamento
            || SectionContent.Content is AtualizacaoDisponivelWindow) return;
        _verificandoAtualizacao = true;
        try
        {
            var info = await viewModel.Configuracoes.VerificarAtualizacaoPainelAsync();
            if (info is { IsAvailable: true }) await PerguntarAtualizacaoAsync(viewModel, info);
        }
        finally { _verificandoAtualizacao = false; }
    }

    private async void OnMainWindowActivated(object? sender, EventArgs e)
    {
        if (!_janelaFoiReativada || _verificandoAtualizacao || _dialogoAtualizacaoAberto || _atualizacaoEmAndamento
            || _viewModel is null || SectionContent.Content is AtualizacaoDisponivelWindow)
            return;

        _janelaFoiReativada = false;
        _verificandoAtualizacao = true;
        try
        {
            var info = await _viewModel.Configuracoes.VerificarAtualizacaoPainelAsync();
            if (info is { IsAvailable: true } && _versaoRecusada != info.LatestVersion.ToString())
                await PerguntarAtualizacaoAsync(_viewModel, info);
        }
        finally { _verificandoAtualizacao = false; }
    }

    private Task PerguntarAtualizacaoAsync(MainViewModel viewModel, Services.PanelUpdateInfo info)
    {
        if (_dialogoAtualizacaoAberto) return Task.CompletedTask;
        _dialogoAtualizacaoAberto = true;
        try
        {
            var tela = new AtualizacaoDisponivelWindow(info);
            tela.AdiarSolicitado += (_, _) =>
            {
                _versaoRecusada = info.LatestVersion.ToString();
                tela.MostrarAdiada();
            };
            tela.AtualizarSolicitado += async (_, _) =>
            {
                tela.IsEnabled = false;
                if (!await viewModel.Configuracoes.AtualizarPainelAsync())
                {
                    tela.IsEnabled = true;
                    tela.MostrarErro("Não foi possível iniciar a atualização. Confira a conexão e tente novamente em Configurações.");
                    return;
                }

                NavegarVoltar();
            };
            NavegarPara(tela);
        }
        finally { _dialogoAtualizacaoAberto = false; }
        return Task.CompletedTask;
    }

    private void OnAtualizacaoPainelIniciada(object? sender, EventArgs e)
    {
        _atualizacaoEmAndamento = true;
        UpdateStatus.Visibility = Visibility.Visible;
        DefinirTextoAtualizacao("Atualizando · 0% · preparando download...");
        UpdateStatusText.ToolTip = null;
        MostrarSpinnerAtualizacao(true);
        _temporizadorAtualizacao.Start();
    }

    private void DefinirTextoAtualizacao(string texto)
    {
        if (UpdateStatusText.Text != texto) UpdateStatusText.Text = texto;
    }

    private void MostrarSpinnerAtualizacao(bool mostrar)
    {
        if (_spinnerAtualizacaoVisivel == mostrar) return;
        _spinnerAtualizacaoVisivel = mostrar;
        UpdateSpinner.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        UpdateSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        if (mostrar && _viewModel?.Configuracoes.ReduzirMovimento != true)
            UpdateSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850))
                { RepeatBehavior = RepeatBehavior.Forever });
    }

    private async void AtualizarProgressoPainel(object? sender, EventArgs e)
    {
        if (_lendoProgressoAtualizacao || _viewModel is null) return;
        var caminho = _viewModel.Configuracoes.CaminhoProgressoAtualizacaoPainel;
        if (!File.Exists(caminho)) return;
        _lendoProgressoAtualizacao = true;
        try
        {
            var progresso = JsonSerializer.Deserialize<Services.PanelUpdateProgress>(
                await File.ReadAllTextAsync(caminho),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (progresso is null) return;

            switch (progresso.Phase)
            {
                case "running":
                    MostrarSpinnerAtualizacao(true);
                    DefinirTextoAtualizacao($"Atualizando · {Math.Clamp(progresso.Percent, 0, 99)}% · {progresso.Message}");
                    break;
                case "download_complete":
                    MostrarSpinnerAtualizacao(false);
                    DefinirTextoAtualizacao("Download da nova atualização concluído");
                    break;
                case "restart_wait":
                    MostrarSpinnerAtualizacao(false);
                    DefinirTextoAtualizacao($"O Comunicador será reiniciado em {progresso.RemainingSeconds}s");
                    if (progresso.RemainingSeconds == 0)
                    {
                        _temporizadorAtualizacao.Stop();
                        await Task.Delay(450);
                        _allowExit = true;
                        Close();
                    }
                    break;
                case "launching":
                case "done":
                    _temporizadorAtualizacao.Stop();
                    _allowExit = true;
                    Close();
                    break;
                case "failed":
                    _temporizadorAtualizacao.Stop();
                    _atualizacaoEmAndamento = false;
                    MostrarSpinnerAtualizacao(false);
                    DefinirTextoAtualizacao("Falha na atualização · veja Configurações");
                    UpdateStatusText.ToolTip = progresso.Message;
                    break;
                case "up_to_date":
                    _temporizadorAtualizacao.Stop();
                    _atualizacaoEmAndamento = false;
                    MostrarSpinnerAtualizacao(false);
                    DefinirTextoAtualizacao("O Comunicador já está atualizado");
                    await Task.Delay(5000);
                    if (!_atualizacaoEmAndamento) UpdateStatus.Visibility = Visibility.Collapsed;
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // O script troca o arquivo de progresso atomicamente; repetimos na próxima leitura.
        }
        finally { _lendoProgressoAtualizacao = false; }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimarEntrada(TopNavigation, -12, 30);
        MostrarSecaoAtual();
        AnimarSecao();
        AtualizarMoldura();
        _ = AnimarAberturaAsync();
        _ = PreaquecerSecoesAsync(++_warmupGeneration);
    }

    private async Task AnimarAberturaAsync()
    {
        var duracaoPulso = TimeSpan.FromMilliseconds(850);
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        StartupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.91, 1.07, duracaoPulso)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = easing,
        });
        StartupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.91, 1.07, duracaoPulso)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = easing,
        });
        StartupIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, duracaoPulso)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = easing,
        });
        await Task.Delay(1500);
        var animacao = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        animacao.Completed += (_, _) =>
        {
            StartupIcon.BeginAnimation(OpacityProperty, null);
            StartupScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            StartupScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            StartupOverlay.Visibility = Visibility.Collapsed;
        };
        StartupOverlay.BeginAnimation(OpacityProperty, animacao);
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
        _viewModel.Configuracoes.AtualizacaoPainelIniciada += OnAtualizacaoPainelIniciada;
        _viewModel.Mensagens.AbrirCarrosselSolicitado += OnAbrirCarrosselSolicitado;
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
        if (_viewModel is not null) _viewModel.Configuracoes.AtualizacaoPainelIniciada -= OnAtualizacaoPainelIniciada;
        if (_viewModel is not null) _viewModel.Mensagens.AbrirCarrosselSolicitado -= OnAbrirCarrosselSolicitado;
        LimparNavegacaoAninhada();
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
            LimparNavegacaoAninhada();
            SectionContent.Content = _sectionHost;
            _navigationTransitionActive = true;
            AtualizarAnimacaoFundo();
            MostrarSecaoAtual();
            AnimarSecao();
            _ = RetomarFundoAsync(++_navigationPauseGeneration);
        }, DispatcherPriority.Render);
    }

    public void NavegarPara(FrameworkElement page, Action? onExit = null,
        bool canGoBack = true, bool lockNavigation = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        _navigationStack.Push(new NavigationFrame(SectionContent.Content, _currentNavigationCleanup,
            _navigationLocked, _currentBackAllowed));
        _currentNavigationCleanup = onExit;
        _currentBackAllowed = canGoBack;
        _navigationLocked = lockNavigation;
        SectionContent.Content = page;
        AtualizarNavegacaoAninhada();
        AnimarSecao();
    }

    public void NavegarVoltar()
    {
        if (_navigationLocked || !_currentBackAllowed || _navigationStack.Count == 0) return;
        _currentNavigationCleanup?.Invoke();
        _currentNavigationCleanup = null;
        var previous = _navigationStack.Pop();
        SectionContent.Content = previous.Content;
        _currentNavigationCleanup = previous.OnExit;
        _navigationLocked = previous.Locked;
        _currentBackAllowed = previous.BackAllowed;
        AtualizarNavegacaoAninhada();
        AnimarSecao();
    }

    private void LimparNavegacaoAninhada()
    {
        _currentNavigationCleanup?.Invoke();
        _currentNavigationCleanup = null;
        foreach (var frame in _navigationStack)
            frame.OnExit?.Invoke();
        _navigationStack.Clear();
        _navigationLocked = false;
        _currentBackAllowed = true;
        AtualizarNavegacaoAninhada();
    }

    private void AtualizarNavegacaoAninhada()
    {
        NavigationBackButton.Visibility = _navigationStack.Count > 0 && _currentBackAllowed
            ? Visibility.Visible : Visibility.Collapsed;
        TopNavigation.IsEnabled = !_navigationLocked;
    }

    private void DesbloquearNavegacao()
    {
        _navigationLocked = false;
        _currentBackAllowed = true;
        AtualizarNavegacaoAninhada();
    }

    private void OnNavigationBackClick(object sender, RoutedEventArgs e) => NavegarVoltar();

    private void OnMainWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || _navigationStack.Count == 0
            || _navigationLocked || !_currentBackAllowed) return;
        NavegarVoltar();
        e.Handled = true;
    }

    private void OnAbrirCarrosselSolicitado(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        var mensagens = _viewModel.Mensagens;
        var carrossel = new CarrosselWindow(mensagens.Enviador, mensagens.Destinatarios,
            () => mensagens.PodeGerenciarCarrossel, () => mensagens.PodeGerenciarCarrossel);
        NavegarPara(carrossel, carrossel.CancelarOperacoes);
    }

    public void MostrarResumoAtualizacao(string resumo) =>
        NavegarPara(new AtualizacaoConcluidaWindow(resumo));

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

    internal void RestaurarDaBandeja()
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
