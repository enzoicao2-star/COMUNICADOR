using System.Windows;
using System.Windows.Controls;

namespace Comunicador.Views;

public partial class AtualizacaoConcluidaWindow : UserControl
{
    public AtualizacaoConcluidaWindow(string resumo)
    {
        InitializeComponent();
        Detalhes.Text = resumo;
    }

    private void Copiar_OnClick(object sender, RoutedEventArgs e) => Clipboard.SetText(Detalhes.Text);
    private void Fechar_OnClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
