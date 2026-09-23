using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Comunicador.Views;

public partial class MensagensView : UserControl
{
    public MensagensView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += (_, _) => AtualizarPulsacao();
        Unloaded += (_, _) => PararPulsacao();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => AtualizarPulsacao();

    private void AtualizarPulsacao()
    {
        if (!IsLoaded || !IsVisible)
        {
            PararPulsacao();
            return;
        }

        IndicadorEnvio.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, .28,
            TimeSpan.FromSeconds(.82))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void PararPulsacao() => IndicadorEnvio.BeginAnimation(UIElement.OpacityProperty, null);
}
