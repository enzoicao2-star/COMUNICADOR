using System.Windows;
using System.Windows.Controls;

namespace Comunicador.Views;

public partial class GerenciarComputadorWindow : UserControl
{
    public GerenciarComputadorWindow() => InitializeComponent();

    public event EventHandler? VoltarSolicitado;

    private void Fechar_Click(object sender, RoutedEventArgs e) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);
}
