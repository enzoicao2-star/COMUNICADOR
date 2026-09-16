using System.Windows;
using System.Windows.Media;
using Comunicador.Services;

namespace Comunicador.Controls;

/// <summary>
/// Desenha a barra superior com a elevação curva do item ativo. A curva se move
/// entre as abas, reproduzindo em WPF o comportamento do componente de referência.
/// </summary>
public sealed class AnimatedNavBackground : FrameworkElement
{
    private double _displayIndex;
    private double _startIndex;
    private double _targetIndex;
    private DateTime _animationStarted;

    public static readonly DependencyProperty ActiveIndexProperty = DependencyProperty.Register(
        nameof(ActiveIndex), typeof(int), typeof(AnimatedNavBackground),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnActiveIndexChanged));

    public static readonly DependencyProperty ItemCountProperty = DependencyProperty.Register(
        nameof(ItemCount), typeof(int), typeof(AnimatedNavBackground),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(AnimatedNavBackground),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public int ActiveIndex { get => (int)GetValue(ActiveIndexProperty); set => SetValue(ActiveIndexProperty, value); }
    public int ItemCount { get => (int)GetValue(ItemCountProperty); set => SetValue(ItemCountProperty, value); }
    public bool ReduceMotion { get => (bool)GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }

    public AnimatedNavBackground()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _displayIndex = _targetIndex = Math.Max(0, ActiveIndex);
        CompositionTarget.Rendering += OnRendering;
        ThemeService.ThemeChanged += OnThemeChanged;
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private static void OnActiveIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AnimatedNavBackground control) return;
        control._startIndex = control._displayIndex;
        control._targetIndex = Math.Max(0, (int)e.NewValue);
        control._animationStarted = DateTime.UtcNow;
        if (!control.IsLoaded || control.ReduceMotion)
        {
            control._displayIndex = control._targetIndex;
        }
        control.InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (ReduceMotion)
        {
            if (_displayIndex != _targetIndex)
            {
                _displayIndex = _targetIndex;
                InvalidateVisual();
            }
            return;
        }

        var elapsed = (DateTime.UtcNow - _animationStarted).TotalMilliseconds;
        if (elapsed >= 340 || Math.Abs(_displayIndex - _targetIndex) < .001)
        {
            if (_displayIndex != _targetIndex)
            {
                _displayIndex = _targetIndex;
                InvalidateVisual();
            }
            return;
        }

        var progress = Math.Clamp(elapsed / 340d, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _displayIndex = _startIndex + (_targetIndex - _startIndex) * eased;
        InvalidateVisual();
    }

    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var count = Math.Max(1, ItemCount);
        var itemWidth = ActualWidth / count;
        var center = Math.Clamp((_displayIndex + .5) * itemWidth, itemWidth / 2, ActualWidth - itemWidth / 2);
        const double barTop = 27;
        var bottom = Math.Max(barTop + 2, ActualHeight - 1);
        var radius = Math.Min((bottom - barTop) / 2, 24);
        var shoulder = Math.Min(62, itemWidth * .48);
        var inner = Math.Min(33, itemWidth * .28);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(radius, barTop), true, true);
            context.LineTo(new Point(Math.Max(0, center - shoulder), barTop), true, false);
            context.BezierTo(
                new Point(center - inner - 12, barTop),
                new Point(center - inner, 0),
                new Point(center, 0), true, false);
            context.BezierTo(
                new Point(center + inner, 0),
                new Point(center + inner + 12, barTop),
                new Point(Math.Min(ActualWidth, center + shoulder), barTop), true, false);
            context.LineTo(new Point(ActualWidth - radius, barTop), true, false);
            context.ArcTo(new Point(ActualWidth, barTop + radius), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(ActualWidth, bottom - radius), true, false);
            context.ArcTo(new Point(ActualWidth - radius, bottom), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(radius, bottom), true, false);
            context.ArcTo(new Point(0, bottom - radius), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(0, barTop + radius), true, false);
            context.ArcTo(new Point(radius, barTop), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();

        var fill = FindBrush("NavSurfaceBrush", Colors.Black);
        dc.DrawGeometry(fill, null, geometry);

        // A esfera usa exatamente o mesmo índice interpolado da curva. Assim ela
        // percorre a barra junto com a elevação em vez de surgir na aba de destino.
        dc.DrawEllipse(FindBrush("AccentBrush", Colors.CornflowerBlue), null,
            new Point(center, 26), 26, 26);
    }

    private static Brush FindBrush(string key, Color fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
