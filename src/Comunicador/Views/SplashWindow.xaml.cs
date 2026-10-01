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
            var pulse = new DoubleAnimation(0.35, 0.88, TimeSpan.FromSeconds(1.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            OuterGlow.BeginAnimation(OpacityProperty, pulse);
            GlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1.06,
                TimeSpan.FromSeconds(1.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            GlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1.06,
                TimeSpan.FromSeconds(1.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            var beam = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.35))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            LoadingBeam.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1,
                TimeSpan.FromSeconds(1.35)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            LoadingBeam.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(64, 360, beam.Duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        };
    }

    public async Task AnimateExitAsync()
    {
        var duration = TimeSpan.FromMilliseconds(650);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        await Task.Delay(duration);
        Close();
    }
}
