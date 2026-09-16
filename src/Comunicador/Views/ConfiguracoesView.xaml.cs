using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class ConfiguracoesView : UserControl
{
    private ConfiguracoesViewModel? _viewModel;

    public ConfiguracoesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        ConectarViewModel(DataContext as ConfiguracoesViewModel);
        Dispatcher.BeginInvoke(AnimarPainelAtual, DispatcherPriority.Loaded);
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e) => DesconectarViewModel();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ConectarViewModel(e.NewValue as ConfiguracoesViewModel);
    }

    private void ConectarViewModel(ConfiguracoesViewModel? viewModel)
    {
        DesconectarViewModel();
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DesconectarViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguracoesViewModel.SecaoConfiguracoes))
            Dispatcher.BeginInvoke(AnimarPainelAtual, DispatcherPriority.Loaded);
    }

    private void AnimarPainelAtual()
    {
        if (_viewModel is null) return;
        var panel = _viewModel.SecaoConfiguracoes switch
        {
            "recebimento" => RecebimentoPanel,
            "geral" => GeralPanel,
            _ => PersonalizacaoPanel,
        };

        if (_viewModel.ReduzirMovimento)
        {
            panel.Opacity = 1;
            panel.RenderTransform = Transform.Identity;
            return;
        }

        var translate = new TranslateTransform();
        panel.RenderTransform = translate;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new BackEase { Amplitude = .1, EasingMode = EasingMode.EaseOut },
        });
    }
}
