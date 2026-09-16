using System.Collections.ObjectModel;
using System.IO;
using Comunicador.Models;
using Comunicador.Protocol;

namespace Comunicador.ViewModels;

public sealed class DestinoMonitor
{
    public Computador Computador { get; init; } = null!;
    public MonitorInfo Monitor { get; init; } = null!;
    public ObservableCollection<ImagemMonitorEditavel> Imagens { get; } = new();

    public string Chave => $"{Computador.Id}:{Monitor.Index}";
    public string Titulo => $"{Computador.NomeExibicao} · Monitor {Monitor.Index + 1}";
    public string Resolucao => Monitor.Descricao;
    public double AlturaPreview => Monitor.Width > 0 && Monitor.Height > 0
        ? Math.Clamp(260d * Monitor.Height / Monitor.Width, 105, 180)
        : 146;
}

public sealed class ImagemMonitorEditavel : ViewModelBase
{
    private double _tamanhoPercentual = 70;

    public string Caminho { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public byte[] Dados { get; init; } = Array.Empty<byte>();

    public double TamanhoPercentual
    {
        get => _tamanhoPercentual;
        set
        {
            if (SetField(ref _tamanhoPercentual, Math.Round(Math.Clamp(
                value, ProtocolConstants.MinImageWidthPercent, ProtocolConstants.MaxImageWidthPercent))))
            {
                OnPropertyChanged(nameof(PreviewWidth));
                OnPropertyChanged(nameof(TamanhoTexto));
            }
        }
    }

    public double PreviewWidth => Math.Max(35, 220 * TamanhoPercentual / 100d);
    public string TamanhoTexto => $"{TamanhoPercentual:0}% da largura do monitor";

    public ImagemMonitor ParaProtocolo(int monitorIndex) => new()
    {
        MonitorIndex = monitorIndex,
        WidthPercent = (int)TamanhoPercentual,
        Image = new ConteudoImagem
        {
            Name = Path.GetFileName(Nome),
            MimeType = MimeType,
            DataBase64 = Convert.ToBase64String(Dados),
        },
    };
}
