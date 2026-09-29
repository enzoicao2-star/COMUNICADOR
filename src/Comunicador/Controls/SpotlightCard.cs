using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Comunicador.Controls;

/// <summary>Card com brilho discreto que segue o ponteiro sem atualizar o restante da lista.</summary>
public sealed class SpotlightCard : ContentControl
{
    private Border? _spotlight;
    private RadialGradientBrush? _brush;

    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        nameof(GlowColor), typeof(Color), typeof(SpotlightCard),
        new PropertyMetadata(Color.FromRgb(108, 99, 255), (d, _) => ((SpotlightCard)d).UpdateGlowColor()));

    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(
        nameof(ReducedMotion), typeof(bool), typeof(SpotlightCard), new PropertyMetadata(false));

    public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _spotlight = GetTemplateChild("PART_Spotlight") as Border;
        _brush = _spotlight?.Background as RadialGradientBrush;
        if (_brush?.IsFrozen == true && _spotlight is not null)
        {
            _brush = _brush.Clone();
            _spotlight.Background = _brush;
        }
        UpdateGlowColor();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        AnimateGlow(1);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        AnimateGlow(0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_brush is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var point = e.GetPosition(this);
        var center = new Point(Math.Clamp(point.X / ActualWidth, 0, 1),
            Math.Clamp(point.Y / ActualHeight, 0, 1));
        _brush.Center = center;
        _brush.GradientOrigin = center;
    }

    private void UpdateGlowColor()
    {
        if (_brush is null || _brush.GradientStops.Count < 2) return;
        var color = GlowColor;
        _brush.GradientStops[0].Color = Color.FromArgb(58, color.R, color.G, color.B);
        _brush.GradientStops[1].Color = Color.FromArgb(0, color.R, color.G, color.B);
    }

    private void AnimateGlow(double opacity)
    {
        if (_spotlight is null) return;
        _spotlight.BeginAnimation(OpacityProperty, null);
        if (ReducedMotion)
        {
            _spotlight.Opacity = opacity;
            return;
        }
        _spotlight.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity,
            TimeSpan.FromMilliseconds(opacity > 0 ? 160 : 260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
        _spotlight.Opacity = opacity;
    }
}
