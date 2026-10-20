using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private ImagemPersistenteAtiva? _draggingImage;

    public AcoesComputadorWindow(Computador computador, ComputadoresViewModel computadores,
        MensagensViewModel? mensagens)
    {
        InitializeComponent();
        _computador = computador;
        _computadores = computadores;
        _mensagens = mensagens;
        DataContext = computadores;
        MidiasNaTelaLista.DataContext = computador;
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

    private async void MostrarMidias_Click(object sender, RoutedEventArgs e)
    {
        MediaPanel.Visibility = Visibility.Visible;
        await _computadores.ConsultarMidiasAtivasAsync(_computador);
    }

    private void OcultarMidias_Click(object sender, RoutedEventArgs e) => MediaPanel.Visibility = Visibility.Collapsed;

    private async void AtualizarMidias_Click(object sender, RoutedEventArgs e)
    {
        await _computadores.ConsultarMidiasAtivasAsync(_computador);
    }

    private async void ControlarMidia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ImagemPersistenteAtiva image, Tag: string action }) return;
        await _computadores.ControlarImagemPersistenteAsync(image, action);
    }

    private void MidiaPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ImagemPersistenteAtiva image, Parent: Canvas canvas }) return;
        _draggingImage = image;
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void MidiaPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingImage is null || sender is not Canvas canvas || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(canvas);
        _draggingImage.XPercent = point.X / 320 * 100;
        _draggingImage.YPercent = point.Y / 180 * 100;
    }

    private async void MidiaPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingImage is null || sender is not Canvas canvas) return;
        canvas.ReleaseMouseCapture();
        var image = _draggingImage;
        _draggingImage = null;
        await _computadores.ControlarImagemPersistenteAsync(image, "move");
    }

    private void OcultarCmd_Click(object sender, RoutedEventArgs e) => CmdPanel.Visibility = Visibility.Collapsed;

    private void Configurar_Click(object sender, RoutedEventArgs e)
    {
        ConfigurarSolicitado?.Invoke(this, EventArgs.Empty);
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavegarVoltar();
}
