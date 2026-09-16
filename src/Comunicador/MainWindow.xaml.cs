using System.ComponentModel;
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
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => DesconectarViewModel();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimarEntrada(TopNavigation, -12, 30);
        MostrarSecaoAtual();
        AnimarSecao();
        AtualizarMoldura();
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
        // As views são construídas uma única vez e reutilizadas. Isso elimina a
        // recompilação de XAML durante cada clique e deixa a troca de abas fluida.
        _sectionViews.Clear();
        _sectionViews[vm.Computadores] = new ComputadoresView { DataContext = vm.Computadores };
        _sectionViews[vm.Mensagens] = new MensagensView { DataContext = vm.Mensagens };
        _sectionViews[vm.Lembretes] = new LembretesView { DataContext = vm.Lembretes };
        _sectionViews[vm.Historico] = new HistoricoView { DataContext = vm.Historico };
        _sectionViews[vm.Logs] = new LogsView { DataContext = vm.Logs };
        _sectionViews[vm.Configuracoes] = new ConfiguracoesView { DataContext = vm.Configuracoes };
    }

    private void DesconectarViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _sectionViews.Clear();
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SecaoAtual)) return;
        Dispatcher.BeginInvoke(() =>
        {
            MostrarSecaoAtual();
            AnimarSecao();
        }, DispatcherPriority.Render);
    }

    private void MostrarSecaoAtual()
    {
        if (_viewModel is null) return;
        if (_sectionViews.TryGetValue(_viewModel.SecaoAtual, out var view))
        {
            SectionContent.Content = view;
        }
    }

    private void AnimarSecao()
    {
        if (_viewModel?.Configuracoes.ReduzirMovimento == true)
        {
            SectionContent.BeginAnimation(OpacityProperty, null);
            SectionTransform.BeginAnimation(TranslateTransform.YProperty, null);
            SectionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SectionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SectionContent.Opacity = 1;
            SectionTransform.Y = 0;
            SectionScale.ScaleX = 1;
            SectionScale.ScaleY = 1;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        SectionContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(175))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        });
        SectionTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        SectionScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(.992, 1, TimeSpan.FromMilliseconds(195))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        SectionScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(.992, 1, TimeSpan.FromMilliseconds(195))
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
        if (WindowFrame is null || MaximizeButton is null) return;
        var maximizada = WindowState == WindowState.Maximized;
        WindowFrame.CornerRadius = maximizada ? new CornerRadius(0) : new CornerRadius(13);
        MaximizeButton.Content = maximizada ? "\uE923" : "\uE922";
    }
}
