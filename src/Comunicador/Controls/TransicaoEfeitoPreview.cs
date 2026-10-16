using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

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
    private readonly Border _effectOverlay;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _rotation = new();
    private readonly TranslateTransform _translation = new();
    private readonly SkewTransform _skew = new();
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
        transform.Children.Add(_skew);
        transform.Children.Add(_translation);

        _image = new Image { Stretch = Stretch.UniformToFill };
        _placeholder = CriarPlaceholder();
        _effectOverlay = new Border { Background = Brushes.Transparent, Opacity = 0, IsHitTestVisible = false };
        var content = new Grid();
        content.Children.Add(_image);
        content.Children.Add(_placeholder);
        content.Children.Add(_effectOverlay);
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
        Loaded += (_, _) => AtualizarReproducaoVisibilidade();
        Unloaded += (_, _) => PararReproducao();
        IsVisibleChanged += (_, _) => AtualizarReproducaoVisibilidade();
    }

    private void AtualizarReproducaoVisibilidade()
    {
        if (IsLoaded && IsVisible) ReiniciarReproducao();
        else PararReproducao();
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
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.LowQuality);

        var duracao = TimeMode == "duration"
            ? Math.Clamp(DurationMs, 50, 10000)
            : Math.Clamp(60000d / Math.Clamp(SpeedPercent, 25, 250), 50, 10000);
        var easing = CriarEasing(id, estadoVisivel < .5);

        if (EhDissolucao(id))
        {
            _animatedContent.Opacity = 1;
            await AnimarDissolucaoAsync(id, estadoVisivel < .5, duracao, easing, cancellationToken);
            ResetarAnimacao();
            AplicarEstado(estadoVisivel > .5 ? para with { Opacity = 0 } : para);
            return;
        }

        if (EhMascara(id))
        {
            _animatedContent.Opacity = 1;
            await AnimarMascaraAsync(id, estadoVisivel < .5, duracao, easing, cancellationToken);
            ResetarAnimacao();
            AplicarEstado(estadoVisivel > .5 ? para with { Opacity = 0 } : para);
            return;
        }

        if (Contem(id, "blur", "focus", "defocus", "depth_of_field"))
        {
            _animatedContent.Opacity = 1;
            await AnimarDesfoqueAsync(estadoVisivel < .5, duracao, easing, cancellationToken);
            ResetarAnimacao();
            AplicarEstado(para);
            return;
        }

        if (EhEfeitoDeLuz(id))
        {
            _animatedContent.Opacity = 1;
            await AnimarCamadaDeCorAsync(id, estadoVisivel < .5, duracao, easing, cancellationToken);
            ResetarAnimacao();
            AplicarEstado(para);
            return;
        }

        if (Contem(id, "glitch", "rgb", "chromatic", "vhs", "static", "scramble", "scanline", "digital_noise",
            "handheld_shake", "pixel", "data_stream", "matrix_rain", "circuit", "hologram", "scan_transition", "film_reel"))
        {
            await AnimarFalhaDigitalAsync(id, estadoVisivel < .5, duracao, cancellationToken);
            ResetarAnimacao();
            AplicarEstado(para);
            return;
        }

        var escalaXDe = de.Scale;
        var escalaXPara = para.Scale;
        var escalaYDe = de.Scale;
        var escalaYPara = para.Scale;
        var anguloDe = de.Angle;
        var anguloPara = para.Angle;
        var skewXDe = 0d;
        var skewXPara = 0d;
        var skewYDe = 0d;
        var skewYPara = 0d;
        if (Contem(id, "flip_horizontal", "flip_card", "3d_flip", "tile_flip"))
        {
            escalaXDe = estadoVisivel < .5 ? .02 : 1;
            escalaXPara = estadoVisivel < .5 ? 1 : .02;
            anguloDe = anguloPara = 0;
        }
        else if (Contem(id, "flip_vertical"))
        {
            escalaYDe = estadoVisivel < .5 ? .02 : 1;
            escalaYPara = estadoVisivel < .5 ? 1 : .02;
            anguloDe = anguloPara = 0;
        }
        else if (Contem(id, "squeeze"))
        {
            escalaXDe = estadoVisivel < .5 ? .14 : 1;
            escalaXPara = estadoVisivel < .5 ? 1 : .14;
        }
        else if (Contem(id, "stretch", "rubber_band"))
        {
            escalaYDe = estadoVisivel < .5 ? .45 : 1;
            escalaYPara = estadoVisivel < .5 ? 1 : .45;
            escalaXDe = estadoVisivel < .5 ? 1.35 : 1;
            escalaXPara = estadoVisivel < .5 ? 1 : 1.35;
        }
        else if (Contem(id, "skew"))
        {
            skewXDe = estadoVisivel < .5 ? -20 : 0;
            skewXPara = estadoVisivel < .5 ? 0 : -20;
            skewYDe = estadoVisivel < .5 ? 10 : 0;
            skewYPara = estadoVisivel < .5 ? 0 : 10;
        }

        Animar(UIElement.OpacityProperty, de.Opacity, para.Opacity, duracao, easing);
        Animar(ScaleTransform.ScaleXProperty, escalaXDe, escalaXPara, duracao, easing, _scale);
        Animar(ScaleTransform.ScaleYProperty, escalaYDe, escalaYPara, duracao, easing, _scale);
        Animar(RotateTransform.AngleProperty, anguloDe, anguloPara, duracao, easing, _rotation);
        Animar(SkewTransform.AngleXProperty, skewXDe, skewXPara, duracao, easing, _skew);
        Animar(SkewTransform.AngleYProperty, skewYDe, skewYPara, duracao, easing, _skew);
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
        _skew.BeginAnimation(SkewTransform.AngleXProperty, null);
        _skew.BeginAnimation(SkewTransform.AngleYProperty, null);
        _animatedContent.Clip = null;
        _animatedContent.Effect = null;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _effectOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        _effectOverlay.Opacity = 0;
        _effectOverlay.Background = Brushes.Transparent;
    }

    private async Task AnimarDissolucaoAsync(string id, bool entrada, double duracao,
        IEasingFunction easing, CancellationToken cancellationToken)
    {
        var (colunas, linhas) = id switch
        {
            "pixel_dissolve" => (14, 9),
            "film_grain_dissolve" => (12, 8),
            "grid_dissolve" => (7, 5),
            "tiles_dissolve" => (5, 4),
            "mosaic_transition" => (6, 5),
            "mosaic_scatter" => (8, 6),
            _ => (11, 8),
        };
        var largura = Math.Max(_animatedContent.ActualWidth, 1);
        var altura = Math.Max(_animatedContent.ActualHeight, 1);
        var celulas = new List<(int X, int Y, RectangleGeometry Forma)>(colunas * linhas);
        var mascara = new GeometryGroup();
        for (var y = 0; y < linhas; y++)
        for (var x = 0; x < colunas; x++)
        {
            var forma = new RectangleGeometry();
            celulas.Add((x, y, forma));
            mascara.Children.Add(forma);
        }

        IEnumerable<(int X, int Y, RectangleGeometry Forma)> ordem;
        if (id is "pixel_dissolve" or "dissolve" or "film_grain_dissolve" or "mosaic_scatter")
        {
            var seed = id.Aggregate(17, (valor, caractere) => unchecked(valor * 31 + caractere));
            var aleatorio = new Random(seed);
            ordem = celulas.OrderBy(_ => aleatorio.Next()).ToArray();
        }
        else if (id == "mosaic_transition")
            ordem = celulas.OrderBy(c => Math.Pow(c.X - (colunas - 1) / 2d, 2)
                + Math.Pow(c.Y - (linhas - 1) / 2d, 2)).ToArray();
        else if (id == "grid_dissolve")
            ordem = celulas.OrderBy(c => c.X + c.Y).ThenBy(c => c.Y).ToArray();
        else
            ordem = celulas.OrderBy(c => c.Y).ThenBy(c => c.Y % 2 == 0 ? c.X : -c.X).ToArray();

        var fila = ordem.ToArray();
        var duracaoCelula = Math.Max(40, duracao * .68);
        var intervalo = Math.Max(0, duracao - duracaoCelula);
        for (var indice = 0; indice < fila.Length; indice++)
        {
            var celula = fila[indice];
            var completa = new Rect(celula.X * largura / colunas, celula.Y * altura / linhas,
                largura / colunas + 1, altura / linhas + 1);
            var centro = new Rect(completa.X + completa.Width / 2, completa.Y + completa.Height / 2, 0, 0);
            var atraso = fila.Length <= 1 ? 0 : intervalo * indice / (fila.Length - 1);
            celula.Forma.BeginAnimation(RectangleGeometry.RectProperty,
                new RectAnimation(entrada ? centro : completa, entrada ? completa : centro,
                    TimeSpan.FromMilliseconds(duracaoCelula))
                {
                    BeginTime = TimeSpan.FromMilliseconds(atraso),
                    EasingFunction = easing,
                });
        }

        _animatedContent.Clip = mascara;
        if (id == "pixel_dissolve") RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
    }

    private static bool EhDissolucao(string id) =>
        id is "dissolve" or "pixel_dissolve" or "grid_dissolve" or "tiles_dissolve"
            or "mosaic_transition" or "mosaic_scatter" or "film_grain_dissolve";

    private async Task AnimarMascaraAsync(string id, bool entrada, double duracao,
        IEasingFunction easing, CancellationToken cancellationToken)
    {
        var largura = Math.Max(_animatedContent.ActualWidth, 1);
        var altura = Math.Max(_animatedContent.ActualHeight, 1);

        if (Contem(id, "wipe_left", "wipe_right", "wipe_up", "wipe_down", "curtain", "shutter",
            "typewriter", "letter_by_letter", "word_by_word", "text_reveal", "letterbox"))
        {
            var mascara = new RectangleGeometry();
            _animatedContent.Clip = mascara;
            var recolhida = CriarRetanguloRecolhido(id, largura, altura);
            mascara.BeginAnimation(RectangleGeometry.RectProperty,
                new RectAnimation(entrada ? recolhida : new Rect(0, 0, largura, altura),
                    entrada ? new Rect(0, 0, largura, altura) : recolhida,
                    TimeSpan.FromMilliseconds(duracao)) { EasingFunction = easing });
            await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
            return;
        }

        var escala = new ScaleTransform(entrada ? 0 : 1, entrada ? 0 : 1, largura / 2, altura / 2);
        var forma = CriarFormaMascara(id, largura, altura);
        forma.Transform = escala;
        _animatedContent.Clip = forma;
        escala.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(entrada ? 0 : 1, entrada ? 1 : 0, TimeSpan.FromMilliseconds(duracao)) { EasingFunction = easing });
        escala.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(entrada ? 0 : 1, entrada ? 1 : 0, TimeSpan.FromMilliseconds(duracao)) { EasingFunction = easing });
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
    }

    private static Rect CriarRetanguloRecolhido(string id, double largura, double altura)
    {
        if (Contem(id, "curtain")) return new Rect(largura / 2, 0, 0, altura);
        if (Contem(id, "shutter")) return new Rect(0, altura / 2, largura, 0);
        if (Contem(id, "right")) return new Rect(largura, 0, 0, altura);
        if (Contem(id, "up")) return new Rect(0, 0, largura, 0);
        if (Contem(id, "down")) return new Rect(0, altura, largura, 0);
        return new Rect(0, 0, 0, altura);
    }

    private static Geometry CriarFormaMascara(string id, double largura, double altura)
    {
        if (Contem(id, "blinds", "stripes", "bars", "letterbox", "checkerboard", "grid", "squares", "tiles", "mosaic"))
        {
            var padrao = new GeometryGroup();
            if (Contem(id, "blinds", "stripes", "bars"))
            {
                var vertical = Contem(id, "vertical");
                const int quantidade = 7;
                for (var i = 0; i < quantidade; i++)
                {
                    if (vertical)
                        padrao.Children.Add(new RectangleGeometry(new Rect(i * largura / quantidade, 0,
                            largura / quantidade * .58, altura)));
                    else
                        padrao.Children.Add(new RectangleGeometry(new Rect(0, i * altura / quantidade,
                            largura, altura / quantidade * .58)));
                }
            }
            else
            {
                const int colunas = 5;
                const int linhas = 4;
                for (var y = 0; y < linhas; y++)
                for (var x = 0; x < colunas; x++)
                {
                    if (Contem(id, "checkerboard") && (x + y) % 2 != 0) continue;
                    if (Contem(id, "tiles", "mosaic") && (x + y) % 3 == 1) continue;
                    if (Contem(id, "stagger") && (x + y) % 2 != 0) continue;
                    padrao.Children.Add(new RectangleGeometry(new Rect(
                        x * largura / colunas, y * altura / linhas,
                        largura / colunas * .86, altura / linhas * .82)));
                }
            }
            return padrao;
        }

        var pontos = CriarPontosMascara(id, largura, altura);
        var figura = new PathFigure { StartPoint = pontos[0], IsClosed = true };
        figura.Segments.Add(new PolyLineSegment(pontos.Skip(1), true));
        return new PathGeometry([figura]);
    }

    private static Point[] CriarPontosMascara(string id, double largura, double altura)
    {
        if (Contem(id, "diamond"))
            return [new(largura / 2, 0), new(largura, altura / 2), new(largura / 2, altura), new(0, altura / 2)];

        if (Contem(id, "star"))
            return Enumerable.Range(0, 10).Select(i => PontoPolar(largura, altura,
                i * Math.PI / 5 - Math.PI / 2, i % 2 == 0 ? .5 : .22)).ToArray();

        if (Contem(id, "hexagon", "honeycomb"))
            return Enumerable.Range(0, 6).Select(i => PontoPolar(largura, altura, i * Math.PI / 3, .5)).ToArray();

        if (Contem(id, "heart"))
        {
            return Enumerable.Range(0, 32).Select(i =>
            {
                var t = i * 2 * Math.PI / 32;
                var x = 16 * Math.Pow(Math.Sin(t), 3);
                var y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                return new Point(largura * (.5 + x / 36), altura * (.48 - y / 36));
            }).ToArray();
        }

        if (Contem(id, "wave", "ripple", "liquid", "gooey", "blob", "morph", "warp", "swirl", "vortex", "ink", "paint", "melt"))
        {
            const int segmentosOndulados = 36;
            return Enumerable.Range(0, segmentosOndulados).Select(i =>
            {
                var angulo = i * 2 * Math.PI / segmentosOndulados;
                var irregularidade = Contem(id, "wave", "ripple")
                    ? .43 + .055 * Math.Sin(angulo * 5)
                    : .43 + .075 * Math.Sin(angulo * 3 + .6) + .035 * Math.Cos(angulo * 7);
                return PontoPolar(largura, altura, angulo, irregularidade);
            }).ToArray();
        }

        var segmentos = Contem(id, "circle", "iris", "radial", "spiral", "clock") ? 32 : 8;
        return Enumerable.Range(0, segmentos)
            .Select(i => PontoPolar(largura, altura, i * 2 * Math.PI / segmentos, .5))
            .ToArray();
    }

    private static Point PontoPolar(double largura, double altura, double angulo, double raio) =>
        new(largura / 2 + Math.Cos(angulo) * largura * raio,
            altura / 2 + Math.Sin(angulo) * altura * raio);

    private async Task AnimarDesfoqueAsync(bool entrada, double duracao,
        IEasingFunction easing, CancellationToken cancellationToken)
    {
        var desfoque = new BlurEffect { Radius = entrada ? 18 : 0, RenderingBias = RenderingBias.Performance };
        _animatedContent.Effect = desfoque;
        desfoque.BeginAnimation(BlurEffect.RadiusProperty,
            new DoubleAnimation(entrada ? 18 : 0, entrada ? 0 : 18, TimeSpan.FromMilliseconds(duracao)) { EasingFunction = easing });
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
    }

    private async Task AnimarCamadaDeCorAsync(string id, bool entrada, double duracao,
        IEasingFunction easing, CancellationToken cancellationToken)
    {
        _effectOverlay.Background = CriarCorDoEfeito(id);
        RenderOptions.SetBitmapScalingMode(_image,
            Contem(id, "pixel") ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        _effectOverlay.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(entrada ? .88 : 0, entrada ? 0 : .88, TimeSpan.FromMilliseconds(duracao)) { EasingFunction = easing });
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
    }

    private async Task AnimarFalhaDigitalAsync(string id, bool entrada, double duracao,
        CancellationToken cancellationToken)
    {
        _effectOverlay.Background = CriarCorDoEfeito(id);
        var quadrosX = new DoubleAnimationUsingKeyFrames();
        var quadrosY = new DoubleAnimationUsingKeyFrames();
        var handheld = Contem(id, "handheld_shake");
        var saltos = handheld ? new[] { 4d, -3d, 2d, -2d, 1d, 0d } : new[] { 12d, -10d, 7d, -4d, 2d, 0d };
        for (var i = 0; i < saltos.Length; i++)
        {
            var instante = TimeSpan.FromMilliseconds(duracao * (i + 1) / saltos.Length);
            quadrosX.KeyFrames.Add(new LinearDoubleKeyFrame(saltos[i], KeyTime.FromTimeSpan(instante)));
            quadrosY.KeyFrames.Add(new LinearDoubleKeyFrame(i % 2 == 0 ? (handheld ? -2 : -3) : (handheld ? 2 : 3), KeyTime.FromTimeSpan(instante)));
        }
        if (entrada) _animatedContent.Opacity = .45;
        _animatedContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(entrada ? .45 : 1, entrada ? 1 : .45, TimeSpan.FromMilliseconds(duracao)));
        _translation.BeginAnimation(TranslateTransform.XProperty, quadrosX);
        _translation.BeginAnimation(TranslateTransform.YProperty, quadrosY);
        _effectOverlay.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(entrada ? .72 : 0, entrada ? 0 : .72, TimeSpan.FromMilliseconds(duracao)));
        await Task.Delay(TimeSpan.FromMilliseconds(duracao), cancellationToken);
    }

    private static bool EhMascara(string id) => Contem(id, "wipe", "reveal", "iris", "curtain", "shutter", "blinds",
        "mask", "circle", "diamond", "star", "heart", "hexagon", "honeycomb", "checkerboard", "grid",
        "squares", "tiles", "stripes", "bars", "spiral", "radial", "sectors", "triangle", "polygon", "split",
        "mosaic", "letterbox", "stagger", "list_stagger", "shatter", "break_apart", "particle", "assemble", "wave", "ripple", "liquid", "gooey", "blob",
        "morph", "warp", "swirl", "vortex", "ink", "paint", "melt", "portal", "typewriter", "letter_by_letter",
        "word_by_word", "text_reveal");

    private static bool EhEfeitoDeLuz(string id) => Contem(id, "flash", "light", "burn", "fire", "color_", "gradient_", "blackout",
        "through_black", "through_white", "through_color", "glow", "bloom", "neon", "lens_flare", "spotlight",
        "shadow", "smoke", "fog", "frost", "ice", "electric", "aurora");

    private static Brush CriarCorDoEfeito(string id)
    {
        if (Contem(id, "handheld_shake")) return Brushes.Transparent;
        if (Contem(id, "blackout", "through_black", "shadow")) return Brushes.Black;
        if (Contem(id, "matrix_rain", "data_stream"))
            return new LinearGradientBrush(Color.FromRgb(12, 85, 38), Color.FromRgb(85, 255, 110), 90);
        if (Contem(id, "circuit", "hologram"))
            return new LinearGradientBrush(Color.FromRgb(15, 160, 220), Color.FromRgb(90, 255, 238), 25);
        if (Contem(id, "film_reel"))
            return new LinearGradientBrush(Color.FromRgb(255, 205, 105), Color.FromRgb(112, 46, 24), 90);
        if (Contem(id, "fire", "burn", "film_burn"))
            return new LinearGradientBrush(Color.FromRgb(255, 226, 120), Color.FromRgb(220, 45, 20), 35);
        if (Contem(id, "frost", "ice", "electric", "aurora", "rgb", "chromatic", "glitch", "vhs", "neon"))
            return new LinearGradientBrush(Color.FromRgb(55, 235, 255), Color.FromRgb(255, 40, 170), 25);
        if (Contem(id, "white", "flash", "exposure")) return Brushes.White;
        if (Contem(id, "smoke", "fog")) return new SolidColorBrush(Color.FromArgb(235, 190, 198, 210));
        if (Contem(id, "color", "gradient"))
            return new LinearGradientBrush(Color.FromRgb(141, 120, 255), Color.FromRgb(255, 92, 146), 45);
        return new LinearGradientBrush(Color.FromRgb(255, 246, 195), Color.FromRgb(255, 105, 58), 0);
    }

    private static EstadoVisual CriarEstadoOculto(string effect)
    {
        var id = effect.ToLowerInvariant();
        var (x, y) = Deslocamento(id);
        var fade = Contem(id, "fade", "dissolve", "flash", "light", "burn", "glow", "blur", "focus",
            "mask", "reveal", "wipe", "iris", "curtain", "shutter", "pixel", "glitch", "noise",
            "static", "digital", "scan", "match_cut", "morph", "liquid", "shape", "shatter", "particle", "smoke",
            "fog", "frost", "ice", "electric", "aurora", "checker", "grid", "tile", "stripe", "radial",
            "spiral", "luma", "grain", "letter", "text", "word", "typewriter");
        var scale = id switch
        {
            "zoom_in" or "zoom_out" or "zoom_through" => .22,
            "zoom_through_tunnel" => .08,
            "zoom_in_from_center" or "zoom_out_to_center" or "expand" or "mask_expansion" => .02,
            "shrink" or "mask_collapse" => .08,
            "scale_up" or "dolly_in" or "hero_transition" or "shared_element_transition" => .58,
            "scale_down" or "dolly_out" => .55,
            "pop_in" or "pop_out" => .12,
            "camera_pull" or "perspective_zoom" => .72,
            "crash_zoom" or "whip_zoom" => .1,
            "overshoot_zoom" or "pulse_transition" => .72,
            _ when Contem(id, "zoom", "scale", "pop", "expand", "shrink", "camera_pull", "dolly", "crash_zoom",
                "whip_zoom", "zoom_match", "morph", "portal", "pulse", "reveal", "collapse", "hero", "card_",
                "shared_element", "shared_axis", "expand_from_click", "match_move", "mask", "iris", "circle", "radial", "squeeze", "stretch") => .48,
            _ when Contem(id, "stack", "anticipation", "recoil") => .72,
            _ => 1,
        };
        var angle = Contem(id, "rotate", "spin", "flip", "page_", "cube", "barrel", "tilt", "fold",
            "hinge", "carousel", "orbit", "origami", "tile_flip", "swing", "twist")
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
        if (id == "push_left") return (46, 0);
        if (id == "push_right") return (-46, 0);
        if (id == "push_up") return (0, 32);
        if (id == "push_down") return (0, -32);
        if (id == "slide_over") return (46, 0);
        if (id == "slide_under") return (-46, 0);
        if (id == "parallax_slide") return (-64, 0);
        if (id == "diagonal_slide") return (-38, -27);
        if (Contem(id, "left", "whip_pan", "truck_left")) return (-46, 0);
        if (Contem(id, "right", "truck_right")) return (46, 0);
        if (Contem(id, "up", "top", "pedestal_up")) return (0, -32);
        if (Contem(id, "down", "bottom", "drop", "fall", "pedestal_down")) return (0, 32);
        if (Contem(id, "anticipation")) return (0, -12);
        if (Contem(id, "recoil")) return (0, 12);
        if (Contem(id, "stack")) return (24, 0);
        if (Contem(id, "slide", "push", "fly", "whip", "swipe", "wipe", "sweep", "reveal", "portal",
            "tracking", "momentum", "inertia", "throw", "shared_axis", "match_move")) return (42, 0);
        if (Contem(id, "bounce", "elastic", "spring", "rubber", "wobble", "jello")) return (0, 10);
        return (0, 0);
    }

    private static double Angulo(string id)
    {
        var graus = Contem(id, "spin", "barrel", "carousel") ? 250
            : Contem(id, "orbit", "dutch_tilt") ? 78
            : Contem(id, "twist", "swing") ? 70
            : Contem(id, "stack") ? 22
            : Contem(id, "flip", "page", "cube", "fold", "hinge", "tile") ? 100 : 40;
        return Contem(id, "left", "out") ? -graus : graus;
    }

    private static bool Contem(string id, params string[] termos) =>
        termos.Any(t => id.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static IEasingFunction CriarEasing(string efeito, bool entrada)
    {
        var modo = entrada ? EasingMode.EaseOut : EasingMode.EaseIn;
        if (Contem(efeito, "bounce"))
            return new BounceEase { EasingMode = modo, Bounces = 3, Bounciness = 2.2 };
        if (Contem(efeito, "elastic", "spring", "jello", "wobble"))
            return new ElasticEase
            {
                EasingMode = modo,
                Oscillations = Contem(efeito, "spring") ? 4 : 3,
                Springiness = Contem(efeito, "jello") ? 4 : 3,
            };
        if (Contem(efeito, "rubber_band", "rubber_stretch", "overshoot", "pop", "recoil"))
            return new BackEase { EasingMode = modo, Amplitude = Contem(efeito, "pop") ? 1.25 : .8 };
        if (Contem(efeito, "inertia", "momentum", "throw"))
            return new ExponentialEase { EasingMode = modo, Exponent = 4 };
        if (Contem(efeito, "anticipation"))
            return new BackEase { EasingMode = modo, Amplitude = 1.4 };
        return new CubicEase { EasingMode = entrada ? EasingMode.EaseOut : EasingMode.EaseIn };
    }

    private readonly record struct EstadoVisual(double Opacity, double Scale, double Angle, double X, double Y)
    {
        public static EstadoVisual Neutro { get; } = new(1, 1, 0, 0, 0);
    }
}
