using System.Windows;

namespace Comunicador.Views;

public partial class AtualizacaoConcluidaWindow : Window
{
    public AtualizacaoConcluidaWindow(string resumo)
    {
        InitializeComponent();
        Detalhes.Text = resumo;
    }

    private void Copiar_OnClick(object sender, RoutedEventArgs e) => Clipboard.SetText(Detalhes.Text);
    private void Fechar_OnClick(object sender, RoutedEventArgs e) => Close();
}
