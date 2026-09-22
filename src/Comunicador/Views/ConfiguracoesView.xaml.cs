using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class ConfiguracoesView : UserControl
{
    private ConfiguracoesViewModel? _viewModel;
    private long _ultimoApostrofo;
    private long _ultimoEventoApostrofo;
    private bool _abrindoLoginAdmin;

    public ConfiguracoesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
        AddHandler(TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnPreviewTextInput), true);
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
        {
            Dispatcher.BeginInvoke(AnimarPainelAtual, DispatcherPriority.Loaded);
            if (_viewModel?.SecaoConfiguracoes == "geral")
                Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        }
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

        var reduzido = _viewModel.ReduzirMovimento;
        var translate = new TranslateTransform();
        panel.RenderTransform = translate;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(reduzido ? .72 : 0, 1,
            TimeSpan.FromMilliseconds(reduzido ? 330 : 280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(reduzido ? 5 : 22, 0,
            TimeSpan.FromMilliseconds(reduzido ? 420 : 360))
        {
            EasingFunction = reduzido
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { Amplitude = .1, EasingMode = EasingMode.EaseOut },
        });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel?.SecaoConfiguracoes != "geral") return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (key is not (Key.OemQuotes or Key.DeadCharProcessed) && virtualKey != 0xDE) return;
        e.Handled = true;
        RegistrarApostrofo();
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_viewModel?.SecaoConfiguracoes != "geral") return;
        if (string.IsNullOrEmpty(e.Text) || !e.Text.Any(c => c is '\'' or '´' or '`' or '’')) return;
        e.Handled = true;
        RegistrarApostrofo();
    }

    private void RegistrarApostrofo()
    {
        if (_viewModel?.SecaoConfiguracoes != "geral" || _abrindoLoginAdmin) return;

        var agora = Stopwatch.GetTimestamp();
        if (_ultimoEventoApostrofo != 0 &&
            Stopwatch.GetElapsedTime(_ultimoEventoApostrofo, agora) < TimeSpan.FromMilliseconds(75)) return;
        _ultimoEventoApostrofo = agora;
        var intervalo = Stopwatch.GetElapsedTime(_ultimoApostrofo, agora);
        _ultimoApostrofo = agora;
        if (intervalo > TimeSpan.FromMilliseconds(850)) return;

        _ultimoApostrofo = 0;
        _ = AbrirLoginAdministradorAsync();
    }

    private async Task AbrirLoginAdministradorAsync()
    {
        if (_viewModel is null || _abrindoLoginAdmin) return;
        _abrindoLoginAdmin = true;
        var dialogo = new AdminLoginWindow(_viewModel.EstePainelEhOwner)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialogo.ShowDialog() != true || string.IsNullOrWhiteSpace(dialogo.Senha))
        {
            _abrindoLoginAdmin = false;
            return;
        }

        IsEnabled = false;
        try
        {
            var resultado = await _viewModel.AlternarAdministradorAsync(dialogo.Senha).ConfigureAwait(true);
            var texto = resultado switch
            {
                "created" => "Senha criada. Este computador agora é o administrador supremo.",
                "transferred" => "O acesso administrativo foi recuperado neste computador.",
                "disabled" => "O acesso administrativo foi desativado neste computador.",
                "invalid_password" => "Senha incorreta.",
                "password_too_short" => "Use uma senha com pelo menos 8 caracteres.",
                _ => "Não foi possível concluir a validação agora.",
            };
            MessageBox.Show(texto, "Administrador", MessageBoxButton.OK,
                resultado is "created" or "transferred" or "disabled"
                    ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            _abrindoLoginAdmin = false;
        }
    }
}
