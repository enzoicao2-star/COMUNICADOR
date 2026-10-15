using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Comunicador.Services;

namespace Comunicador.Controls;

/// <summary>Fundos vetoriais responsivos. Todos os modos, exceto "Sem fundo",
/// usam o relógio de renderização e acompanham o tamanho atual da janela.</summary>
public sealed class AnimatedBackground : FrameworkElement
{
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly List<Particle> _particles = new();
    private readonly List<Star> _stars = new();
    private readonly List<FlowParticle> _fluidParticles = new();
    private readonly Random _fluidRandom = new(731942);
    private TimeSpan _lastFrame;
    private double _lastFluidTime = -1;
    private WriteableBitmap? _particleWaveBitmap;
    private byte[]? _particleWavePixels;
    private Brush[]? _vortexBrushes;
    private string? _vortexBrushSignature;
    private Brush[]? _fluidBrushes;
    private Pen[]? _fluidPens;
    private string? _fluidBrushSignature;
    private Pen? _topographicPen;
    private Color _topographicPenColor;
    private double _topographicPenOpacity = -1;
    private bool _renderingSubscribed;
    private WavePoint[] _wavePoints = [];
    private int _waveColumns;
    private int _waveRows;
    private int _waveWidth;
    private int _waveHeight;
    private Point _pointerTarget;
    private Point _pointerSmoothed;
    private Point _pointerAtLastFrame;
    private bool _pointerActive;
    private double _lastWaveFrame = -1;
    private double _elapsedBeforePause;
    private double _activeSegmentStartedAt;
    private double? _nebulaClickTime;
    private Point _nebulaClickPosition;
    private readonly List<NebulaStar> _nebulaStars = new();
    private WriteableBitmap? _nebulaBitmap;
    private byte[]? _nebulaPixels;
    private int _nebulaBitmapWidth;
    private int _nebulaBitmapHeight;
    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(string), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata("Sem fundo", FrameworkPropertyMetadataOptions.AffectsRender, OnModeChanged));
    public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
        nameof(Intensity), typeof(double), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PauseAnimationProperty = DependencyProperty.Register(
        nameof(PauseAnimation), typeof(bool), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPauseAnimationChanged));
    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public double Intensity { get => (double)GetValue(IntensityProperty); set => SetValue(IntensityProperty, value); }
    public bool ReduceMotion { get => (bool)GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }
    public bool PauseAnimation { get => (bool)GetValue(PauseAnimationProperty); set => SetValue(PauseAnimationProperty, value); }
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    internal bool IsRenderingSubscribed => _renderingSubscribed;

    public void SetPointer(Point position)
    {
        if ((Mode != "Ondas interativas" && Mode != "Nebulosa halftone") || ReduceMotion) return;
        if (!_pointerActive)
            _pointerSmoothed = _pointerAtLastFrame = position;
        _pointerTarget = position;
        _pointerActive = true;
    }

    public void ClearPointer() => _pointerActive = false;

    public void DropNebulaSparkle(Point position)
    {
        if (Mode != "Nebulosa halftone" || ReduceMotion) return;
        _nebulaClickPosition = position;
        _nebulaClickTime = GetAnimationTime();
        InvalidateVisual();
    }

    public AnimatedBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += OnThemeChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        UpdateRenderingSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        SetRenderingSubscribed(false);
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateRenderingSubscription();

    private static void OnPauseAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AnimatedBackground background) return;
        var now = background._animationClock.Elapsed.TotalSeconds;
        if ((bool)e.NewValue)
        {
            background._elapsedBeforePause += Math.Max(0, now - background._activeSegmentStartedAt);
        }
        else
        {
            background._activeSegmentStartedAt = now;
        }
        background.UpdateRenderingSubscription();
    }

    private void UpdateRenderingSubscription() =>
        SetRenderingSubscribed(IsLoaded && IsVisible && !PauseAnimation && Mode != "Sem fundo");

    private void SetRenderingSubscribed(bool subscribe)
    {
        if (_renderingSubscribed == subscribe) return;
        _renderingSubscribed = subscribe;
        if (subscribe) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void OnThemeChanged() => InvalidateVisual();

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedBackground background && e.NewValue is string mode)
        {
            background.EnsureModeData(mode);
            background.UpdateRenderingSubscription();
        }
    }

    private void EnsureModeData(string mode)
    {
        if (mode == "Vórtice" && _particles.Count == 0) SeedVortex(2200, 3);
        else if (mode == "Constelação" && _stars.Count == 0) SeedStars(72);
        else if (mode == "Partículas fluidas" && _fluidParticles.Count == 0) SeedFluidParticles(1500);
        else if (mode == "Nebulosa halftone" && _nebulaStars.Count == 0) SeedNebulaStars(148);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (PauseAnimation || Mode == "Sem fundo") return;
        var rendering = e as RenderingEventArgs;
        // O fundo roda a 30 fps e deixa a composição da interface/navegação livre
        // para 60 fps. Visualmente continua contínuo, sem travar a troca de abas.
        var frameInterval = ReduceMotion ? 66 : 32;
        if (rendering is not null && rendering.RenderingTime - _lastFrame < TimeSpan.FromMilliseconds(frameInterval)) return;
        if (rendering is not null) _lastFrame = rendering.RenderingTime;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || Mode == "Sem fundo") return;
        var time = GetAnimationTime() * (ReduceMotion ? .22 : 1d);
        var opacity = .20 + Math.Clamp(Intensity / 100d, 0, 1) * .65;

        switch (Mode)
        {
            case "Topográfico": DrawTopographic(dc, time, opacity); break;
            case "Caminhos flutuantes": DrawFloatingPaths(dc, time, opacity); break;
            case "Vórtice": DrawVortex(dc, time, opacity); break;
            case "Ondas luminosas": DrawLuminousWaves(dc, time, opacity); break;
            case "Constelação": DrawConstellation(dc, time, opacity); break;
            case "Grade fluida": DrawFluidGrid(dc, time, opacity); break;
            case "Partículas fluidas": DrawFluidParticles(dc, time, opacity); break;
            case "Onda de partículas": DrawParticleWave(dc, time); break;
            case "Ondas interativas": DrawInteractiveWaves(dc, time, opacity); break;
            case "Nebulosa halftone": DrawHalftoneNebula(dc, time, opacity); break;
        }
    }

    private double GetAnimationTime()
    {
        var elapsed = PauseAnimation
            ? _elapsedBeforePause
            : _elapsedBeforePause + Math.Max(0, _animationClock.Elapsed.TotalSeconds - _activeSegmentStartedAt);
        return elapsed * Math.Clamp(Speed, 5, 100) / 100d;
    }

    /// <summary>
    /// Interpretação nativa em WPF do Halftone Nebula do 21st.dev. O campo de gás
    /// é calculado uma vez por célula de 6 px e enviado como bitmap reaproveitado;
    /// isso mantém o aspecto pontilhado sem criar milhares de visuais por quadro.
    /// </summary>
    private void DrawHalftoneNebula(DrawingContext dc, double time, double opacity)
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        EnsureNebulaBitmap(width, height);
        if (_nebulaBitmap is null || _nebulaPixels is null) return;

        Array.Clear(_nebulaPixels);
        const int cellSize = 6;
        var columns = (width + cellSize - 1) / cellSize;
        var rows = (height + cellSize - 1) / cellSize;
        var dark = IsDarkTheme();
        var background = ThemeColor("BackgroundBrush", Colors.Black);
        var accent = ThemeColor("AccentBrush", Colors.CornflowerBlue);
        var foreground = ThemeColor("TextPrimaryBrush", Colors.White);
        var pointer = _pointerActive && !ReduceMotion;
        if (pointer)
        {
            _pointerSmoothed.X += (_pointerTarget.X - _pointerSmoothed.X) * .14;
            _pointerSmoothed.Y += (_pointerTarget.Y - _pointerSmoothed.Y) * .14;
        }

        var parallaxX = pointer ? (_pointerSmoothed.X / Math.Max(1, ActualWidth) - .5) * 18 : 0;
        var parallaxY = pointer ? (_pointerSmoothed.Y / Math.Max(1, ActualHeight) - .5) * 12 : 0;
        var planetRadius = Math.Min(108, Math.Min(width, height) * .12);
        var planetCenterX = width * .74 + parallaxX * .35;
        var planetCenterY = height * .23 + parallaxY * .35;
        for (var row = 0; row < rows; row++)
        {
            var y0 = row * cellSize;
            var cellHeight = Math.Min(cellSize, height - y0);
            var pixelY = y0 + cellHeight * .5;
            var ny = pixelY / height;
            for (var column = 0; column < columns; column++)
            {
                var x0 = column * cellSize;
                var cellWidth = Math.Min(cellSize, width - x0);
                var pixelX = x0 + cellWidth * .5;
                var nx = pixelX / width;

                // Duas camadas de ruído, deformadas entre si, formam a faixa de gás.
                var warpX = PerlinNoise(nx * 2.8 + 7, ny * 2.4, time * .025) * .12;
                var warpY = PerlinNoise(nx * 2.5, ny * 2.8 + 19, time * .022) * .12;
                var gas = .64 * Noise01((nx + warpX) * 3.4, (ny + warpY) * 2.8, time * .018)
                    + .36 * Noise01((nx + warpX) * 6.3 + 5, (ny + warpY) * 5.1 - 3, time * .024);
                var bandDistance = Math.Abs(ny - .50 - (nx - .5) * .32);
                var band = 1 - SmoothStep(.035, .38, bandDistance);
                var density = Math.Clamp((gas * .53 + band * .55 - .43) * 1.9, 0, 1);

                if (pointer)
                {
                    var pointerDx = pixelX - _pointerSmoothed.X;
                    var pointerDy = pixelY - _pointerSmoothed.Y;
                    var lens = Math.Exp(-(pointerDx * pointerDx + pointerDy * pointerDy) / (2 * 158d * 158d));
                    density = Math.Clamp(density + lens * .36, 0, 1);
                }

                var planetX = (pixelX - planetCenterX) / Math.Max(1, planetRadius);
                var planetY = (pixelY - planetCenterY) / Math.Max(1, planetRadius);
                var planetDistanceSquared = planetX * planetX + planetY * planetY;
                var onPlanet = planetDistanceSquared < 1;
                var planetLight = onPlanet
                    ? Math.Clamp(.67 - planetX * .24 - planetY * .10, .18, 1)
                    : 0;
                if (onPlanet) density = Math.Max(density, .42 + planetLight * .48);

                var dither = (Bayer4[row & 3, column & 3] - 7.5) / 16d * .13;
                var level = (int)Math.Round(Math.Clamp(density + dither, 0, 1) * 6);
                var quantizedDensity = level / 6d;
                var dimColor = MixColor(background, accent, .24 + quantizedDensity * .43);
                var dotColor = MixColor(accent, foreground, .16 + quantizedDensity * .58);
                if (onPlanet)
                {
                    dimColor = MixColor(background, accent, .27 + planetLight * .38);
                    dotColor = MixColor(accent, foreground, .20 + planetLight * .66);
                }

                var baseAlpha = level <= 1 && !onPlanet
                    ? (byte)0
                    : (byte)Math.Clamp(Math.Round((dark ? 7 + level * 2.6 : 4 + level * 1.7) * opacity), 0, 255);
                var dotAlpha = (byte)Math.Clamp(Math.Round((dark ? 50 + level * 15 : 24 + level * 12) * opacity), 0, 255);
                var radius = cellSize * (.12 + quantizedDensity * .42);
                var radiusSquared = radius * radius;
                var centerCellX = cellWidth * .5;
                var centerCellY = cellHeight * .5;

                for (var localY = 0; localY < cellHeight; localY++)
                {
                    var dy = localY + .5 - centerCellY;
                    var rowOffset = ((y0 + localY) * width + x0) * 4;
                    for (var localX = 0; localX < cellWidth; localX++)
                    {
                        var dx = localX + .5 - centerCellX;
                        var isDot = dx * dx + dy * dy <= radiusSquared;
                        var color = isDot ? dotColor : dimColor;
                        var alpha = isDot ? dotAlpha : baseAlpha;
                        var index = rowOffset + localX * 4;
                        _nebulaPixels[index] = color.B;
                        _nebulaPixels[index + 1] = color.G;
                        _nebulaPixels[index + 2] = color.R;
                        _nebulaPixels[index + 3] = alpha;
                    }
                }
            }
        }

        _nebulaBitmap.WritePixels(new Int32Rect(0, 0, width, height), _nebulaPixels, width * 4, 0);
        dc.DrawImage(_nebulaBitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawNebulaStars(dc, time, opacity, parallaxX, parallaxY);
    }

    private void EnsureNebulaBitmap(int width, int height)
    {
        if (_nebulaBitmapWidth == width && _nebulaBitmapHeight == height
            && _nebulaBitmap is not null && _nebulaPixels?.Length == width * height * 4)
            return;

        _nebulaBitmapWidth = width;
        _nebulaBitmapHeight = height;
        _nebulaBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _nebulaPixels = new byte[width * height * 4];
    }

    private void DrawNebulaStars(DrawingContext dc, double time, double opacity, double parallaxX, double parallaxY)
    {
        var starBrush = ThemeBrush("TextPrimaryBrush", 1);
        var accentBrush = ThemeBrush("AccentBrush", 1);
        var starPen = new Pen(ThemeBrush("TextPrimaryBrush", 1), .75);
        starPen.Freeze();

        foreach (var star in _nebulaStars)
        {
            var twinkle = .34 + .66 * (1 + Math.Sin(time * (1.2 + star.Depth) + star.Phase)) * .5;
            var x = star.X * ActualWidth + parallaxX * star.Depth;
            var y = star.Y * ActualHeight + parallaxY * star.Depth;
            dc.PushOpacity(Math.Clamp(opacity * star.Depth * twinkle * .68, 0, 1));
            dc.DrawRectangle(star.Depth > .72 ? starBrush : accentBrush, null,
                new Rect(Math.Round(x), Math.Round(y), star.Size, star.Size));
            if (star.Depth > .88 && star.Size > 2.1)
            {
                dc.DrawLine(starPen, new Point(x - 3.5, y + star.Size * .5), new Point(x + 4.5, y + star.Size * .5));
                dc.DrawLine(starPen, new Point(x + star.Size * .5, y - 3.5), new Point(x + star.Size * .5, y + 4.5));
            }
            dc.Pop();
        }

        if (_nebulaClickTime is not double clickTime) return;
        var age = time - clickTime;
        if (age < 0 || age > 1.15)
        {
            if (age > 1.15) _nebulaClickTime = null;
            return;
        }

        var progress = age / 1.15;
        var ripplePen = new Pen(ThemeBrush("AccentBrush", (1 - progress) * opacity * .70), 1.25);
        ripplePen.Freeze();
        var rippleRadius = 7 + progress * 76;
        dc.DrawEllipse(null, ripplePen, _nebulaClickPosition, rippleRadius, rippleRadius);
        if (progress < .7)
        {
            var sparkleOpacity = (1 - progress / .7) * opacity;
            var sparklePen = new Pen(ThemeBrush("TextPrimaryBrush", sparkleOpacity), 1.1);
            sparklePen.Freeze();
            var sparkleSize = 5 + Math.Sin(progress * Math.PI) * 6;
            dc.DrawLine(sparklePen, new Point(_nebulaClickPosition.X - sparkleSize, _nebulaClickPosition.Y),
                new Point(_nebulaClickPosition.X + sparkleSize, _nebulaClickPosition.Y));
            dc.DrawLine(sparklePen, new Point(_nebulaClickPosition.X, _nebulaClickPosition.Y - sparkleSize),
                new Point(_nebulaClickPosition.X, _nebulaClickPosition.Y + sparkleSize));
        }
    }

    private static double Noise01(double x, double y, double z) => (PerlinNoise(x, y, z) + 1) * .5;

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Color MixColor(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(start.R + (end.R - start.R) * amount),
            (byte)Math.Round(start.G + (end.G - start.G) * amount),
            (byte)Math.Round(start.B + (end.B - start.B) * amount));
    }

    private void DrawInteractiveWaves(DrawingContext dc, double time, double opacity)
    {
        const int xGap = 12;
        const int yGap = 12;
        var width = (int)Math.Ceiling(ActualWidth);
        var height = (int)Math.Ceiling(ActualHeight);
        if (width != _waveWidth || height != _waveHeight)
        {
            _waveWidth = width;
            _waveHeight = height;
            _waveColumns = (int)Math.Ceiling((width + 120d) / xGap) + 1;
            _waveRows = (int)Math.Ceiling((height + 36d) / yGap) + 1;
            _wavePoints = new WavePoint[_waveColumns * _waveRows];
            for (var column = 0; column < _waveColumns; column++)
                for (var row = 0; row < _waveRows; row++)
                    _wavePoints[column * _waveRows + row] = new WavePoint();
            _lastWaveFrame = -1;
        }

        // O movimento do mouse chega pela janela, inclusive quando está sobre um botão.
        // O fundo continua fora dos testes de clique.
        var frameScale = _lastWaveFrame < 0 || PauseAnimation ? 0
            : Math.Clamp((time - _lastWaveFrame) * 30, .25, 2);
        _lastWaveFrame = time;
        var damping = Math.Pow(.95, frameScale);
        var mouseDx = 0d;
        var mouseDy = 0d;
        if (_pointerActive && !ReduceMotion && frameScale > 0)
        {
            _pointerSmoothed.X += (_pointerTarget.X - _pointerSmoothed.X) * .18;
            _pointerSmoothed.Y += (_pointerTarget.Y - _pointerSmoothed.Y) * .18;
            mouseDx = _pointerSmoothed.X - _pointerAtLastFrame.X;
            mouseDy = _pointerSmoothed.Y - _pointerAtLastFrame.Y;
            _pointerAtLastFrame = _pointerSmoothed;
        }

        var startX = (width - (_waveColumns - 1) * xGap) / 2d;
        var startY = (height - (_waveRows - 1) * yGap) / 2d;
        var color = ThemeColor("TextPrimaryBrush", Colors.White);
        var pen = new Pen(new SolidColorBrush(color)
            { Opacity = (IsDarkTheme() ? .19 : .13) + opacity * .13 }, .75);
        pen.Freeze();

        for (var column = 0; column < _waveColumns; column++)
        {
            var x = startX + column * xGap;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var row = 0; row < _waveRows; row++)
                {
                    var y = startY + row * yGap;
                    var point = _wavePoints[column * _waveRows + row];
                    var noise = PerlinNoise((x + time * 8) * .003,
                        (y + time * 3) * .002, .31) * 8;

                    if (frameScale > 0)
                    {
                        if (_pointerActive && !ReduceMotion)
                        {
                            var distanceX = x - _pointerSmoothed.X;
                            var distanceY = y - _pointerSmoothed.Y;
                            var distanceSquared = distanceX * distanceX + distanceY * distanceY;
                            if (distanceSquared < 175 * 175)
                            {
                                var influence = 1 - Math.Sqrt(distanceSquared) / 175;
                                point.VelocityX += mouseDx * influence * .11;
                                point.VelocityY += mouseDy * influence * .11;
                            }
                        }

                        point.VelocityX = (point.VelocityX - point.OffsetX * .01 * frameScale) * damping;
                        point.VelocityY = (point.VelocityY - point.OffsetY * .01 * frameScale) * damping;
                        point.OffsetX = Math.Clamp(point.OffsetX + point.VelocityX * frameScale, -50, 50);
                        point.OffsetY = Math.Clamp(point.OffsetY + point.VelocityY * frameScale, -50, 50);
                    }

                    var moved = new Point(x + Math.Cos(noise) * 12 + point.OffsetX,
                        y + Math.Sin(noise) * 6 + point.OffsetY);
                    if (row == 0) context.BeginFigure(moved, false, false);
                    else context.LineTo(moved, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private sealed class WavePoint
    {
        public double OffsetX;
        public double OffsetY;
        public double VelocityX;
        public double VelocityY;
    }

    /// <summary>
    /// Port direto do particle-wave do 21st.dev: grade 200 x 200 com espaçamento
    /// 0,3, câmera em (0, 6, 5), FOV de 75 graus e o mesmo deslocamento do shader.
    /// O bitmap evita criar 40 mil objetos WPF por quadro e preserva a animação fluida.
    /// </summary>
    private void DrawParticleWave(DrawingContext dc, double time)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        EnsureParticleWaveBitmap(pixelWidth, pixelHeight);
        if (_particleWaveBitmap is null || _particleWavePixels is null) return;

        var background = IsDarkTheme() ? (byte)0 : (byte)255;
        for (var index = 0; index < _particleWavePixels.Length; index += 4)
        {
            _particleWavePixels[index] = background;
            _particleWavePixels[index + 1] = background;
            _particleWavePixels[index + 2] = background;
            _particleWavePixels[index + 3] = 255;
        }

        // O shader original incrementa uTime em 0,05 por frame. Em 60 Hz são
        // aproximadamente três unidades por segundo.
        var shaderTime = time * 3d;
        const int amountX = 200;
        const int amountY = 200;
        const double gap = .3;
        const double cameraY = 6;
        const double cameraZ = 5;
        const double cameraDistance = 7.810249675906654;
        const double forwardY = -cameraY / cameraDistance;
        const double forwardZ = -cameraZ / cameraDistance;
        const double upY = cameraZ / cameraDistance;
        const double upZ = -cameraY / cameraDistance;
        var aspect = pixelWidth / (double)pixelHeight;
        var tanHalfFov = Math.Tan(75d * Math.PI / 360d);
        var particle = background == 0 ? (byte)255 : (byte)0;
        var cameraWaveOffset = Math.Cos(shaderTime) * .2;

        for (var ix = 0; ix < amountX; ix++)
        {
            var baseX = ix * gap - amountX * gap / 2d;
            // Estes valores só dependem da coluna e do tempo. Calculá-los para
            // cada uma das 200 linhas repetia seno e cosseno sem necessidade.
            var y = Math.Sin(baseX + shaderTime) * .5 + cameraWaveOffset;
            var x = baseX + Math.Sin(y + shaderTime) * .5;
            var scale = 1d + Math.Sin(x + shaderTime) * .5 + Math.Cos(y + shaderTime) * .2;
            var relativeY = y - cameraY;
            var depthFromY = relativeY * forwardY;
            var cameraUpFromY = relativeY * upY;

            for (var iy = 0; iy < amountY; iy++)
            {
                var z = iy * gap - amountX * gap / 2d;
                // Equivalente ao lookAt(scene.position) da câmera Three.js.
                var relativeZ = z - cameraZ;
                var depth = depthFromY + relativeZ * forwardZ;
                if (depth <= .01) continue;

                var cameraUp = cameraUpFromY + relativeZ * upZ;
                var normalizedX = (x / depth) / (tanHalfFov * aspect);
                var normalizedY = (cameraUp / depth) / tanHalfFov;
                if (normalizedX < -1.04 || normalizedX > 1.04 || normalizedY < -1.04 || normalizedY > 1.04)
                    continue;

                var screenX = (int)Math.Round((normalizedX + 1) * .5 * (pixelWidth - 1));
                var screenY = (int)Math.Round((1 - normalizedY) * .5 * (pixelHeight - 1));

                // Mesmo cálculo de scale e gl_PointSize usado no vertex shader.
                var pointSize = Math.Clamp(scale * 15d / depth, .45, 9);
                DrawParticleSquare(_particleWavePixels, pixelWidth, pixelHeight,
                    screenX, screenY, pointSize, particle);
            }
        }

        _particleWaveBitmap.WritePixels(
            new Int32Rect(0, 0, pixelWidth, pixelHeight),
            _particleWavePixels,
            pixelWidth * 4,
            0);
        dc.DrawImage(_particleWaveBitmap, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void EnsureParticleWaveBitmap(int width, int height)
    {
        if (_particleWaveBitmap?.PixelWidth == width && _particleWaveBitmap.PixelHeight == height &&
            _particleWavePixels?.Length == width * height * 4)
            return;

        _particleWaveBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _particleWavePixels = new byte[width * height * 4];
    }

    private static void DrawParticleSquare(
        byte[] pixels,
        int width,
        int height,
        int centerX,
        int centerY,
        double size,
        byte color)
    {
        var diameter = Math.Max(1, (int)Math.Ceiling(size));
        var startX = centerX - diameter / 2;
        var startY = centerY - diameter / 2;
        var endX = startX + diameter;
        var endY = startY + diameter;

        for (var y = Math.Max(0, startY); y < Math.Min(height, endY); y++)
        {
            for (var x = Math.Max(0, startX); x < Math.Min(width, endX); x++)
            {
                var offset = (y * width + x) * 4;
                // fragment shader: vec4(uColor, 0.5), sobre preto ou branco.
                pixels[offset] = (byte)((pixels[offset] + color) / 2);
                pixels[offset + 1] = (byte)((pixels[offset + 1] + color) / 2);
                pixels[offset + 2] = (byte)((pixels[offset + 2] + color) / 2);
            }
        }
    }

    private static bool IsDarkTheme()
    {
        var color = ThemeColor("BackgroundBrush", Colors.White);
        var luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        return luminance < 128;
    }

    private void DrawTopographic(DrawingContext dc, double time, double opacity)
    {
        var lineColor = ThemeColor("BackgroundLineBrush", Colors.White);
        var lineOpacity = Math.Clamp(opacity * .36, 0, 1);
        if (_topographicPen is null || _topographicPenColor != lineColor || _topographicPenOpacity != lineOpacity)
        {
            _topographicPenColor = lineColor;
            _topographicPenOpacity = lineOpacity;
            _topographicPen = new Pen(new SolidColorBrush(lineColor) { Opacity = lineOpacity }, .9);
            _topographicPen.Freeze();
        }
        var pen = _topographicPen;
        var spacing = Math.Max(30, ActualHeight / 20d);
        var travel = time * 26;
        for (var line = -3; line < ActualHeight / spacing + 4; line++)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                for (var x = -30d; x <= ActualWidth + 30; x += 16)
                {
                    var y = line * spacing
                        + Math.Sin((x + travel) * .008 + line * .52) * 24
                        + Math.Sin((x - travel * .6) * .021 - line * .31) * 10
                        + Math.Cos(x * .003 + line + time * .34) * 17;
                    if (first) { context.BeginFigure(new Point(x, y), false, false); first = false; }
                    else context.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawFloatingPaths(DrawingContext dc, double time, double opacity)
    {
        for (var i = 0; i < 36; i++)
        {
            var t = i / 35d;
            var direction = i % 2 == 0 ? 1d : -1d;
            var phase = time * (.32 + i * .003) * direction + i * .41;
            var y = (-.12 + t * 1.22) * ActualHeight;
            var sway = Math.Sin(phase) * (18 + t * 24);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(-ActualWidth * .12, y + sway), false, false);
                context.BezierTo(
                    new Point(ActualWidth * .20, y - ActualHeight * .24 + Math.Cos(phase * .8) * 36),
                    new Point(ActualWidth * .62, y + ActualHeight * .20 + Math.Sin(phase * .65) * 48),
                    new Point(ActualWidth * 1.12, y - Math.Cos(phase) * 30), true, false);
            }
            geometry.Freeze();

            var basePen = FrozenPen("BackgroundLineBrush", opacity * (.12 + i * .006), .55 + i * .025);
            dc.DrawGeometry(null, basePen, geometry);

            var movingBrush = ThemeBrush("AccentBrush", opacity * (.16 + i * .004));
            var dash = new DashStyle([1.2, 8.5], -time * (2.2 + i * .018) - i * .35);
            dash.Freeze();
            var movingPen = new Pen(movingBrush, .8 + i * .018) { DashStyle = dash, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            movingPen.Freeze();
            dc.DrawGeometry(null, movingPen, geometry);
        }
    }

    private void DrawVortex(DrawingContext dc, double time, double opacity)
    {
        var accent = ThemeColor("AccentBrush", Colors.CornflowerBlue);
        var muted = ThemeColor("TextSecondaryBrush", Colors.Gray);
        var signature = $"{accent}-{muted}-{opacity:F3}";
        if (_vortexBrushes is null || _vortexBrushSignature != signature)
        {
            _vortexBrushSignature = signature;
            _vortexBrushes = Enumerable.Range(0, 12).Select(i =>
            {
                var t = i / 11d;
                var color = Color.FromRgb(
                    (byte)(accent.R + (muted.R - accent.R) * t),
                    (byte)(accent.G + (muted.G - accent.G) * t),
                    (byte)(accent.B + (muted.B - accent.B) * t));
                var brush = new SolidColorBrush(color) { Opacity = opacity * (.9 - t * .45) };
                brush.Freeze();
                return brush;
            }).ToArray();
        }
        var brushes = _vortexBrushes;

        var rotation = time * .11;
        var pulse = 1 + Math.Sin(time * .46) * .045;
        var scaleX = ActualWidth / 8.1d * pulse;
        var scaleY = ActualHeight / 7.2d * pulse;
        var centerX = ActualWidth * (.5 + Math.Sin(time * .11) * .024);
        var centerY = ActualHeight * (.5 + Math.Cos(time * .09) * .028);
        foreach (var p in _particles)
        {
            var x = p.X * Math.Cos(rotation) - p.Z * Math.Sin(rotation);
            var z = p.X * Math.Sin(rotation) + p.Z * Math.Cos(rotation);
            var y = p.Y + z * .58;
            var perspective = 1d / Math.Max(.55, 1 + z * .035);
            var px = centerX + x * scaleX * perspective;
            var py = centerY + y * scaleY * perspective;
            var size = Math.Max(.65, 1.75 * perspective);
            dc.DrawEllipse(brushes[Math.Clamp((int)(p.Radius * 11), 0, 11)], null,
                new Point(px, py), size, size);
        }
    }

    private void DrawLuminousWaves(DrawingContext dc, double time, double opacity)
    {
        for (var wave = 0; wave < 10; wave++)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                for (var x = -20d; x <= ActualWidth + 20; x += 14)
                {
                    var phase = x / Math.Max(220, ActualWidth * .22) + time * (.42 + wave * .018) + wave * .57;
                    var y = ActualHeight * (.16 + wave * .078)
                        + Math.Sin(phase) * (28 + wave * 2)
                        + Math.Cos(phase * .48 - time * .22) * 18;
                    if (first) { context.BeginFigure(new Point(x, y), false, false); first = false; }
                    else context.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            var width = 1.1 + wave * .12;
            var pen = FrozenPen(wave % 2 == 0 ? "AccentBrush" : "BackgroundLineBrush",
                opacity * (.22 - wave * .009), width);
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawConstellation(DrawingContext dc, double time, double opacity)
    {
        var points = new Point[_stars.Count];
        for (var i = 0; i < _stars.Count; i++)
        {
            var star = _stars[i];
            points[i] = new Point(
                (star.X + Math.Sin(time * .10 + star.Phase) * .018) * ActualWidth,
                (star.Y + Math.Cos(time * .085 + star.Phase * 1.4) * .022) * ActualHeight);
        }

        var linePen = FrozenPen("BackgroundLineBrush", opacity * .13, .65);
        var maxDistance = Math.Clamp(Math.Min(ActualWidth, ActualHeight) * .16, 72, 145);
        for (var i = 0; i < points.Length; i++)
        {
            for (var j = i + 1; j < points.Length; j++)
            {
                var delta = points[i] - points[j];
                if (delta.Length <= maxDistance) dc.DrawLine(linePen, points[i], points[j]);
            }
        }

        var accent = ThemeBrush("AccentBrush", opacity * .72);
        var muted = ThemeBrush("BackgroundLineBrush", opacity * .55);
        for (var i = 0; i < points.Length; i++)
        {
            var pulse = 1.15 + .85 * (1 + Math.Sin(time * 1.4 + _stars[i].Phase)) / 2;
            dc.DrawEllipse(i % 4 == 0 ? accent : muted, null, points[i], pulse, pulse);
        }
    }

    private void DrawFluidGrid(DrawingContext dc, double time, double opacity)
    {
        var pen = FrozenPen("BackgroundLineBrush", opacity * .23, .72);
        const int columns = 18;
        const int rows = 12;
        for (var col = -1; col <= columns + 1; col++)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var row = -1; row <= rows + 1; row++)
                {
                    var y = row * ActualHeight / rows;
                    var x = col * ActualWidth / columns
                        + Math.Sin(row * .62 + time * .44 + col * .31) * 18
                        + Math.Cos(y * .008 - time * .28) * 9;
                    if (row == -1) context.BeginFigure(new Point(x, y), false, false);
                    else context.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        for (var row = -1; row <= rows + 1; row++)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var col = -1; col <= columns + 1; col++)
                {
                    var x = col * ActualWidth / columns;
                    var y = row * ActualHeight / rows
                        + Math.Cos(col * .53 - time * .38 + row * .29) * 15
                        + Math.Sin(x * .006 + time * .24) * 8;
                    if (col == -1) context.BeginFigure(new Point(x, y), false, false);
                    else context.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>
    /// Campo de partículas guiado pelo mesmo Perlin 3D do componente de referência.
    /// As linhas curtas preservam visualmente o rastro translúcido do canvas web sem
    /// acumular quadros antigos sobre os controles do WPF.
    /// </summary>
    private void DrawFluidParticles(DrawingContext dc, double time, double opacity)
    {
        var dt = _lastFluidTime < 0 || time <= _lastFluidTime
            ? 1d / 60
            : Math.Clamp(time - _lastFluidTime, 1d / 240, .08);
        if (ReduceMotion) dt *= .22;
        _lastFluidTime = time;

        var color = ThemeColor("TextPrimaryBrush", Colors.White);
        var signature = $"{color}-{opacity:F3}";
        if (_fluidBrushes is null || _fluidPens is null || _fluidBrushSignature != signature)
        {
            _fluidBrushSignature = signature;
            _fluidBrushes = new Brush[10];
            _fluidPens = new Pen[10];
            for (var i = 0; i < _fluidBrushes.Length; i++)
            {
                _fluidBrushes[i] = new SolidColorBrush(color)
                    { Opacity = opacity * .15 * (i + 1) / _fluidBrushes.Length };
                _fluidBrushes[i].Freeze();
                _fluidPens[i] = new Pen(_fluidBrushes[i], .72)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                _fluidPens[i].Freeze();
            }
        }
        var brushes = _fluidBrushes;
        var pens = _fluidPens;

        foreach (var particle in _fluidParticles)
        {
            particle.Life += dt * 60;
            if (particle.Life > particle.MaxLife)
            {
                ResetFluidParticle(particle);
            }

            var oldX = particle.X * ActualWidth;
            var oldY = particle.Y * ActualHeight;
            var noise = PerlinNoise(
                particle.X * ActualWidth * .003,
                particle.Y * ActualHeight * .003,
                time * .1);
            var angle = noise * Math.PI * 4;
            const double speed = 120; // 2 px por quadro no componente a 60 fps.
            particle.VelocityX = Math.Cos(angle) * speed;
            particle.VelocityY = Math.Sin(angle) * speed;
            if (ActualWidth > 0) particle.X += particle.VelocityX * dt / ActualWidth;
            if (ActualHeight > 0) particle.Y += particle.VelocityY * dt / ActualHeight;

            var wrapped = false;
            if (particle.X < 0) { particle.X += 1; wrapped = true; }
            else if (particle.X > 1) { particle.X -= 1; wrapped = true; }
            if (particle.Y < 0) { particle.Y += 1; wrapped = true; }
            else if (particle.Y > 1) { particle.Y -= 1; wrapped = true; }

            var progress = Math.Clamp(particle.Life / particle.MaxLife, 0, 1);
            var fade = Math.Sin(progress * Math.PI);
            var brushIndex = Math.Clamp((int)Math.Round(fade * (brushes.Length - 1)), 0, brushes.Length - 1);
            var x = particle.X * ActualWidth;
            var y = particle.Y * ActualHeight;
            if (!wrapped && dt > 0) dc.DrawLine(pens[brushIndex], new Point(oldX, oldY), new Point(x, y));
            dc.DrawEllipse(brushes[brushIndex], null, new Point(x, y), particle.Size, particle.Size);
        }
    }

    private void SeedVortex(int count, int arms)
    {
        var random = new Random(424242);
        for (var i = 0; i < count; i++)
        {
            var radius = Math.Pow(random.NextDouble(), .6) * 6;
            var branch = (i % arms) / (double)arms * Math.PI * 2;
            var angle = branch + radius * .5;
            var scatter = (random.NextDouble() - .5) * (.6 + radius * .08);
            _particles.Add(new(
                Math.Cos(angle) * radius + scatter,
                (random.NextDouble() - .5) * .6,
                Math.Sin(angle) * radius + scatter,
                radius / 6));
        }
    }

    private void SeedStars(int count)
    {
        var random = new Random(918273);
        for (var i = 0; i < count; i++)
            _stars.Add(new(random.NextDouble(), random.NextDouble(), random.NextDouble() * Math.PI * 2));
    }

    private void SeedNebulaStars(int count)
    {
        var random = new Random(214067);
        for (var i = 0; i < count; i++)
            _nebulaStars.Add(new(random.NextDouble(), random.NextDouble(), .24 + random.NextDouble() * .76,
                random.NextDouble() * Math.PI * 2, .8 + random.NextDouble() * 2.3));
    }

    private void SeedFluidParticles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var particle = new FlowParticle();
            ResetFluidParticle(particle, randomLife: true);
            _fluidParticles.Add(particle);
        }
    }

    private void ResetFluidParticle(FlowParticle particle, bool randomLife = false)
    {
        particle.X = _fluidRandom.NextDouble();
        particle.Y = _fluidRandom.NextDouble();
        particle.Size = .5 + _fluidRandom.NextDouble() * 1.5;
        particle.MaxLife = 100 + _fluidRandom.NextDouble() * 50;
        particle.Life = randomLife ? _fluidRandom.NextDouble() * particle.MaxLife : 0;
        particle.VelocityX = particle.VelocityY = 0;
    }

    private static readonly int[] NoisePermutation =
    [
        151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,
        37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,
        57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,
        166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,
        102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,
        116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,
        126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,
        152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,
        113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,
        241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,
        121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,
        215,61,156,180,
    ];

    private static readonly int[] NoiseTable = BuildNoiseTable();

    private static int[] BuildNoiseTable()
    {
        var table = new int[512];
        for (var i = 0; i < 256; i++) table[i] = table[256 + i] = NoisePermutation[i];
        return table;
    }

    private static double PerlinNoise(double x, double y, double z)
    {
        var floorX = Math.Floor(x);
        var floorY = Math.Floor(y);
        var floorZ = Math.Floor(z);
        var ix = (int)floorX & 255;
        var iy = (int)floorY & 255;
        var iz = (int)floorZ & 255;
        x -= floorX;
        y -= floorY;
        z -= floorZ;

        var u = Fade(x);
        var v = Fade(y);
        var w = Fade(z);
        var a = NoiseTable[ix] + iy;
        var aa = NoiseTable[a] + iz;
        var ab = NoiseTable[a + 1] + iz;
        var b = NoiseTable[ix + 1] + iy;
        var ba = NoiseTable[b] + iz;
        var bb = NoiseTable[b + 1] + iz;

        return Lerp(w,
            Lerp(v,
                Lerp(u, Grad(NoiseTable[aa], x, y, z), Grad(NoiseTable[ba], x - 1, y, z)),
                Lerp(u, Grad(NoiseTable[ab], x, y - 1, z), Grad(NoiseTable[bb], x - 1, y - 1, z))),
            Lerp(v,
                Lerp(u, Grad(NoiseTable[aa + 1], x, y, z - 1), Grad(NoiseTable[ba + 1], x - 1, y, z - 1)),
                Lerp(u, Grad(NoiseTable[ab + 1], x, y - 1, z - 1), Grad(NoiseTable[bb + 1], x - 1, y - 1, z - 1))));
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double t, double a, double b) => a + t * (b - a);
    private static double Grad(int hash, double x, double y, double z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static Pen FrozenPen(string key, double opacity, double width)
    {
        var pen = new Pen(ThemeBrush(key, opacity), width);
        pen.Freeze();
        return pen;
    }

    private static Brush ThemeBrush(string key, double opacity)
    {
        var brush = new SolidColorBrush(ThemeColor(key, Colors.White)) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static Color ThemeColor(string key, Color fallback) =>
        (Application.Current.TryFindResource(key) as SolidColorBrush)?.Color ?? fallback;

    private readonly record struct Particle(double X, double Y, double Z, double Radius);
    private readonly record struct Star(double X, double Y, double Phase);
    private readonly record struct NebulaStar(double X, double Y, double Depth, double Phase, double Size);
    private sealed class FlowParticle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; set; }
        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public double Life { get; set; }
        public double MaxLife { get; set; }
    }
}
