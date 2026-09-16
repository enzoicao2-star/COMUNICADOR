using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Comunicador.Services;

namespace Comunicador.Controls;

/// <summary>
/// Seletor de cor nativo inspirado no componente do 21st.dev. Trabalha em HSV
/// internamente e oferece edição equivalente em HEX, HSL e RGB.
/// </summary>
public partial class ColorPickerPopover : UserControl
{
    private double _hue = 216;
    private double _saturation = .70;
    private double _value = 1;
    private bool _ready;
    private bool _updatingFields;
    private bool _updatingProperty;
    private bool _draggingSaturation;
    private bool _draggingHue;
    private Ellipse? _triggerSwatch;
    private TextBlock? _hexDisplay;

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorPickerPopover),
        new FrameworkPropertyMetadata("#4C8DFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public ColorPickerPopover() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ready = true;
        TriggerButton.ApplyTemplate();
        _triggerSwatch = TriggerButton.Template.FindName("TriggerSwatch", TriggerButton) as Ellipse;
        _hexDisplay = TriggerButton.Template.FindName("HexDisplay", TriggerButton) as TextBlock;
        if (TryParseColor(SelectedColor, out var color)) SetFromRgb(color, updateProperty: false);
        else UpdateUi();
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPickerPopover)d;
        if (picker._updatingProperty || !picker._ready || e.NewValue is not string value) return;
        if (TryParseColor(value, out var color)) picker.SetFromRgb(color, updateProperty: false);
    }

    private void OnTriggerClick(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        UpdateUi();
        PopupCard.Opacity = 0;
        PopupScale.ScaleX = PopupScale.ScaleY = .96;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PopupCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = ease,
        });
        PopupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = ease,
        });
        PopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = ease,
        });
    }

    private void OnSaturationMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSaturation = true;
        SaturationSurface.CaptureMouse();
        UpdateSaturation(e.GetPosition(SaturationSurface));
        e.Handled = true;
    }

    private void OnSaturationMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingSaturation) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndSaturationDrag();
            return;
        }
        UpdateSaturation(e.GetPosition(SaturationSurface));
    }

    private void OnSaturationMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingSaturation) UpdateSaturation(e.GetPosition(SaturationSurface));
        EndSaturationDrag();
        e.Handled = true;
    }

    private void EndSaturationDrag()
    {
        _draggingSaturation = false;
        if (SaturationSurface.IsMouseCaptured) SaturationSurface.ReleaseMouseCapture();
    }

    private void UpdateSaturation(Point point)
    {
        if (SaturationSurface.ActualWidth <= 0 || SaturationSurface.ActualHeight <= 0) return;
        _saturation = Math.Clamp(point.X / SaturationSurface.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / SaturationSurface.ActualHeight, 0, 1);
        CommitColor();
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueSurface.CaptureMouse();
        UpdateHue(e.GetPosition(HueSurface));
        e.Handled = true;
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingHue) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndHueDrag();
            return;
        }
        UpdateHue(e.GetPosition(HueSurface));
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingHue) UpdateHue(e.GetPosition(HueSurface));
        EndHueDrag();
        e.Handled = true;
    }

    private void EndHueDrag()
    {
        _draggingHue = false;
        if (HueSurface.IsMouseCaptured) HueSurface.ReleaseMouseCapture();
    }

    private void UpdateHue(Point point)
    {
        if (HueSurface.ActualWidth <= 0) return;
        _hue = Math.Clamp(point.X / HueSurface.ActualWidth, 0, 1) * 360;
        if (_hue >= 360) _hue = 0;
        CommitColor();
    }

    private void OnPickerSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => PositionPointers();

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HexPanel is null || ModeSelector.SelectedItem is not ComboBoxItem item) return;
        var mode = item.Tag as string ?? "HEX";
        HexPanel.Visibility = mode == "HEX" ? Visibility.Visible : Visibility.Collapsed;
        HslPanel.Visibility = mode == "HSL" ? Visibility.Visible : Visibility.Collapsed;
        RgbPanel.Visibility = mode == "RGB" ? Visibility.Visible : Visibility.Collapsed;
        if (_ready) UpdateInputFields();
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || !_ready || !TryParseColor(HexInput.Text, out var color)) return;
        SetFromRgb(color, updateProperty: true);
    }

    private void OnHslTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || !_ready
            || !TryDouble(HueInput.Text, out var h)
            || !TryDouble(SaturationInput.Text, out var s)
            || !TryDouble(LightnessInput.Text, out var l)) return;

        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s / 100, 0, 1);
        l = Math.Clamp(l / 100, 0, 1);
        var v = l + s * Math.Min(l, 1 - l);
        _hue = h;
        _value = v;
        _saturation = v <= 0 ? 0 : 2 * (1 - l / v);
        CommitColor();
    }

    private void OnRgbTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || !_ready
            || !int.TryParse(RedInput.Text, out var r)
            || !int.TryParse(GreenInput.Text, out var g)
            || !int.TryParse(BlueInput.Text, out var b)) return;
        SetFromRgb(Color.FromRgb((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255)), updateProperty: true);
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && TryParseColor(hex, out var color))
            SetFromRgb(color, updateProperty: true);
    }

    private void CommitColor()
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        var hex = ToHex(color);
        _updatingProperty = true;
        SetCurrentValue(SelectedColorProperty, hex);
        _updatingProperty = false;
        GetBindingExpression(SelectedColorProperty)?.UpdateSource();
        UpdateUi();
    }

    private void SetFromRgb(Color color, bool updateProperty)
    {
        RgbToHsv(color, out _hue, out _saturation, out _value);
        if (updateProperty)
        {
            _updatingProperty = true;
            SetCurrentValue(SelectedColorProperty, ToHex(color));
            _updatingProperty = false;
            GetBindingExpression(SelectedColorProperty)?.UpdateSource();
        }
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (!_ready) return;
        var color = HsvToRgb(_hue, _saturation, _value);
        var brush = new SolidColorBrush(color);
        if (_triggerSwatch is not null) _triggerSwatch.Fill = brush;
        ContrastSwatch.Background = brush;
        if (_hexDisplay is not null) _hexDisplay.Text = ToHex(color);
        SaturationHueLayer.Background = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
        PositionPointers();
        UpdateInputFields();
        UpdateContrast(color);
    }

    private void PositionPointers()
    {
        if (!_ready) return;
        var saturationWidth = SaturationSurface.ActualWidth;
        var saturationHeight = SaturationSurface.ActualHeight;
        if (saturationWidth > 0 && saturationHeight > 0)
        {
            Canvas.SetLeft(SaturationPointer, _saturation * saturationWidth - SaturationPointer.Width / 2);
            Canvas.SetTop(SaturationPointer, (1 - _value) * saturationHeight - SaturationPointer.Height / 2);
        }

        var hueWidth = HueSurface.ActualWidth;
        if (hueWidth > 0)
        {
            Canvas.SetLeft(HuePointer, (_hue / 360) * hueWidth - HuePointer.Width / 2);
            Canvas.SetTop(HuePointer, 0);
        }
    }

    private void UpdateInputFields()
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        var lightness = _value * (1 - _saturation / 2);
        var hslSaturation = lightness is <= 0 or >= 1 ? 0 : (_value - lightness) / Math.Min(lightness, 1 - lightness);

        _updatingFields = true;
        HexInput.Text = ToHex(color);
        HueInput.Text = Math.Round(_hue).ToString(CultureInfo.InvariantCulture);
        SaturationInput.Text = Math.Round(hslSaturation * 100).ToString(CultureInfo.InvariantCulture);
        LightnessInput.Text = Math.Round(lightness * 100).ToString(CultureInfo.InvariantCulture);
        RedInput.Text = color.R.ToString(CultureInfo.InvariantCulture);
        GreenInput.Text = color.G.ToString(CultureInfo.InvariantCulture);
        BlueInput.Text = color.B.ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;
    }

    private void UpdateContrast(Color color)
    {
        static double Linear(byte component)
        {
            var channel = component / 255d;
            return channel <= .03928 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }

        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var blackTextRatio = (luminance + .05) / .05;
        var whiteTextRatio = 1.05 / (luminance + .05);
        LightRatioText.Text = blackTextRatio.ToString("0.00", CultureInfo.InvariantCulture);
        DarkRatioText.Text = whiteTextRatio.ToString("0.00", CultureInfo.InvariantCulture);
        ContrastLetter.Foreground = blackTextRatio >= whiteTextRatio ? Brushes.Black : Brushes.White;

        SetBadge(LightAaBadge, LightAaText, blackTextRatio >= 4.5, "AA");
        SetBadge(LightAaaBadge, LightAaaText, blackTextRatio >= 7, "AAA");
        SetBadge(DarkAaBadge, DarkAaText, whiteTextRatio >= 4.5, "AA");
        SetBadge(DarkAaaBadge, DarkAaaText, whiteTextRatio >= 7, "AAA");
    }

    private static void SetBadge(Border badge, TextBlock text, bool passed, string label)
    {
        var color = passed ? Color.FromRgb(39, 176, 125) : Color.FromRgb(227, 71, 71);
        badge.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
        badge.BorderBrush = new SolidColorBrush(Color.FromArgb(92, color.R, color.G, color.B));
        text.Foreground = new SolidColorBrush(color);
        text.Text = $"{(passed ? "✓" : "×")} {label}";
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (!ThemeService.TryNormalizeColor(value, out var normalized)) return false;
        color = (Color)ColorConverter.ConvertFromString(normalized);
        return true;
    }

    private static bool TryDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }
}
