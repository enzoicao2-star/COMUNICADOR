using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class ComputadoresView : UserControl
{
    public ComputadoresView()
    {
        InitializeComponent();
    }

    private void AbrirGerenciamentoComputador_Click(object sender, RoutedEventArgs e)
        => AbrirGerenciamentoComputador(sender, e);

    private void AbrirGerenciamentoComputador_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Computador computador }
            || DataContext is not ComputadoresViewModel viewModel) return;

        if (!viewModel.PodeAbrirAcoesIndividuais)
        {
            viewModel.MostrarAvisoTopo("Ações individuais disponíveis somente para administradores.");
            e.Handled = true;
            return;
        }

        var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
        viewModel.ComputadorGerenciado = computador;
        var janela = new AcoesComputadorWindow(computador, viewModel, mainViewModel?.Mensagens)
        {
            Owner = Window.GetWindow(this),
        };
        var abrirConfiguracoes = false;
        janela.ConfigurarSolicitado += (_, _) =>
        {
            abrirConfiguracoes = true;
            janela.Close();
        };
        janela.ShowDialog();
        if (abrirConfiguracoes) ExibirConfiguracoes(computador, viewModel);
        else viewModel.ComputadorGerenciado = null;
        e.Handled = true;
    }

    private void AbrirGerenciamentoComputador(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Computador computador }
            || DataContext is not ComputadoresViewModel viewModel) return;

        ExibirConfiguracoes(computador, viewModel);
        e.Handled = true;
    }

    private void ExibirConfiguracoes(Computador computador, ComputadoresViewModel viewModel)
    {
        viewModel.ComputadorGerenciado = computador;
        var painel = new GerenciarComputadorWindow { DataContext = viewModel };
        painel.VoltarSolicitado += (_, _) =>
        {
            ConfiguracoesComputadorHost.Content = null;
            ConfiguracoesComputadorHost.Visibility = Visibility.Collapsed;
            ListaComputadores.Visibility = Visibility.Visible;
            viewModel.ComputadorGerenciado = null;
        };
        ListaComputadores.Visibility = Visibility.Collapsed;
        ConfiguracoesComputadorHost.Content = painel;
        ConfiguracoesComputadorHost.Visibility = Visibility.Visible;
    }
}
