using System.Windows;
using System.Windows.Media;
using Comunicador.Services;

namespace Comunicador.Controls;

/// <summary>Fundos vetoriais responsivos. Todos os modos, exceto "Sem fundo",
/// usam o relógio de renderização e acompanham o tamanho atual da janela.</summary>
public sealed class AnimatedBackground : FrameworkElement
{
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly List<Particle> _particles = new();
    private readonly List<Star> _stars = new();
    private readonly List<FlowParticle> _fluidParticles = new();
    private readonly Random _fluidRandom = new(731942);
    private TimeSpan _lastFrame;
    private double _lastFluidTime = -1;

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(string), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata("Sem fundo", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
        nameof(Intensity), typeof(double), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(AnimatedBackground),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public double Intensity { get => (double)GetValue(IntensityProperty); set => SetValue(IntensityProperty, value); }
    public bool ReduceMotion { get => (bool)GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }

    public AnimatedBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        SeedVortex(2800, 3);
        SeedStars(92);
        SeedFluidParticles(2000);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRendering;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    private void OnRendering(object? sender, EventArgs e)
    {
        if (ReduceMotion || Mode == "Sem fundo") return;
        var rendering = e as RenderingEventArgs;
        // O fundo roda a 30 fps e deixa a composição da interface/navegação livre
        // para 60 fps. Visualmente continua contínuo, sem travar a troca de abas.
        if (rendering is not null && rendering.RenderingTime - _lastFrame < TimeSpan.FromMilliseconds(32)) return;
        if (rendering is not null) _lastFrame = rendering.RenderingTime;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || Mode == "Sem fundo") return;
        var time = ReduceMotion ? 0d : (DateTime.UtcNow - _started).TotalSeconds;
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
        }
    }

    private void DrawTopographic(DrawingContext dc, double time, double opacity)
    {
        var pen = FrozenPen("BackgroundLineBrush", opacity * .36, .9);
        var spacing = Math.Max(30, ActualHeight / 20d);
        for (var line = -3; line < ActualHeight / spacing + 4; line++)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                for (var x = -30d; x <= ActualWidth + 30; x += 16)
                {
                    var travel = time * 26;
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
        var brushes = Enumerable.Range(0, 12).Select(i =>
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
        if (ReduceMotion) dt = 0;
        _lastFluidTime = time;

        var color = ThemeColor("TextPrimaryBrush", Colors.White);
        var brushes = new Brush[10];
        var pens = new Pen[10];
        for (var i = 0; i < brushes.Length; i++)
        {
            // O original atinge 0,15 de alfa e cruza suavemente por dez níveis.
            brushes[i] = new SolidColorBrush(color) { Opacity = opacity * .15 * (i + 1) / brushes.Length };
            brushes[i].Freeze();
            pens[i] = new Pen(brushes[i], .72) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pens[i].Freeze();
        }

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
