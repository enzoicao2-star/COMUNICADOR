using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Comunicador.ViewModels;
using Microsoft.Win32;

namespace Comunicador.Views;

public partial class SelecionarMonitorWindow : Window
{
    private MensagensViewModel? ViewModel => DataContext as MensagensViewModel;

    public SelecionarMonitorWindow(MensagensViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.AtualizarMiniaturasDosDestinosAsync();
    }

    private async void Atualizar_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.AtualizarMiniaturasDosDestinosAsync();
    }

    private void DefinirMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DestinoMonitor destino }) return;
        var imagens = destino.Midias.Where(item => item.EhImagem).ToList();
        if (imagens.Count == 0)
        {
            MessageBox.Show(this, "Solte ou escolha uma imagem para este monitor antes de ajustar posição e tamanho.",
                "Imagem necessária", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new DefinirMonitorWindow(destino, imagens) { Owner = this }.ShowDialog();
    }

    private void OutraImagem_Click(object sender, RoutedEventArgs e) => AbrirArquivo(sender, "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.bmp");

    private void OutroVideo_Click(object sender, RoutedEventArgs e) => AbrirArquivo(sender, "Vídeos|*.mp4;*.wmv");

    private void AbrirArquivo(object sender, string filtro)
    {
        if (sender is not Button { DataContext: DestinoMonitor destino } || ViewModel is not { } viewModel) return;
        var dialog = new OpenFileDialog { Filter = filtro + "|Todos os arquivos|*.*", Multiselect = false };
        if (dialog.ShowDialog(this) == true) viewModel.CarregarArquivoNoMonitor(destino, dialog.FileName);
    }

    private void Monitor_DragOver(object sender, DragEventArgs e)
    {
        var arquivos = Arquivos(e);
        var valido = arquivos.Length > 0 && (arquivos.All(EhImagem) || arquivos.Length == 1 && EhVideo(arquivos[0]));
        e.Effects = valido ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Monitor_DragEnter(object sender, DragEventArgs e)
    {
        Monitor_DragOver(sender, e);
        if (e.Effects == DragDropEffects.Copy && sender is Border border
            && TryFindResource("AccentBrush") is Brush accent)
        {
            border.BorderBrush = accent;
            border.BorderThickness = new Thickness(2);
        }
    }

    private void Monitor_DragLeave(object sender, DragEventArgs e) => RestoreBorder(sender);

    private void Monitor_Drop(object sender, DragEventArgs e)
    {
        RestoreBorder(sender);
        if (sender is Border { DataContext: DestinoMonitor destino } && ViewModel is { } viewModel)
            foreach (var path in Arquivos(e)) viewModel.CarregarArquivoNoMonitor(destino, path);
        e.Handled = true;
    }

    private void RestoreBorder(object sender)
    {
        if (sender is not Border border) return;
        border.SetResourceReference(Border.BorderBrushProperty, "CardGlassBorderBrush");
        border.BorderThickness = new Thickness(1);
    }

    private static string[] Arquivos(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop)
        ? e.Data.GetData(DataFormats.FileDrop) as string[] ?? [] : [];

    private static bool EhImagem(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp";

    private static bool EhVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".wmv";
}
