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
        => AbrirGerenciamentoComputador(sender, e);

    private void AbrirGerenciamentoComputador(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Computador computador }
            || DataContext is not ComputadoresViewModel viewModel) return;

        viewModel.ComputadorGerenciado = computador;
        var janela = new GerenciarComputadorWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = viewModel,
        };
        janela.ShowDialog();
        viewModel.ComputadorGerenciado = null;
        e.Handled = true;
    }
}
