using System.Windows;

namespace Comunicador.Views;

public partial class GerenciarComputadorWindow : Window
{
    public GerenciarComputadorWindow() => InitializeComponent();

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
