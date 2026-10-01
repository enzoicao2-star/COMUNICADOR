using System.Windows;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class AcoesComputadorWindow : Window
{
    public event EventHandler? ConfigurarSolicitado;
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
            StatusAcoes.Text = "Este painel não tem permissão para enviar mídias.";
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

        _mensagens.IniciarEnvioIndividual(_computador, imagem);
        try
        {
            var janela = new Window
            {
                Title = imagem ? $"Enviar imagem · {_computador.NomeExibicao}" : $"Mensagem beta · {_computador.NomeExibicao}",
                Owner = this,
                Width = 1080,
                Height = 790,
                MinWidth = 760,
                MinHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
                Content = new MensagensView { DataContext = _mensagens },
            };
            janela.ShowDialog();
        }
        finally
        {
            _mensagens.FinalizarEnvioIndividual();
        }
    }

    private void MostrarCmd_Click(object sender, RoutedEventArgs e) => CmdPanel.Visibility = Visibility.Visible;

    private void Configurar_Click(object sender, RoutedEventArgs e)
    {
        ConfigurarSolicitado?.Invoke(this, EventArgs.Empty);
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
