using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class TransicoesMidiaView : UserControl
{
    private readonly TranslateTransform _previewOffset = new();

    public TransicoesMidiaView(MensagensViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewMedia.RenderTransform = _previewOffset;
    }

    private async void Reproduzir_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MensagensViewModel viewModel) return;
        ResetarPrevia();
        if (sender is Button botao) botao.IsEnabled = false;
        try
        {
            PrepararEntrada(viewModel.EfeitoEntrada);
            await Animar(viewModel.EfeitoEntrada, entrada: true, viewModel.VelocidadeEntradaPercentual);
            await Task.Delay(750);
            await Animar(viewModel.EfeitoSaida, entrada: false, viewModel.VelocidadeSaidaPercentual);
        }
        finally
        {
            ResetarPrevia();
            if (sender is Button botaoReproduzir) botaoReproduzir.IsEnabled = true;
        }
    }

    private void PrepararEntrada(string efeito)
    {
        _previewOffset.X = 0;
        _previewOffset.Y = 0;
        PreviewMedia.Opacity = efeito is "fade" or "top" or "bottom" or "left" or "right" ? 0 : 1;
        var (x, y) = Deslocamento(efeito, 210);
        _previewOffset.X = x;
        _previewOffset.Y = y;
    }

    private async Task Animar(string efeito, bool entrada, int velocidade)
    {
        if (efeito == "none") return;
        var duracao = TimeSpan.FromMilliseconds(Math.Clamp(60000d / velocidade, 180, 2400));
        var (x, y) = entrada ? (0d, 0d) : Deslocamento(efeito, 210);
        var opacidadeFinal = efeito == "fade" || efeito is "top" or "bottom" or "left" or "right"
            ? entrada ? 1d : 0d
            : 1d;
        var easing = new CubicEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn };
        var tarefas = new List<Task>
        {
            AnimarPropriedade(PreviewMedia, UIElement.OpacityProperty, PreviewMedia.Opacity,
                opacidadeFinal, duracao, easing),
            AnimarPropriedade(_previewOffset, TranslateTransform.XProperty, _previewOffset.X, x, duracao, easing),
            AnimarPropriedade(_previewOffset, TranslateTransform.YProperty, _previewOffset.Y, y, duracao, easing),
        };
        await Task.WhenAll(tarefas);
    }

    private static (double X, double Y) Deslocamento(string efeito, double distancia) => efeito switch
    {
        "top" => (0, -distancia),
        "bottom" => (0, distancia),
        "left" => (-distancia, 0),
        "right" => (distancia, 0),
        _ => (0, 0),
    };

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
        _previewOffset.X = 0;
        _previewOffset.Y = 0;
        PreviewMedia.Opacity = 1;
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
