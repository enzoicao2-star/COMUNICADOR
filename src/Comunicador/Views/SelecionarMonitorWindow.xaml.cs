using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class SelecionarMonitorView : UserControl
{
    private readonly MensagensViewModel _viewModel;
    private DefinirMonitorView? _editorAtual;
    private DestinoMonitor? _destinoAtual;

    public event EventHandler? VoltarSolicitado;

    public SelecionarMonitorView(MensagensViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await CarregarPreviaAsync();
    }

    private async Task CarregarPreviaAsync()
    {
        try
        {
            await _viewModel.AtualizarMiniaturasDosDestinosAsync();
            if (MonitorSelecionado.SelectedItem is DestinoMonitor destino)
                MostrarEditor(destino);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
            or HttpRequestException or TaskCanceledException)
        {
            StatusSelecao.Text = "Não foi possível atualizar a miniatura agora; você ainda pode configurar a mídia.";
        }
    }

    private void Monitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorSelecionado.SelectedItem is not DestinoMonitor destino) return;
        if (_editorAtual is not null && !ReferenceEquals(_destinoAtual, destino))
            _editorAtual.SalvarConfiguracao();
        MostrarEditor(destino);
    }

    private void MostrarEditor(DestinoMonitor destino)
    {
        if (ReferenceEquals(_destinoAtual, destino) && _editorAtual is not null) return;
        StatusSelecao.Text = string.Empty;
        _destinoAtual = destino;
        var editor = new DefinirMonitorView(destino, _viewModel);
        editor.VoltarSolicitado += (_, _) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);
        editor.DefinicaoConcluida += (_, _) => StatusSelecao.Text = $"Configuração salva para {destino.Titulo}.";
        _editorAtual = editor;
        EditorMonitorHost.Content = editor;
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) =>
        VoltarSolicitado?.Invoke(this, EventArgs.Empty);
}
