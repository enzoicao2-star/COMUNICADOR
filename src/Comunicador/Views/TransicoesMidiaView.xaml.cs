using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class TransicoesMidiaView : UserControl
{
    private readonly TranslateTransform _previewOffset = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly RotateTransform _previewRotation = new();
    private bool _entradaSelecionada = true;
    private bool _previaEmExecucao;

    public TransicoesMidiaView(MensagensViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewMedia.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { _previewScale, _previewRotation, _previewOffset },
        };
        ListaCategorias.ItemsSource = TransicaoCatalogo.Categorias;
        if (ListaCategorias.Items.Count > 0) ListaCategorias.SelectedIndex = 0;
    }

    private void Categoria_SelectionChanged(object sender, SelectionChangedEventArgs e) => AtualizarGaleria();

    private void BuscaTransicoes_TextChanged(object sender, TextChangedEventArgs e) => AtualizarGaleria();

    private void AtualizarGaleria()
    {
        if (GaleriaEfeitos is null || ListaCategorias?.SelectedItem is not CategoriaTransicao categoria) return;
        var busca = BuscaTransicoes?.Text?.Trim();
        GaleriaEfeitos.ItemsSource = string.IsNullOrWhiteSpace(busca)
            ? categoria.Opcoes
            : categoria.Opcoes.Where(o => o.Nome.Contains(busca, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private void LadoTransicao_Checked(object sender, RoutedEventArgs e) =>
        _entradaSelecionada = ReferenceEquals(sender, LadoEntradaRadio);

    private async void SelecionarEfeito_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string efeito } || DataContext is not MensagensViewModel vm) return;
        if (_entradaSelecionada) vm.EfeitoEntrada = efeito;
        else vm.EfeitoSaida = efeito;
        await ReproduzirPreviaAsync(vm);
    }

    private async void Reproduzir_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MensagensViewModel vm) await ReproduzirPreviaAsync(vm);
    }

    private async Task ReproduzirPreviaAsync(MensagensViewModel vm)
    {
        if (_previaEmExecucao) return;
        _previaEmExecucao = true;
        ReproduzirButton.IsEnabled = false;
        GaleriaEfeitos.IsEnabled = false;
        try
        {
            ResetarPrevia();
            PrepararEntrada(vm.EfeitoEntrada);
            await Animar(vm.EfeitoEntrada, true, vm.ModoTempoEntrada,
                vm.VelocidadeEntradaPercentual, vm.DuracaoEntradaMs);
            await Task.Delay(550);
            await Animar(vm.EfeitoSaida, false, vm.ModoTempoSaida,
                vm.VelocidadeSaidaPercentual, vm.DuracaoSaidaMs);
        }
        finally
        {
            ResetarPrevia();
            ReproduzirButton.IsEnabled = true;
            GaleriaEfeitos.IsEnabled = true;
            _previaEmExecucao = false;
        }
    }

    private void PrepararEntrada(string efeito)
    {
        _previewOffset.X = 0;
        _previewOffset.Y = 0;
        _previewScale.ScaleX = 1;
        _previewScale.ScaleY = 1;
        _previewRotation.Angle = 0;
        if (efeito == "none") return;
        var (dx, dy) = Deslocamento(efeito, 150);
        _previewOffset.X = dx;
        _previewOffset.Y = dy;
        if (UsaEscala(efeito))
        {
            _previewScale.ScaleX = .62;
            _previewScale.ScaleY = .62;
        }
        if (UsaRotacao(efeito)) _previewRotation.Angle = AnguloRotacao(efeito);
        PreviewMedia.Opacity = 0;
    }

    private async Task Animar(string efeito, bool entrada, string modoTempo, int velocidade, int duracaoMs)
    {
        if (efeito == "none") return;
        var duracao = TimeSpan.FromMilliseconds(modoTempo == "duration"
            ? Math.Clamp(duracaoMs, 50, 10000)
            : Math.Clamp(60000d / Math.Clamp(velocidade, 25, 250), 50, 10000));
        var deslocamento = Deslocamento(efeito, 150);
        var destinoX = entrada ? 0 : deslocamento.X;
        var destinoY = entrada ? 0 : deslocamento.Y;
        var tarefas = new List<Task>
        {
            AnimarPropriedade(PreviewMedia, UIElement.OpacityProperty, PreviewMedia.Opacity,
                entrada ? 1 : 0, duracao, CriarEasing(efeito, entrada)),
            AnimarPropriedade(_previewOffset, TranslateTransform.XProperty, _previewOffset.X,
                destinoX, duracao, CriarEasing(efeito, entrada)),
            AnimarPropriedade(_previewOffset, TranslateTransform.YProperty, _previewOffset.Y,
                destinoY, duracao, CriarEasing(efeito, entrada)),
        };
        var escalaDestino = UsaEscala(efeito) ? entrada ? 1d : .68d : 1d;
        tarefas.Add(AnimarPropriedade(_previewScale, ScaleTransform.ScaleXProperty,
            _previewScale.ScaleX, escalaDestino, duracao, CriarEasing(efeito, entrada)));
        tarefas.Add(AnimarPropriedade(_previewScale, ScaleTransform.ScaleYProperty,
            _previewScale.ScaleY, escalaDestino, duracao, CriarEasing(efeito, entrada)));
        var rotacaoDestino = UsaRotacao(efeito) ? entrada ? 0 : AnguloRotacao(efeito) : 0;
        tarefas.Add(AnimarPropriedade(_previewRotation, RotateTransform.AngleProperty,
            _previewRotation.Angle, rotacaoDestino, duracao, CriarEasing(efeito, entrada)));
        await Task.WhenAll(tarefas);
    }

    private static (double X, double Y) Deslocamento(string efeito, double distancia)
    {
        var id = efeito.ToLowerInvariant();
        if (Contem(id, "left", "diagonal", "whip_pan", "truck_left")) return (-distancia, 0);
        if (Contem(id, "right", "truck_right")) return (distancia, 0);
        if (Contem(id, "up", "top", "pedestal_up")) return (0, -distancia);
        if (Contem(id, "down", "bottom", "drop", "fall", "pedestal_down")) return (0, distancia);
        if (Contem(id, "slide", "push", "fly", "whip", "swipe", "wipe", "sweep", "reveal", "portal"))
            return (distancia, 0);
        return (0, 0);
    }

    private static bool UsaEscala(string efeito) => Contem(efeito, "zoom", "scale", "pop", "expand", "shrink",
        "camera_pull", "morph", "portal", "pulse", "reveal", "collapse", "hero", "card_");

    private static bool UsaRotacao(string efeito) => Contem(efeito, "rotate", "spin", "flip", "page_", "cube",
        "barrel", "tilt", "fold", "hinge", "carousel", "orbit", "origami", "tile_flip");

    private static double AnguloRotacao(string efeito)
    {
        var id = efeito.ToLowerInvariant();
        var graus = Contem(id, "spin", "barrel", "carousel") ? 270
            : Contem(id, "flip", "page", "cube", "fold", "hinge", "tile") ? 110 : 42;
        return id.Contains("left", StringComparison.Ordinal) || id.Contains("out", StringComparison.Ordinal)
            ? -graus : graus;
    }

    private static bool Contem(string valor, params string[] termos) =>
        termos.Any(t => valor.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static IEasingFunction CriarEasing(string efeito, bool entrada)
    {
        if (Contem(efeito, "bounce", "jello", "rubber", "spring"))
            return new ElasticEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn, Oscillations = 2, Springiness = 3 };
        if (Contem(efeito, "overshoot", "pop", "elastic", "recoil", "wobble"))
            return new BackEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn, Amplitude = .8 };
        return new CubicEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn };
    }

    private static Task AnimarPropriedade(IAnimatable alvo, DependencyProperty propriedade,
        double de, double para, TimeSpan duracao, IEasingFunction easing)
    {
        var concluida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animacao = new DoubleAnimation(de, para, duracao)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        animacao.Completed += (_, _) => concluida.TrySetResult();
        alvo.BeginAnimation(propriedade, animacao, HandoffBehavior.SnapshotAndReplace);
        return concluida.Task;
    }

    private void ResetarPrevia()
    {
        PreviewMedia.BeginAnimation(UIElement.OpacityProperty, null);
        _previewOffset.BeginAnimation(TranslateTransform.XProperty, null);
        _previewOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _previewScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _previewScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _previewRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _previewOffset.X = 0;
        _previewOffset.Y = 0;
        _previewScale.ScaleX = 1;
        _previewScale.ScaleY = 1;
        _previewRotation.Angle = 0;
        PreviewMedia.Opacity = 1;
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
