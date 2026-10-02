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

        var main = Window.GetWindow(this) as MainWindow;
        var mainViewModel = main?.DataContext as MainViewModel;
        if (main is null || mainViewModel is null) return;
        viewModel.ComputadorGerenciado = computador;
        var acoes = new AcoesComputadorWindow(computador, viewModel, mainViewModel.Mensagens);
        acoes.ConfigurarSolicitado += (_, _) => AbrirConfiguracoes(main, computador, viewModel);
        acoes.EnviarSolicitado += imagem =>
        {
            mainViewModel.Mensagens.IniciarEnvioIndividual(computador, imagem);
            var compositor = new MensagensView { DataContext = mainViewModel.Mensagens };
            main.NavegarPara(compositor, mainViewModel.Mensagens.FinalizarEnvioIndividual);
        };
        main.NavegarPara(acoes, () => viewModel.ComputadorGerenciado = null);
        e.Handled = true;
    }

    private void AbrirGerenciamentoComputador(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Computador computador }
            || DataContext is not ComputadoresViewModel viewModel) return;

        if (Window.GetWindow(this) is MainWindow main)
            AbrirConfiguracoes(main, computador, viewModel, limparSelecaoAoVoltar: true);
        e.Handled = true;
    }

    private static void AbrirConfiguracoes(MainWindow main, Computador computador,
        ComputadoresViewModel viewModel, bool limparSelecaoAoVoltar = false)
    {
        viewModel.ComputadorGerenciado = computador;
        var painel = new GerenciarComputadorWindow { DataContext = viewModel };
        painel.VoltarSolicitado += (_, _) => main.NavegarVoltar();
        main.NavegarPara(painel, limparSelecaoAoVoltar
            ? () => viewModel.ComputadorGerenciado = null : null);
    }
}
