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
    private readonly Dictionary<string, ScrollViewer> _paineisConfiguracao = new(StringComparer.Ordinal);
    private ConfiguracoesViewModel? _viewModel;
    private long _ultimoApostrofo;
    private long _ultimoEventoApostrofo;
    private bool _abrindoLoginAdmin;

    public ConfiguracoesView()
    {
        InitializeComponent();
        _paineisConfiguracao.Add("personalizacao", PersonalizacaoPanel);
        _paineisConfiguracao.Add("recebimento", RecebimentoPanel);
        _paineisConfiguracao.Add("geral", GeralPanel);
        _paineisConfiguracao.Add("administrador", AdministradorPanel);
        foreach (var painel in _paineisConfiguracao.Values)
            ConfigPanelsGrid.Children.Remove(painel);
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
        if (EditorOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            FecharEditor();
            e.Handled = true;
            return;
        }
        if (_viewModel?.SecaoConfiguracoes != "geral") return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (key is not (Key.OemQuotes or Key.DeadCharProcessed) && virtualKey != 0xDE) return;
        e.Handled = true;
        RegistrarApostrofo();
    }

    private void EditarConfiguracao_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string secao } ||
            !_paineisConfiguracao.TryGetValue(secao, out var painel)) return;

        EditorTitulo.Text = secao switch
        {
            "personalizacao" => "Personalização",
            "recebimento" => "Recebimento",
            "geral" => "Painel e rede",
            "administrador" => "Administração global",
            _ => "Configurações",
        };
        EditorConteudo.Content = painel;
        EditorOverlay.Visibility = Visibility.Visible;
    }

    private void FecharEditor_Click(object sender, RoutedEventArgs e) => FecharEditor();

    private void FecharEditorAoClicarFora(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) FecharEditor();
    }

    private void FecharEditor()
    {
        EditorConteudo.Content = null;
        EditorOverlay.Visibility = Visibility.Collapsed;
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

    private Task AbrirLoginAdministradorAsync()
    {
        if (_viewModel is null || _abrindoLoginAdmin) return Task.CompletedTask;
        _abrindoLoginAdmin = true;
        if (Window.GetWindow(this) is not MainWindow main)
        {
            _abrindoLoginAdmin = false;
            return Task.CompletedTask;
        }
        AdminOperationStatus.Visibility = Visibility.Collapsed;
        var dialogo = new AdminLoginWindow(_viewModel.EstePainelEhOwner);
        dialogo.VoltarSolicitado += (_, _) => main.NavegarVoltar();
        dialogo.ContinuarSolicitado += async (_, _) =>
        {
            dialogo.IsEnabled = false;
            try
            {
                var resultado = await _viewModel!.AlternarAdministradorAsync(dialogo.Senha).ConfigureAwait(true);
                var sucesso = resultado is "created" or "transferred" or "disabled";
                var texto = resultado switch
                {
                    "created" => "Senha criada. Este computador agora é o administrador supremo.",
                    "transferred" => "O acesso administrativo foi recuperado neste computador.",
                    "disabled" => "O acesso administrativo foi desativado neste computador.",
                    "invalid_password" => "Senha incorreta.",
                    "password_too_short" => "Use uma senha com pelo menos 8 caracteres.",
                    _ => "Não foi possível concluir a validação agora.",
                };
                if (!sucesso)
                {
                    dialogo.MostrarErro(texto);
                    return;
                }
                AdminOperationStatus.Text = texto;
                AdminOperationStatus.Visibility = Visibility.Visible;
                main.NavegarVoltar();
            }
            finally { dialogo.IsEnabled = true; }
        };
        main.NavegarPara(dialogo, () => _abrindoLoginAdmin = false);
        return Task.CompletedTask;
    }

    private void AlterarSenhaAdmin_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.EstePainelEhOwner != true) return;
        if (Window.GetWindow(this) is not MainWindow main) return;
        AdminOperationStatus.Visibility = Visibility.Collapsed;
        var dialog = new AlterarSenhaAdminWindow();
        dialog.VoltarSolicitado += (_, _) => main.NavegarVoltar();
        dialog.ConfirmarSolicitado += async (_, _) =>
        {
            dialog.IsEnabled = false;
            try
            {
                var result = await _viewModel!.AlterarSenhaAdminAsync(dialog.SenhaAtual, dialog.NovaSenha);
                var message = result switch
                {
                    "changed" => "Senha administrativa alterada e sincronizada.",
                    "invalid_password" => "A senha atual está incorreta.",
                    "password_too_short" => "A nova senha deve ter de 8 a 256 caracteres.",
                    _ => "Não foi possível alterar a senha agora.",
                };
                if (result != "changed")
                {
                    dialog.MostrarErro(message);
                    return;
                }
                AdminOperationStatus.Text = message;
                AdminOperationStatus.Visibility = Visibility.Visible;
                main.NavegarVoltar();
            }
            finally { dialog.IsEnabled = true; }
        };
        main.NavegarPara(dialog);
    }
}
