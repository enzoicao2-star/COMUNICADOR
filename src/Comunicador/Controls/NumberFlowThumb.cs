using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Comunicador.Controls;

/// <summary>Marcador de slider com número acima e transição vertical curta.</summary>
public sealed class NumberFlowThumb : Thumb
{
    private TextBlock? _currentText;
    private TextBlock? _previousText;
    private TranslateTransform? _currentTransform;
    private TranslateTransform? _previousTransform;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberFlowThumb),
        new FrameworkPropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(NumberFlowThumb), new PropertyMetadata(string.Empty));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Suffix { get => GetValue(SuffixProperty) as string ?? string.Empty; set => SetValue(SuffixProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _currentText = GetTemplateChild("PART_CurrentValue") as TextBlock;
        _previousText = GetTemplateChild("PART_PreviousValue") as TextBlock;
        // Transforms vindos de ControlTemplate podem ser congelados pelo WPF para
        // compartilhamento. O NumberFlow os anima diretamente, então cada texto
        // precisa de uma instância local e mutável.
        if (_currentText is not null)
        {
            _currentTransform = new TranslateTransform();
            _currentText.RenderTransform = _currentTransform;
        }
        if (_previousText is not null)
        {
            _previousTransform = new TranslateTransform();
            _previousText.RenderTransform = _previousTransform;
        }
        if (_currentText is not null) _currentText.Text = Format(Value);
        if (_previousText is not null) _previousText.Text = string.Empty;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberFlowThumb thumb)
        {
            thumb.AnimateValue((double)e.OldValue, (double)e.NewValue);
        }
    }

    private void AnimateValue(double previous, double current)
    {
        if (_currentText is null || _previousText is null
            || _currentTransform is null || _previousTransform is null)
        {
            return;
        }

        _previousText.Text = Format(previous);
        _currentText.Text = Format(current);
        var direction = current >= previous ? 1d : -1d;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        _previousText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));
        _previousTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -5 * direction, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        _currentText.BeginAnimation(OpacityProperty, new DoubleAnimation(.15, 1, TimeSpan.FromMilliseconds(190)));
        _currentTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6 * direction, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }

    private string Format(double value) =>
        $"{Math.Round(value).ToString(CultureInfo.CurrentCulture)}{Suffix}";
}
