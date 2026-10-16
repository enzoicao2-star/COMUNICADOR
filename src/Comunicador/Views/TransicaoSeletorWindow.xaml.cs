using System.Windows;
using System.Windows.Controls;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class TransicaoSeletorWindow : Window
{
    private readonly MensagensViewModel _viewModel;
    private readonly bool _saida;
    private readonly string _efeitoAtual;

    public TransicaoSeletorWindow(MensagensViewModel viewModel, bool saida, string efeitoAtual)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _saida = saida;
        _efeitoAtual = efeitoAtual;
        DataContext = viewModel;
        Tag = saida;
        TituloJanela.Text = saida ? "Selecionar transição de saída" : "Selecionar transição de entrada";

        ListaCategorias.ItemsSource = TransicaoCatalogo.ObterCategoriasPara(saida);
        var categoriaAtual = TransicaoCatalogo.Categorias.FirstOrDefault(c =>
            c.Opcoes.Any(o => string.Equals(o.Id, _efeitoAtual, StringComparison.OrdinalIgnoreCase)))
            ?? TransicaoCatalogo.Categorias.FirstOrDefault();
        ListaCategorias.SelectedItem = categoriaAtual;
        AtualizarEfeitos();

        var efeitoSelecionado = ListaEfeitos.Items
            .Cast<OpcaoTransicao>()
            .FirstOrDefault(o => string.Equals(o.Id, _efeitoAtual, StringComparison.OrdinalIgnoreCase));
        if (efeitoSelecionado is not null)
        {
            ListaEfeitos.SelectedItem = efeitoSelecionado;
            ListaEfeitos.ScrollIntoView(efeitoSelecionado);
            TextoSelecao.Text = $"Selecionada: {efeitoSelecionado.Nome}";
        }
    }

    private void Categoria_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AtualizarEfeitos();

    private void BuscaTransicoes_TextChanged(object sender, TextChangedEventArgs e) =>
        AtualizarEfeitos();

    private void Efeito_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextoSelecao is null) return;
        TextoSelecao.Text = ListaEfeitos.SelectedItem is OpcaoTransicao efeito
            ? $"Selecionada: {efeito.Nome}"
            : "Selecione uma transição.";
    }

    private void AtualizarEfeitos()
    {
        if (ListaEfeitos is null || ListaCategorias?.SelectedItem is not CategoriaTransicao categoria)
            return;

        var selecionadoAntes = ListaEfeitos.SelectedItem as OpcaoTransicao;
        var consulta = BuscaTransicoes?.Text?.Trim();
        var efeitos = string.IsNullOrWhiteSpace(consulta)
            ? categoria.Opcoes
            : categoria.Opcoes.Where(o => o.Nome.Contains(consulta, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        ListaEfeitos.ItemsSource = efeitos;

        var idPreferido = selecionadoAntes?.Id ?? _efeitoAtual;
        var atual = efeitos.FirstOrDefault(o =>
            string.Equals(o.Id, idPreferido, StringComparison.OrdinalIgnoreCase));
        ListaEfeitos.SelectedItem = atual;
        TextoSelecao.Text = atual is null
            ? "Selecione uma transição."
            : $"Selecionada: {atual.Nome}";
    }

    private void Definir_Click(object sender, RoutedEventArgs e)
    {
        if (ListaEfeitos.SelectedItem is not OpcaoTransicao efeito) return;
        if (_saida) _viewModel.EfeitoSaida = efeito.Id;
        else _viewModel.EfeitoEntrada = efeito.Id;
        DialogResult = true;
    }
}
