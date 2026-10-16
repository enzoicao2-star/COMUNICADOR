using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Comunicador.Controls;

/// <summary>Miniatura animada que demonstra um efeito da galeria em loop.</summary>
public sealed class TransicaoEfeitoPreview : UserControl
{
    public static readonly DependencyProperty EffectIdProperty = DependencyProperty.Register(
        nameof(EffectId), typeof(string), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata("none", OnPlaybackOptionChanged));

    public static readonly DependencyProperty PreviewImageProperty = DependencyProperty.Register(
        nameof(PreviewImage), typeof(ImageSource), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata(null, OnPreviewChanged));

    public static readonly DependencyProperty PreviewLabelProperty = DependencyProperty.Register(
        nameof(PreviewLabel), typeof(string), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata("Imagem", OnPreviewChanged));

    public static readonly DependencyProperty IsExitPreviewProperty = DependencyProperty.Register(
        nameof(IsExitPreview), typeof(bool), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata(false, OnPlaybackOptionChanged));

    public static readonly DependencyProperty TimeModeProperty = DependencyProperty.Register(
        nameof(TimeMode), typeof(string), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata("speed", OnPlaybackOptionChanged));

    public static readonly DependencyProperty SpeedPercentProperty = DependencyProperty.Register(
        nameof(SpeedPercent), typeof(int), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata(100, OnPlaybackOptionChanged));

    public static readonly DependencyProperty DurationMsProperty = DependencyProperty.Register(
        nameof(DurationMs), typeof(int), typeof(TransicaoEfeitoPreview),
        new PropertyMetadata(600, OnPlaybackOptionChanged));

    private readonly Grid _animatedContent;
    private readonly Image _image;
    private readonly StackPanel _placeholder;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _rotation = new();
    private readonly TranslateTransform _translation = new();
    private CancellationTokenSource? _playback;

    public string EffectId { get => (string)GetValue(EffectIdProperty); set => SetValue(EffectIdProperty, value); }
    public ImageSource? PreviewImage { get => (ImageSource?)GetValue(PreviewImageProperty); set => SetValue(PreviewImageProperty, value); }
    public string PreviewLabel { get => (string)GetValue(PreviewLabelProperty); set => SetValue(PreviewLabelProperty, value); }
    public bool IsExitPreview { get => (bool)GetValue(IsExitPreviewProperty); set => SetValue(IsExitPreviewProperty, value); }
    public string TimeMode { get => (string)GetValue(TimeModeProperty); set => SetValue(TimeModeProperty, value); }
    public int SpeedPercent { get => (int)GetValue(SpeedPercentProperty); set => SetValue(SpeedPercentProperty, value); }
    public int DurationMs { get => (int)GetValue(DurationMsProperty); set => SetValue(DurationMsProperty, value); }

    public TransicaoEfeitoPreview()
    {
        var transform = new TransformGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(_rotation);
        transform.Children.Add(_translation);

        _image = new Image { Stretch = Stretch.UniformToFill };
        _placeholder = CriarPlaceholder();
        var content = new Grid();
        content.Children.Add(_image);
        content.Children.Add(_placeholder);
        content.RenderTransformOrigin = new Point(.5, .5);
        content.RenderTransform = transform;

        _animatedContent = content;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 19, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 82, 101)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Child = content,
        };
        Loaded += (_, _) => ReiniciarReproducao();
        Unloaded += (_, _) => PararReproducao();
    }

    private static StackPanel CriarPlaceholder()
    {
        var simbolo = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(141, 120, 255)),
            Child = new TextBlock
            {
                Text = "\uE768;",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var texto = new TextBlock
        {
            Text = "Vídeo selecionado",
            Foreground = Brushes.White,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 3, 2, 0),
        };
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { simbolo, texto },
        };
    }

    private static void OnPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TransicaoEfeitoPreview preview) return;
        preview._image.Source = preview.PreviewImage;
        preview._image.Visibility = preview.PreviewImage is null ? Visibility.Collapsed : Visibility.Visible;
        preview._placeholder.Visibility = preview.PreviewImage is null ? Visibility.Visible : Visibility.Collapsed;
        if (preview._placeholder.Children.Count > 1 && preview._placeholder.Children[1] is TextBlock label)
            label.Text = preview.PreviewLabel;
        preview.ReiniciarReproducao();
    }

    private static void OnPlaybackOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransicaoEfeitoPreview preview) preview.ReiniciarReproducao();
    }

    private void ReiniciarReproducao()
    {
        if (!IsLoaded) return;
        PararReproducao();
        var playback = new CancellationTokenSource();
        _playback = playback;
        _ = ReproduzirEmLoopAsync(playback.Token);
    }

    private void PararReproducao()
    {
        _playback?.Cancel();
        _playback?.Dispose();
        _playback = null;
        ResetarAnimacao();
    }

    private async Task ReproduzirEmLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var id = EffectId ?? "none";
                if (id.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    ResetarAnimacao();
                    await Task.Delay(850, cancellationToken);
                    continue;
                }

                if (IsExitPreview)
                {
                    AplicarEstado(1);
                    await Task.Delay(420, cancellationToken);
                    await AnimarParaEstadoAsync(id, 1, cancellationToken);
                    await Task.Delay(300, cancellationToken);
                }
                else
                {
                    AplicarEstado(0);
                    await AnimarParaEstadoAsync(id, 0, cancellationToken);
                    await Task.Delay(520, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnimarParaEstadoAsync(string id, double estadoVisivel, CancellationToken cancellationToken)
    {
        var oculto = CriarEstadoOculto(id);
        var visivel = EstadoVisual.Neutro;
        var de = estadoVisivel > .5 ? visivel : oculto;
        var para = estadoVisivel > .5 ? oculto : visivel;
        AplicarEstado(de);

        var duracao = TimeMode == "duration"
            ? Math.Clamp(DurationMs, 50, 10000)
            : Math.Clamp(60000d / Math.Clamp(SpeedPercent, 25, 250), 50, 10000);
        var easing = CriarEasing(id, estadoVisivel < .5);
        Animar(UIElement.OpacityProperty, de.Opacity, para.Opacity, duracao, easing);
        Animar(ScaleTransform.ScaleXProperty, de.Scale, para.Scale, duracao, easing, _scale);
        Animar(ScaleTransform.ScaleYProperty, de.Scale, para.Scale, duracao, easing, _scale);
        Animar(RotateTransform.AngleProperty, de.Angle, para.Angle, duracao, easing, _rotation);
        Animar(TranslateTransform.XProperty, de.X, para.X, duracao, easing, _translation);
        Animar(TranslateTransform.YProperty, de.Y, para.Y, duracao, easing, _translation);
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
        ResetarAnimacao();
        AplicarEstado(para);
    }

    private void Animar(DependencyProperty property, double from, double to, double durationMs,
        IEasingFunction easing, IAnimatable? target = null)
    {
        target ??= _animatedContent;
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void AplicarEstado(double estadoVisivel)
    {
        var id = EffectId ?? "none";
        var state = estadoVisivel > .5 ? EstadoVisual.Neutro : CriarEstadoOculto(id);
        ResetarAnimacao();
        _animatedContent.Opacity = state.Opacity;
        _scale.ScaleX = state.Scale;
        _scale.ScaleY = state.Scale;
        _rotation.Angle = state.Angle;
        _translation.X = state.X;
        _translation.Y = state.Y;
    }

    private void AplicarEstado(EstadoVisual state)
    {
        ResetarAnimacao();
        _animatedContent.Opacity = state.Opacity;
        _scale.ScaleX = state.Scale;
        _scale.ScaleY = state.Scale;
        _rotation.Angle = state.Angle;
        _translation.X = state.X;
        _translation.Y = state.Y;
    }

    private void ResetarAnimacao()
    {
        _animatedContent.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _translation.BeginAnimation(TranslateTransform.XProperty, null);
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static EstadoVisual CriarEstadoOculto(string effect)
    {
        var id = effect.ToLowerInvariant();
        var (x, y) = Deslocamento(id);
        var fade = Contem(id, "fade", "dissolve", "flash", "light", "burn", "glow", "blur", "focus",
            "mask", "reveal", "wipe", "iris", "curtain", "shutter", "pixel", "glitch", "noise",
            "static", "digital", "scan", "morph", "liquid", "shape", "shatter", "particle", "smoke",
            "fog", "frost", "ice", "electric", "aurora", "checker", "grid", "tile", "stripe", "radial",
            "spiral", "luma", "grain", "letter", "text", "word", "typewriter");
        var scale = Contem(id, "zoom", "scale", "pop", "expand", "shrink", "camera_pull", "morph",
            "portal", "pulse", "reveal", "collapse", "hero", "card_", "mask", "iris", "circle", "radial")
            ? .48 : 1;
        var angle = Contem(id, "rotate", "spin", "flip", "page_", "cube", "barrel", "tilt", "fold",
            "hinge", "carousel", "orbit", "origami", "tile_flip")
            ? Angulo(id) : 0;
        if (Contem(id, "glitch", "rgb", "chromatic", "vhs", "scramble", "digital_noise"))
        {
            x = 16;
            y = -5;
        }
        if (Contem(id, "bounce", "elastic", "spring", "rubber", "wobble", "jello", "overshoot"))
            scale = .72;
        return new EstadoVisual(fade ? 0 : 1, scale, angle, x, y);
    }

    private static (double X, double Y) Deslocamento(string id)
    {
        if (Contem(id, "left", "diagonal", "whip_pan", "truck_left")) return (-46, 0);
        if (Contem(id, "right", "truck_right")) return (46, 0);
        if (Contem(id, "up", "top", "pedestal_up")) return (0, -32);
        if (Contem(id, "down", "bottom", "drop", "fall", "pedestal_down")) return (0, 32);
        if (Contem(id, "slide", "push", "fly", "whip", "swipe", "wipe", "sweep", "reveal", "portal")) return (42, 0);
        if (Contem(id, "bounce", "elastic", "spring", "rubber", "wobble", "jello")) return (0, 10);
        return (0, 0);
    }

    private static double Angulo(string id)
    {
        var graus = Contem(id, "spin", "barrel", "carousel") ? 250
            : Contem(id, "flip", "page", "cube", "fold", "hinge", "tile") ? 100 : 40;
        return Contem(id, "left", "out") ? -graus : graus;
    }

    private static bool Contem(string id, params string[] termos) =>
        termos.Any(t => id.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static IEasingFunction CriarEasing(string efeito, bool entrada)
    {
        if (Contem(efeito, "bounce", "jello", "rubber", "spring"))
            return new ElasticEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn, Oscillations = 2, Springiness = 3 };
        if (Contem(efeito, "overshoot", "pop", "elastic", "recoil", "wobble"))
            return new BackEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn, Amplitude = .8 };
        return new CubicEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn };
    }

    private readonly record struct EstadoVisual(double Opacity, double Scale, double Angle, double X, double Y)
    {
        public static EstadoVisual Neutro { get; } = new(1, 1, 0, 0, 0);
    }
}
