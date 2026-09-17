using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Comunicador.Controls;

/// <summary>Badge inspirada no AwardBadge do 21st.dev. Renderiza vários estilos,
/// brilho holográfico e inclinação guiada pelo ponteiro sem exigir clique.</summary>
public sealed class InteractiveBadge : FrameworkElement
{
    private double _pointerX = .5;
    private double _pointerY = .5;
    private bool _hovered;
    private BitmapSource? _customIcon;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata("BADGE", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata("#4C8DFF", FrameworkPropertyMetadataOptions.AffectsRender, OnColorChanged));
    public static readonly DependencyProperty BadgeStyleProperty = DependencyProperty.Register(
        nameof(BadgeStyle), typeof(string), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata("Holográfica", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata("Estrela", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CustomIconBase64Property = DependencyProperty.Register(
        nameof(CustomIconBase64), typeof(string), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCustomIconChanged));
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow), typeof(bool), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnGlowChanged));
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(InteractiveBadge),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public string BadgeStyle { get => (string)GetValue(BadgeStyleProperty); set => SetValue(BadgeStyleProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? CustomIconBase64 { get => (string?)GetValue(CustomIconBase64Property); set => SetValue(CustomIconBase64Property, value); }
    public bool Glow { get => (bool)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    public InteractiveBadge()
    {
        Cursor = Cursors.Arrow;
        MouseEnter += (_, _) => { _hovered = true; InvalidateVisual(); };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            _pointerX = _pointerY = .5;
            InvalidateVisual();
        };
        MouseMove += (_, e) =>
        {
            if (!Interactive || ActualWidth <= 0 || ActualHeight <= 0) return;
            var point = e.GetPosition(this);
            _pointerX = Math.Clamp(point.X / ActualWidth, 0, 1);
            _pointerY = Math.Clamp(point.Y / ActualHeight, 0, 1);
            InvalidateVisual();
        };
        Loaded += (_, _) => ApplyGlow();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var textWidth = Math.Clamp((Text?.Length ?? 5) * 7.2 + 48, 92, 230);
        return new Size(textWidth, BadgeStyle == "Selo" ? 40 : 34);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var color = ParseColor(Color, Colors.CornflowerBlue);
        var rect = new Rect(1, 1, ActualWidth - 2, ActualHeight - 2);
        var radius = BadgeStyle switch
        {
            "Pílula" => ActualHeight / 2,
            "Selo" => 8,
            "Contorno" => 7,
            _ => 9,
        };

        var transform = Transform.Identity;
        if (_hovered && Interactive)
        {
            var angle = (_pointerX - .5) * 4.2;
            var skew = (.5 - _pointerY) * 3.2;
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(.985, .985, ActualWidth / 2, ActualHeight / 2));
            group.Children.Add(new SkewTransform(skew, -angle * .25, ActualWidth / 2, ActualHeight / 2));
            group.Children.Add(new RotateTransform(angle, ActualWidth / 2, ActualHeight / 2));
            transform = group;
        }
        dc.PushTransform(transform);

        var fill = CreateFill(color);
        var border = new Pen(new SolidColorBrush(Adjust(color, BadgeStyle == "Contorno" ? .25 : -.28)),
            BadgeStyle == "Contorno" ? 1.6 : 1);
        border.Freeze();
        dc.DrawRoundedRectangle(fill, border, rect, radius, radius);

        if (BadgeStyle == "Holográfica") DrawHolographicOverlay(dc, rect, radius);
        else if (BadgeStyle == "Metal") DrawMetalLines(dc, rect);
        else if (BadgeStyle == "Selo") DrawSealCorners(dc, rect, color);

        DrawIcon(dc, new Rect(10, (ActualHeight - 19) / 2, 19, 19), color);
        var foreground = Luminance(color) > 155 && BadgeStyle != "Contorno" ? Colors.Black : Colors.White;
        if (BadgeStyle == "Contorno") foreground = color;
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal,
            FontWeights.SemiBold, FontStretches.Normal);
        var formatted = new FormattedText(
            string.IsNullOrWhiteSpace(Text) ? "BADGE" : Text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            11.5,
            new SolidColorBrush(foreground),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, ActualWidth - 42),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(formatted, new Point(35, (ActualHeight - formatted.Height) / 2));
        dc.Pop();
    }

    private Brush CreateFill(Color color)
    {
        if (BadgeStyle == "Contorno")
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(26, color.R, color.G, color.B));
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Adjust(color, .28), 0),
                new GradientStop(color, .52),
                new GradientStop(Adjust(color, -.24), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private void DrawHolographicOverlay(DrawingContext dc, Rect rect, double radius)
    {
        var center = _hovered && Interactive ? _pointerX : .28;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(Math.Clamp(center - .42, 0, 1), 0),
            EndPoint = new Point(Math.Clamp(center + .42, 0, 1), 1),
            GradientStops =
            {
                new GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 70, 90), 0),
                new GradientStop(System.Windows.Media.Color.FromArgb(95, 255, 70, 90), .18),
                new GradientStop(System.Windows.Media.Color.FromArgb(100, 255, 220, 60), .35),
                new GradientStop(System.Windows.Media.Color.FromArgb(95, 70, 255, 180), .52),
                new GradientStop(System.Windows.Media.Color.FromArgb(105, 80, 130, 255), .7),
                new GradientStop(System.Windows.Media.Color.FromArgb(0, 190, 80, 255), 1),
            },
        };
        dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
    }

    private static void DrawMetalLines(DrawingContext dc, Rect rect)
    {
        var pen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(55, 255, 255, 255)), .7);
        pen.Freeze();
        for (var x = rect.Left + 5; x < rect.Right; x += 7)
            dc.DrawLine(pen, new Point(x, rect.Top + 2), new Point(x - 10, rect.Bottom - 2));
    }

    private static void DrawSealCorners(DrawingContext dc, Rect rect, Color color)
    {
        var brush = new SolidColorBrush(Adjust(color, -.34));
        brush.Freeze();
        static StreamGeometry Polygon(params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze();
            return geometry;
        }
        dc.DrawGeometry(brush, null, Polygon(
            new(rect.Left, rect.Top + 8), new(rect.Left + 8, rect.Top),
            new(rect.Left + 8, rect.Bottom), new(rect.Left, rect.Bottom - 8)));
        dc.DrawGeometry(brush, null, Polygon(
            new(rect.Right, rect.Top + 8), new(rect.Right - 8, rect.Top),
            new(rect.Right - 8, rect.Bottom), new(rect.Right, rect.Bottom - 8)));
    }

    private void DrawIcon(DrawingContext dc, Rect target, Color color)
    {
        if (_customIcon is not null)
        {
            dc.DrawImage(_customIcon, target);
            return;
        }
        var glyph = IconGlyph(Icon);
        var formatted = new FormattedText(glyph, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            15, new SolidColorBrush(Luminance(color) > 155 && BadgeStyle != "Contorno" ? Colors.Black : Colors.White),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(target.Left + (target.Width - formatted.Width) / 2,
            target.Top + (target.Height - formatted.Height) / 2));
    }

    private static string IconGlyph(string? name) => name switch
    {
        "Coroa" => "\uE7BF", "Estrela" => "\uE734", "Escudo" => "\uEA18",
        "Raio" => "\uE945", "Diamante" => "\uE735", "Fogo" => "\uE9CA",
        "Coração" => "\uEB52", "Usuário" => "\uE77B", "Código" => "\uE943",
        "Música" => "\uE8D6", "Jogo" => "\uE7FC", "Casa" => "\uE80F",
        "Medalha" => "\uE7E7", "Chave" => "\uE8D7", "Globo" => "\uE774",
        _ => "\uE734",
    };

    private static void OnCustomIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InteractiveBadge badge) return;
        badge._customIcon = null;
        if (e.NewValue is string base64 && !string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 64;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                badge._customIcon = bitmap;
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException)
            {
                badge._customIcon = null;
            }
        }
        badge.InvalidateVisual();
    }

    private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractiveBadge badge) badge.ApplyGlow();
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractiveBadge badge) badge.ApplyGlow();
    }

    private void ApplyGlow()
    {
        if (!Glow) { Effect = null; return; }
        var color = ParseColor(Color, Colors.CornflowerBlue);
        Effect = new DropShadowEffect { Color = color, BlurRadius = 11, ShadowDepth = 0, Opacity = .62 };
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value ?? string.Empty); }
        catch (FormatException) { return fallback; }
    }

    private static Color Adjust(Color color, double amount)
    {
        byte C(byte value) => (byte)Math.Clamp(value + 255 * amount, 0, 255);
        return System.Windows.Media.Color.FromRgb(C(color.R), C(color.G), C(color.B));
    }

    private static double Luminance(Color c) => .2126 * c.R + .7152 * c.G + .0722 * c.B;
}
