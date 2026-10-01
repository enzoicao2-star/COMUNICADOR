using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Comunicador.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
            var duration = TimeSpan.FromMilliseconds(850);
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.91, 1.07, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = easing,
            });
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.91, 1.07, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = easing,
            });
            PulseIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = easing,
            });
        };
    }

    public async Task AnimateExitAsync()
    {
        var duration = TimeSpan.FromMilliseconds(240);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        await Task.Delay(duration);
        Close();
    }
}
