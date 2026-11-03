using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Comunicador.Controls;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class ComputadoresView : UserControl
{
    private readonly DispatcherTimer _tempoParaArrastar;
    private SpotlightCard? _cardPressionado;
    private Computador? _computadorPressionado;
    private Point _pontoInicial;
    private bool _arrasteLiberado;
    private bool _arrastando;
    private bool _ordemAlterada;
    private bool _arrastoConcluido;
    private Computador? _duploCliquePendente;

    public ComputadoresView()
    {
        InitializeComponent();
        _tempoParaArrastar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _tempoParaArrastar.Tick += (_, _) =>
        {
            _tempoParaArrastar.Stop();
            _arrasteLiberado = true;
        };

        PreviewMouseLeftButtonDown += Card_MouseDown;
        PreviewMouseLeftButtonUp += Card_MouseUp;
        PreviewMouseMove += Card_MouseMove;
    }

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _duploCliquePendente = null;
        var origem = e.OriginalSource as DependencyObject;
        if (OrigemInterativa(origem)) return;
        var card = EncontrarAncestral<SpotlightCard>(origem);
        if (card?.DataContext is not Computador computador) return;

        _cardPressionado = card;
        _computadorPressionado = computador;
        _pontoInicial = e.GetPosition(this);
        _arrasteLiberado = false;
        _arrastoConcluido = false;
        _ordemAlterada = false;
        AnimarPressao(card, pressionado: true);
        _tempoParaArrastar.Start();
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_cardPressionado is null || _computadorPressionado is null
            || e.LeftButton != MouseButtonState.Pressed || !_arrasteLiberado) return;

        var ponteiro = e.GetPosition(this);
        if (!_arrastando)
        {
            var deslocamento = ponteiro - _pontoInicial;
            if (deslocamento.Length < 6) return;
            IniciarArraste(ponteiro);
        }

        AtualizarPreviaArraste(ponteiro);
        ReordenarEnquantoArrasta(e.OriginalSource as DependencyObject, e.GetPosition(ListaCardsComputador));
        e.Handled = true;
    }

    private void Card_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _tempoParaArrastar.Stop();
        if (_arrastando)
        {
            _duploCliquePendente = null;
            if (_ordemAlterada && DataContext is ComputadoresViewModel viewModel)
            {
                try
                {
                    viewModel.SalvarOrdemComputadores();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
                {
                    viewModel.StatusMensagem = $"Não foi possível salvar a ordem dos cards: {ex.Message}";
                }
            }

            EncerrarArraste();
            _arrastoConcluido = true;
            e.Handled = true;
            return;
        }

        var abrirDepoisDoClique = _duploCliquePendente;
        var foiPressaoLonga = _arrasteLiberado;
        _duploCliquePendente = null;
        if (_cardPressionado is not null) AnimarPressao(_cardPressionado, pressionado: false);
        LimparPressao();
        if (abrirDepoisDoClique is not null && !foiPressaoLonga)
            AbrirAcoesComputador(abrirDepoisDoClique);
    }

    private void IniciarArraste(Point ponteiro)
    {
        if (_cardPressionado is null || _computadorPressionado is null) return;
        _arrastando = true;

        var largura = Math.Max(1, (int)Math.Ceiling(_cardPressionado.ActualWidth));
        var altura = Math.Max(1, (int)Math.Ceiling(_cardPressionado.ActualHeight));
        var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        imagem.Render(_cardPressionado);
        CardDragPreview.Width = largura;
        CardDragPreview.Height = altura;
        CardDragPreview.Background = new ImageBrush(imagem) { Stretch = Stretch.Fill };
        CardDragPreview.Visibility = Visibility.Visible;
        _cardPressionado.Opacity = 0.24;
        Mouse.Capture(this, CaptureMode.SubTree);
        AtualizarPreviaArraste(ponteiro);
    }

    private void AtualizarPreviaArraste(Point ponteiro)
    {
        Canvas.SetLeft(CardDragPreview, ponteiro.X - CardDragPreview.Width / 2);
        Canvas.SetTop(CardDragPreview, ponteiro.Y - CardDragPreview.Height / 2);
    }

    private void ReordenarEnquantoArrasta(DependencyObject? origem, Point ponteiroNaLista)
    {
        if (DataContext is not ComputadoresViewModel viewModel
            || _computadorPressionado is null) return;

        var alvo = EncontrarAncestral<SpotlightCard>(origem);
        if (alvo?.DataContext is not Computador computadorAlvo
            || ReferenceEquals(computadorAlvo, _computadorPressionado)) return;

        var centroAlvo = alvo.TransformToAncestor(ListaCardsComputador)
            .Transform(new Point(alvo.ActualWidth / 2, alvo.ActualHeight / 2));
        var estaAbaixo = ponteiroNaLista.Y > centroAlvo.Y;
        var mesmaLinha = Math.Abs(ponteiroNaLista.Y - centroAlvo.Y) <= alvo.ActualHeight * 0.45;
        var depoisDoAlvo = estaAbaixo || (mesmaLinha && ponteiroNaLista.X > centroAlvo.X);

        var indiceOrigem = viewModel.Computadores.IndexOf(_computadorPressionado);
        var indiceAlvo = viewModel.Computadores.IndexOf(computadorAlvo);
        if (indiceOrigem < 0 || indiceAlvo < 0) return;
        var novoIndice = indiceAlvo + (depoisDoAlvo ? 1 : 0);
        if (indiceOrigem < novoIndice) novoIndice--;
        if (viewModel.MoverComputadorDuranteArraste(_computadorPressionado, novoIndice))
            _ordemAlterada = true;
    }

    private void EncerrarArraste()
    {
        if (_cardPressionado is not null)
        {
            _cardPressionado.Opacity = 1;
            AnimarPressao(_cardPressionado, pressionado: false);
        }
        CardDragPreview.Visibility = Visibility.Collapsed;
        CardDragPreview.Background = null;
        Mouse.Capture(null);
        _arrastando = false;
        LimparPressao();
    }

    private void LimparPressao()
    {
        _cardPressionado = null;
        _computadorPressionado = null;
        _arrasteLiberado = false;
        _ordemAlterada = false;
    }

    private static void AnimarPressao(SpotlightCard card, bool pressionado)
    {
        if (card.ReducedMotion)
        {
            card.RenderTransform = Transform.Identity;
            return;
        }

        if (card.RenderTransform is not ScaleTransform escala)
            card.RenderTransform = escala = new ScaleTransform(1, 1);
        card.RenderTransformOrigin = new Point(0.5, 0.5);

        var destino = pressionado ? 0.965 : 1d;
        var inicio = pressionado ? 1d : 0.965;
        var duracao = TimeSpan.FromMilliseconds(pressionado ? 85 : 145);
        var animacao = new DoubleAnimation(inicio, destino, duracao)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, animacao, HandoffBehavior.SnapshotAndReplace);
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, animacao, HandoffBehavior.SnapshotAndReplace);
    }

    private static bool OrigemInterativa(DependencyObject? origem)
    {
        while (origem is not null && origem is not SpotlightCard)
        {
            if (origem is ButtonBase or TextBoxBase or ComboBox or Slider or ScrollBar or InteractiveBadge)
                return true;
            origem = ObterPai(origem);
        }
        return false;
    }

    private static T? EncontrarAncestral<T>(DependencyObject? elemento) where T : DependencyObject
    {
        while (elemento is not null)
        {
            if (elemento is T encontrado) return encontrado;
            elemento = ObterPai(elemento);
        }
        return null;
    }

    private static DependencyObject? ObterPai(DependencyObject elemento) => elemento is Visual or Visual3D
        ? VisualTreeHelper.GetParent(elemento)
        : LogicalTreeHelper.GetParent(elemento);

    private void AbrirGerenciamentoComputador_Click(object sender, RoutedEventArgs e)
        => AbrirGerenciamentoComputador(sender, e);

    private void AbrirGerenciamentoComputador_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_arrastando || _arrastoConcluido)
        {
            e.Handled = true;
            return;
        }
        if (OrigemInterativa(e.OriginalSource as DependencyObject)
            || sender is not FrameworkElement { DataContext: Computador computador }) return;
        _duploCliquePendente = computador;
        e.Handled = true;
    }

    private void AbrirAcoesComputador(Computador computador)
    {
        if (DataContext is not ComputadoresViewModel viewModel) return;

        if (!viewModel.PodeAbrirAcoesIndividuais)
        {
            viewModel.MostrarAvisoTopo("Ações individuais disponíveis somente para administradores.");
            return;
        }

        var main = Window.GetWindow(this) as MainWindow;
        var mainViewModel = main?.DataContext as MainViewModel;
        if (main is null || mainViewModel is null) return;
        viewModel.ComputadorGerenciado = computador;
        var acoes = new AcoesComputadorWindow(computador, viewModel, mainViewModel.Mensagens);
        acoes.ConfigurarSolicitado += (_, _) => AbrirConfiguracoes(main, computador, viewModel);
        acoes.EnviarSolicitado += imagem =>
        {
            mainViewModel.Mensagens.IniciarEnvioIndividual(computador, imagem);
            var compositor = new MensagensView { DataContext = mainViewModel.Mensagens };
            main.NavegarPara(compositor, mainViewModel.Mensagens.FinalizarEnvioIndividual);
        };
        main.NavegarPara(acoes, () => viewModel.ComputadorGerenciado = null);
    }

    private void AbrirGerenciamentoComputador(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Computador computador }
            || DataContext is not ComputadoresViewModel viewModel) return;

        if (Window.GetWindow(this) is MainWindow main)
            AbrirConfiguracoes(main, computador, viewModel, limparSelecaoAoVoltar: true);
        e.Handled = true;
    }

    private static void AbrirConfiguracoes(MainWindow main, Computador computador,
        ComputadoresViewModel viewModel, bool limparSelecaoAoVoltar = false)
    {
        viewModel.ComputadorGerenciado = computador;
        var painel = new GerenciarComputadorWindow { DataContext = viewModel };
        painel.VoltarSolicitado += (_, _) => main.NavegarVoltar();
        main.NavegarPara(painel, limparSelecaoAoVoltar
            ? () => viewModel.ComputadorGerenciado = null : null);
    }
}
