using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class MensagensView : UserControl
{
    private readonly Dictionary<string, Border> _editoresMensagem = new(StringComparer.Ordinal);
    private ListCollectionView? _destinatariosFiltrados;
    private bool _reabrirGruposDepoisDosDestinatarios;

    public MensagensView()
    {
        InitializeComponent();
        _editoresMensagem.Add("composicao", CardComposicao);
        _editoresMensagem.Add("formato", CardFormato);
        _editoresMensagem.Add("midia", CardImagem);
        _editoresMensagem.Add("interacao", CardInteracao);
        foreach (var editor in _editoresMensagem.Values)
            PainelCardsMensagem.Children.Remove(editor);
        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += (_, _) => AtualizarPulsacao();
        Unloaded += (_, _) => PararPulsacao();
        DataContextChanged += (_, _) => PrepararListaDestinatarios();
        Loaded += (_, _) => PrepararListaDestinatarios();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => AtualizarPulsacao();

    private void AtualizarPulsacao()
    {
        if (!IsLoaded || !IsVisible)
        {
            PararPulsacao();
            return;
        }

        IndicadorEnvio.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, .28,
            TimeSpan.FromSeconds(.82))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void PararPulsacao() => IndicadorEnvio.BeginAnimation(UIElement.OpacityProperty, null);

    private void PrepararListaDestinatarios()
    {
        if (DataContext is not MensagensViewModel viewModel) return;
        _destinatariosFiltrados = new ListCollectionView(viewModel.Destinatarios)
        {
            Filter = FiltrarDestinatario,
        };
        ListaDestinatarios.ItemsSource = _destinatariosFiltrados;
    }

    private bool FiltrarDestinatario(object item)
    {
        if (item is not ComputadorSelecionavel destinatario) return false;
        var filtro = FiltroDestinatarios.Text.Trim();
        if (filtro.Length == 0) return true;

        return destinatario.Computador.NomeExibicao.Contains(filtro, StringComparison.CurrentCultureIgnoreCase)
            || destinatario.Computador.Nome.Contains(filtro, StringComparison.CurrentCultureIgnoreCase)
            || destinatario.Computador.EnderecoIpExibicao.Contains(filtro, StringComparison.CurrentCultureIgnoreCase);
    }

    private void AbrirDestinatarios_Click(object sender, RoutedEventArgs e)
    {
        _reabrirGruposDepoisDosDestinatarios = false;
        FecharTodosOverlays();
        FiltroDestinatarios.Clear();
        DestinatariosOverlay.Visibility = Visibility.Visible;
        _destinatariosFiltrados?.Refresh();
        FiltroDestinatarios.Focus();
    }

    private void FiltroDestinatarios_TextChanged(object sender, TextChangedEventArgs e) =>
        _destinatariosFiltrados?.Refresh();

    private void SelecionarTodosDestinatarios_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MensagensViewModel viewModel) return;
        foreach (var destinatario in viewModel.Destinatarios) destinatario.Selecionado = true;
    }

    private void LimparDestinatarios_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MensagensViewModel viewModel) return;
        foreach (var destinatario in viewModel.Destinatarios) destinatario.Selecionado = false;
    }

    private void FecharDestinatarios_Click(object sender, RoutedEventArgs e)
    {
        DestinatariosOverlay.Visibility = Visibility.Collapsed;
        if (!_reabrirGruposDepoisDosDestinatarios) return;
        _reabrirGruposDepoisDosDestinatarios = false;
        GruposOverlay.Visibility = Visibility.Visible;
    }

    private void AbrirModelos_Click(object sender, RoutedEventArgs e)
    {
        FecharTodosOverlays();
        CadastroModeloPainel.Visibility = Visibility.Collapsed;
        ModelosOverlay.Visibility = Visibility.Visible;
    }

    private void AbrirNovoModelo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MensagensViewModel viewModel)
        {
            viewModel.ModeloSelecionado = null;
            viewModel.NovoModeloNome = string.Empty;
        }
        FecharTodosOverlays();
        CadastroModeloPainel.Visibility = Visibility.Visible;
        ModelosOverlay.Visibility = Visibility.Visible;
    }

    private void MostrarCadastroModelo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MensagensViewModel viewModel)
        {
            viewModel.ModeloSelecionado = null;
            viewModel.NovoModeloNome = string.Empty;
        }
        CadastroModeloPainel.Visibility = Visibility.Visible;
    }

    private void AbrirGrupos_Click(object sender, RoutedEventArgs e)
    {
        FecharTodosOverlays();
        CadastroGrupoPainel.Visibility = Visibility.Collapsed;
        GruposOverlay.Visibility = Visibility.Visible;
    }

    private void MostrarCadastroGrupo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MensagensViewModel viewModel)
        {
            viewModel.GrupoSelecionado = null;
            viewModel.NovoGrupoNome = string.Empty;
        }
        CadastroGrupoPainel.Visibility = Visibility.Visible;
    }

    private void AbrirDestinatariosParaGrupo_Click(object sender, RoutedEventArgs e)
    {
        _reabrirGruposDepoisDosDestinatarios = true;
        FiltroDestinatarios.Clear();
        GruposOverlay.Visibility = Visibility.Collapsed;
        DestinatariosOverlay.Visibility = Visibility.Visible;
        _destinatariosFiltrados?.Refresh();
        FiltroDestinatarios.Focus();
    }

    private void AplicarModelo_Click(object sender, RoutedEventArgs e) =>
        ModelosOverlay.Visibility = Visibility.Collapsed;

    private void AplicarGrupo_Click(object sender, RoutedEventArgs e) =>
        GruposOverlay.Visibility = Visibility.Collapsed;

    private void FecharModelos_Click(object sender, RoutedEventArgs e) =>
        ModelosOverlay.Visibility = Visibility.Collapsed;

    private void FecharGrupos_Click(object sender, RoutedEventArgs e) =>
        GruposOverlay.Visibility = Visibility.Collapsed;

    private void AbrirAparencia_Click(object sender, RoutedEventArgs e)
    {
        FecharTodosOverlays();
        FecharEditorMensagem();
        AparenciaOverlay.Visibility = Visibility.Visible;
    }

    private void FecharAparencia_Click(object sender, RoutedEventArgs e) =>
        AparenciaOverlay.Visibility = Visibility.Collapsed;

    private void AbrirEditorMensagem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string secao } ||
            !_editoresMensagem.TryGetValue(secao, out var editor)) return;

        EditorMensagemTitulo.Text = secao switch
        {
            "composicao" => "Conteúdo do aviso",
            "formato" => "Formato do aviso",
            "midia" => "Imagem, vídeo ou áudio",
            "interacao" => "Interação e respostas",
            _ => "Configurações do envio",
        };
        EditorMensagemConteudo.Content = editor;
        EditorMensagemOverlay.Visibility = Visibility.Visible;
    }

    private void FecharEditorMensagem_Click(object sender, RoutedEventArgs e) => FecharEditorMensagem();

    private void FecharEditorMensagemAoClicarFora(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) FecharEditorMensagem();
    }

    private void FecharEditorMensagem()
    {
        EditorMensagemConteudo.Content = null;
        EditorMensagemOverlay.Visibility = Visibility.Collapsed;
    }

    private void FecharOverlayAoClicarFora(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (ReferenceEquals(sender, DestinatariosOverlay)) FecharDestinatarios_Click(sender, new RoutedEventArgs());
        else if (ReferenceEquals(sender, ModelosOverlay)) ModelosOverlay.Visibility = Visibility.Collapsed;
        else if (ReferenceEquals(sender, GruposOverlay)) GruposOverlay.Visibility = Visibility.Collapsed;
        else if (ReferenceEquals(sender, AparenciaOverlay)) AparenciaOverlay.Visibility = Visibility.Collapsed;
    }

    private void FecharTodosOverlays()
    {
        DestinatariosOverlay.Visibility = Visibility.Collapsed;
        ModelosOverlay.Visibility = Visibility.Collapsed;
        GruposOverlay.Visibility = Visibility.Collapsed;
        AparenciaOverlay.Visibility = Visibility.Collapsed;
    }
}
