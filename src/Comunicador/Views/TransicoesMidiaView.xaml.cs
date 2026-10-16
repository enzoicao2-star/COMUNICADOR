using System.Windows;
using System.Windows.Controls;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class TransicoesMidiaView : UserControl
{
    private bool _entradaSelecionada = true;

    public TransicoesMidiaView(MensagensViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        GaleriaEfeitos.Tag = false;
        ListaCategorias.ItemsSource = TransicaoCatalogo.Categorias;
        if (ListaCategorias.Items.Count > 0) ListaCategorias.SelectedIndex = 0;
    }

    private void Categoria_SelectionChanged(object sender, SelectionChangedEventArgs e) => AtualizarGaleria();

    private void BuscaTransicoes_TextChanged(object sender, TextChangedEventArgs e) => AtualizarGaleria();

    private void AtualizarGaleria()
    {
        if (GaleriaEfeitos is null || ListaCategorias?.SelectedItem is not CategoriaTransicao categoria) return;
        var busca = BuscaTransicoes?.Text?.Trim();
        GaleriaEfeitos.ItemsSource = string.IsNullOrWhiteSpace(busca)
            ? categoria.Opcoes
            : categoria.Opcoes.Where(o => o.Nome.Contains(busca, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private void LadoTransicao_Checked(object sender, RoutedEventArgs e)
    {
        // O Checked do RadioButton pode disparar durante o carregamento do XAML,
        // antes de a galeria ter sido criada. Nesse momento, mantenha a seleção
        // padrão (entrada) e deixe o construtor inicializar o estado da tela.
        if (GaleriaEfeitos is null || sender is not RadioButton radio) return;

        _entradaSelecionada = ReferenceEquals(radio, LadoEntradaRadio);
        GaleriaEfeitos.Tag = !_entradaSelecionada;
        if (DataContext is MensagensViewModel vm)
            vm.PreviewEfeitoSaida = !_entradaSelecionada;
    }

    private void SelecionarEfeito_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string efeito } || DataContext is not MensagensViewModel vm) return;
        if (_entradaSelecionada) vm.EfeitoEntrada = efeito;
        else vm.EfeitoSaida = efeito;
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
