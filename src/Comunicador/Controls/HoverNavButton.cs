using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Comunicador.Controls;

public sealed class HoverNavButton : Button
{
    private readonly DispatcherTimer _timer;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(HoverNavButton), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HoverNavButton), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsLabelVisibleProperty = DependencyProperty.Register(
        nameof(IsLabelVisible), typeof(bool), typeof(HoverNavButton), new PropertyMetadata(false));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsLabelVisible
    {
        get => (bool)GetValue(IsLabelVisibleProperty);
        private set => SetValue(IsLabelVisibleProperty, value);
    }

    public HoverNavButton()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (IsMouseOver) IsLabelVisible = true;
        };
        MouseEnter += (_, _) => _timer.Start();
        MouseLeave += (_, _) =>
        {
            _timer.Stop();
            IsLabelVisible = false;
        };
    }
}
