using System.Windows;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class TransicoesMidiaView : System.Windows.Controls.UserControl
{
    public TransicoesMidiaView(MensagensViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void SelecionarEntrada_Click(object sender, RoutedEventArgs e) =>
        AbrirSeletor(saida: false);

    private void SelecionarSaida_Click(object sender, RoutedEventArgs e) =>
        AbrirSeletor(saida: true);

    private void AbrirSeletor(bool saida)
    {
        if (DataContext is not MensagensViewModel viewModel) return;
        var efeitoAtual = saida ? viewModel.EfeitoSaida : viewModel.EfeitoEntrada;
        var seletor = new TransicaoSeletorWindow(viewModel, saida, efeitoAtual);
        if (Window.GetWindow(this) is { } owner) seletor.Owner = owner;
        seletor.ShowDialog();
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
