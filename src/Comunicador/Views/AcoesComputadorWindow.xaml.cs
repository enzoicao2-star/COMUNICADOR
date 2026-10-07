using System.Windows;
using System.Windows.Controls;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class AcoesComputadorWindow : UserControl
{
    public event EventHandler? ConfigurarSolicitado;
    public event Action<bool>? EnviarSolicitado;
    private readonly Computador _computador;
    private readonly ComputadoresViewModel _computadores;
    private readonly MensagensViewModel? _mensagens;

    public AcoesComputadorWindow(Computador computador, ComputadoresViewModel computadores,
        MensagensViewModel? mensagens)
    {
        InitializeComponent();
        _computador = computador;
        _computadores = computadores;
        _mensagens = mensagens;
        DataContext = computadores;
        if (!computadores.PodeEnviarMidias)
            StatusAcoes.Text = "Envio de imagens e mensagens requer a permissão de envio de mídias.";
    }

    private void EnviarImagem_Click(object sender, RoutedEventArgs e) => AbrirCompositor(imagem: true);
    private void EnviarMensagem_Click(object sender, RoutedEventArgs e) => AbrirCompositor(imagem: false);

    private void AbrirCompositor(bool imagem)
    {
        if (_mensagens is null || !_computadores.PodeEnviarMidias)
        {
            StatusAcoes.Text = "Envio permitido somente para painéis com a permissão de enviar mídias.";
            return;
        }

        EnviarSolicitado?.Invoke(imagem);
    }

    private void MostrarCmd_Click(object sender, RoutedEventArgs e) => CmdPanel.Visibility = Visibility.Visible;

    private void OcultarCmd_Click(object sender, RoutedEventArgs e) => CmdPanel.Visibility = Visibility.Collapsed;

    private void Configurar_Click(object sender, RoutedEventArgs e)
    {
        ConfigurarSolicitado?.Invoke(this, EventArgs.Empty);
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
