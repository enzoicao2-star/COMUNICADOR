using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Comunicador.ViewModels;
using Microsoft.Win32;

namespace Comunicador.Views;

public partial class DefinirMonitorView : UserControl
{
    private readonly DestinoMonitor _destino;
    private readonly MensagensViewModel _viewModel;
    private bool _carregado;
    private bool _arrastando;
    private Point _inicioArrasto;

    public event EventHandler? VoltarSolicitado;
    public event EventHandler? DefinicaoConcluida;

    public DefinirMonitorView(DestinoMonitor destino, MensagensViewModel viewModel)
    {
        InitializeComponent();
        _destino = destino;
        _viewModel = viewModel;
        PapelParedePreview.DataContext = destino;
        SubtituloMonitor.Text = $"{destino.Titulo} · {destino.Resolucao}";
        var larguraMonitor = destino.Monitor.Width > 0 ? destino.Monitor.Width : 1920;
        var alturaMonitor = destino.Monitor.Height > 0 ? destino.Monitor.Height : 1080;
        var escalaMonitor = Math.Min(520d / larguraMonitor, 310d / alturaMonitor);
        MonitorCanvas.Width = larguraMonitor * escalaMonitor;
        MonitorCanvas.Height = alturaMonitor * escalaMonitor;
        AtualizarImagemPreview(carregarValores: true);
        _carregado = true;
        AtualizarPreview();
    }

    private List<MidiaMonitorEditavel> Imagens => _destino.Midias.Where(item => item.EhImagem).ToList();

    public void AtualizarImagemPreview(bool carregarValores)
    {
        var primeira = Imagens.FirstOrDefault();
        ImagemPreview.Visibility = primeira is null ? Visibility.Collapsed : Visibility.Visible;
        if (primeira is null)
        {
            StatusImagem.Text = "Solte uma imagem ou GIF nesta prévia, ou carregue um arquivo.";
            return;
        }

        ImagemPreview.DataBase64 = primeira.DadosBase64;
        ImagemPreview.MimeType = primeira.MimeType;
        StatusImagem.Text = $"{primeira.Nome} · GIFs são reproduzidos nesta prévia.";
        if (!carregarValores) return;
        SliderX.Value = primeira.XPercent ?? 50;
        SliderY.Value = primeira.YPercent ?? 50;
        SliderTamanho.Value = primeira.TamanhoPercentual;
        ManterAteRemover.IsChecked = primeira.ManterAteRemover;
        RestaurarAposReiniciar.IsChecked = primeira.RestaurarAposReiniciar;
    }

    private void CarregarImagem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Escolher imagem para {_destino.Titulo}",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.bmp|Todos os arquivos|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _viewModel.CarregarArquivoNoMonitor(_destino, dialog.FileName);
            AtualizarImagemPreview(carregarValores: true);
            AtualizarPreview();
        }
    }

    private void Monitor_DragOver(object sender, DragEventArgs e)
    {
        var arquivos = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? e.Data.GetData(DataFormats.FileDrop) as string[] ?? [] : [];
        var extensao = arquivos.Length == 1 ? Path.GetExtension(arquivos[0]).ToLowerInvariant() : string.Empty;
        e.Effects = extensao is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Monitor_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var arquivos = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        if (arquivos.Length != 1) return;
        _viewModel.CarregarArquivoNoMonitor(_destino, arquivos[0]);
        AtualizarImagemPreview(carregarValores: true);
        AtualizarPreview();
    }

    private void Opcao_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_carregado) AtualizarPreview();
    }

    private void AtualizarPreview()
    {
        TextoX.Text = $"{SliderX.Value:0}%";
        TextoY.Text = $"{SliderY.Value:0}%";
        TextoTamanho.Text = $"{SliderTamanho.Value:0}%";

        var proporcao = 1d;
        try
        {
            var primeira = Imagens.FirstOrDefault();
            if (primeira is null) return;
            using var stream = new MemoryStream(primeira.Dados, writable: false);
            var decoder = primeira.MimeType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
                ? new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand)
                : BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0 && decoder.Frames[0].PixelHeight > 0)
                proporcao = (double)decoder.Frames[0].PixelWidth / decoder.Frames[0].PixelHeight;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException) { }

        var largura = Math.Min(MonitorCanvas.Width * SliderTamanho.Value / 100d, MonitorCanvas.Width - 12);
        var altura = Math.Min(largura / Math.Max(.1, proporcao), MonitorCanvas.Height - 12);
        if (largura / Math.Max(.1, proporcao) > altura) largura = altura * proporcao;
        ImagemPreview.Width = Math.Max(28, largura);
        ImagemPreview.Height = Math.Max(28, altura);
        Canvas.SetLeft(ImagemPreview, Math.Clamp(
            MonitorCanvas.Width * SliderX.Value / 100d - ImagemPreview.Width / 2,
            0, MonitorCanvas.Width - ImagemPreview.Width));
        Canvas.SetTop(ImagemPreview, Math.Clamp(
            MonitorCanvas.Height * SliderY.Value / 100d - ImagemPreview.Height / 2,
            0, MonitorCanvas.Height - ImagemPreview.Height));
        Canvas.SetLeft(AlcaRedimensionar, Canvas.GetLeft(ImagemPreview) + ImagemPreview.Width - AlcaRedimensionar.Width / 2);
        Canvas.SetTop(AlcaRedimensionar, Canvas.GetTop(ImagemPreview) + ImagemPreview.Height - AlcaRedimensionar.Height / 2);
    }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source ||
            !ReferenceEquals(source, ImagemPreview) && !ImagemPreview.IsAncestorOf(source)) return;
        _arrastando = true;
        _inicioArrasto = e.GetPosition(MonitorCanvas);
        MonitorCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_arrastando || e.LeftButton != MouseButtonState.Pressed) return;
        var atual = e.GetPosition(MonitorCanvas);
        var esquerda = Canvas.GetLeft(ImagemPreview) + atual.X - _inicioArrasto.X;
        var topo = Canvas.GetTop(ImagemPreview) + atual.Y - _inicioArrasto.Y;
        esquerda = Math.Clamp(esquerda, 0, MonitorCanvas.Width - ImagemPreview.Width);
        topo = Math.Clamp(topo, 0, MonitorCanvas.Height - ImagemPreview.Height);
        Canvas.SetLeft(ImagemPreview, esquerda);
        Canvas.SetTop(ImagemPreview, topo);
        SliderX.Value = 100 * (esquerda + ImagemPreview.Width / 2) / MonitorCanvas.Width;
        SliderY.Value = 100 * (topo + ImagemPreview.Height / 2) / MonitorCanvas.Height;
        _inicioArrasto = atual;
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _arrastando = false;
        MonitorCanvas.ReleaseMouseCapture();
    }

    private void Redimensionar_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (MonitorCanvas.Width <= 0 || ImagemPreview.Width <= 0) return;
        var proporcao = ImagemPreview.Width / Math.Max(1, ImagemPreview.Height);
        var deltaHorizontal = e.HorizontalChange;
        var deltaVerticalComoLargura = e.VerticalChange * proporcao;
        var delta = Math.Abs(deltaHorizontal) >= Math.Abs(deltaVerticalComoLargura)
            ? deltaHorizontal
            : deltaVerticalComoLargura;
        var larguraDesejada = Math.Clamp(
            ImagemPreview.Width + delta, MonitorCanvas.Width * .1, MonitorCanvas.Width);
        SliderTamanho.Value = 100 * larguraDesejada / MonitorCanvas.Width;
    }

    private void Permanencia_Changed(object sender, RoutedEventArgs e)
    {
        if (RestaurarAposReiniciar is not null && ManterAteRemover.IsChecked != true)
            RestaurarAposReiniciar.IsChecked = false;
    }

    private void Definir_Click(object sender, RoutedEventArgs e)
    {
        if (!SalvarConfiguracao())
        {
            StatusImagem.Text = "Carregue uma imagem antes de definir o monitor.";
            return;
        }
        DefinicaoConcluida?.Invoke(this, EventArgs.Empty);
    }

    public bool SalvarConfiguracao()
    {
        var imagens = Imagens;
        if (imagens.Count == 0) return false;
        var manter = ManterAteRemover.IsChecked == true || RestaurarAposReiniciar.IsChecked == true;
        foreach (var imagem in imagens)
        {
            imagem.XPercent = SliderX.Value;
            imagem.YPercent = SliderY.Value;
            imagem.TamanhoPercentual = SliderTamanho.Value;
            imagem.ManterAteRemover = manter;
            imagem.RestaurarAposReiniciar = RestaurarAposReiniciar.IsChecked == true;
        }
        return true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);
}
