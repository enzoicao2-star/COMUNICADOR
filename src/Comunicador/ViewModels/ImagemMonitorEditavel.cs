using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Comunicador.Models;
using Comunicador.Protocol;

namespace Comunicador.ViewModels;

public sealed class DestinoMonitor : ViewModelBase
{
    private BitmapSource? _wallpaperPreview;
    public Computador Computador { get; init; } = null!;
    public MonitorInfo Monitor { get; init; } = null!;
    public ObservableCollection<MidiaMonitorEditavel> Midias { get; } = new();

    public BitmapSource? WallpaperPreview
    {
        get => _wallpaperPreview;
        private set => SetField(ref _wallpaperPreview, value);
    }
    public string WallpaperPreviewState => WallpaperPreview is null
        ? "Miniatura do papel de parede ainda não disponível"
        : "Papel de parede atual deste computador";

    public void DefinirWallpaperPreview(byte[]? bytes)
    {
        BitmapSource? preview = null;
        if (bytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();
                preview = bitmap;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
            {
                preview = null;
            }
        }
        WallpaperPreview = preview;
        OnPropertyChanged(nameof(WallpaperPreviewState));
    }

    public string Chave => $"{Computador.Id}:{Monitor.Index}";
    public string Titulo => $"{Computador.NomeExibicao} · Monitor {Monitor.Index + 1}";
    public string Resolucao => Monitor.Descricao;
    public double AlturaPreview => Monitor.Width > 0 && Monitor.Height > 0
        ? Math.Clamp(300d * Monitor.Height / Monitor.Width, 96, 220)
        : 169;
}

public enum TipoMidiaMonitor
{
    Imagem,
    Video,
}

public sealed class MidiaMonitorEditavel : ViewModelBase
{
    private static readonly ConditionalWeakTable<byte[], string> CodificacoesBase64 = new();
    private double _tamanhoPercentual = 70;
    private double? _xPercent;
    private double? _yPercent;
    private bool _manterAteRemover;
    private bool _restaurarAposReiniciar;
    private BitmapSource? _imagemPreview;
    private bool _imagemPreviewCarregada;

    public string Caminho { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public byte[] Dados { get; init; } = Array.Empty<byte>();
    public string DadosBase64 => CodificarBase64(Dados);
    internal static string CodificarBase64(byte[] dados) =>
        CodificacoesBase64.GetValue(dados, static bytes => Convert.ToBase64String(bytes));
    public TipoMidiaMonitor Tipo { get; init; }
    public bool EhImagem => Tipo == TipoMidiaMonitor.Imagem;
    public bool EhVideo => Tipo == TipoMidiaMonitor.Video;
    public string? PreviewDataBase64 => EhImagem ? DadosBase64 : null;
    public BitmapSource? ImagemPreview
    {
        get
        {
            if (_imagemPreviewCarregada || !EhImagem || Dados.Length == 0) return _imagemPreview;
            _imagemPreviewCarregada = true;
            try
            {
                using var stream = new MemoryStream(Dados, writable: false);
                BitmapSource frame;
                if (MimeType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
                {
                    var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }
                else
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    frame = bitmap;
                }

                if (frame.CanFreeze) frame.Freeze();
                _imagemPreview = frame;
            }
            catch (Exception)
            {
                _imagemPreview = null;
            }

            return _imagemPreview;
        }
    }

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

    public double? XPercent
    {
        get => _xPercent;
        set => SetField(ref _xPercent, value is null ? null : Math.Clamp(value.Value, 0, 100));
    }

    public double? YPercent
    {
        get => _yPercent;
        set => SetField(ref _yPercent, value is null ? null : Math.Clamp(value.Value, 0, 100));
    }

    public bool ManterAteRemover
    {
        get => _manterAteRemover;
        set
        {
            if (SetField(ref _manterAteRemover, value))
                OnPropertyChanged(nameof(DuracaoIndefinida));
        }
    }

    public bool RestaurarAposReiniciar
    {
        get => _restaurarAposReiniciar;
        set
        {
            if (SetField(ref _restaurarAposReiniciar, value) && value)
                ManterAteRemover = true;
        }
    }

    public bool DuracaoIndefinida => ManterAteRemover;

    public ImagemMonitor ParaProtocolo(int monitorIndex) => new()
    {
        MonitorIndex = monitorIndex,
        WidthPercent = (int)TamanhoPercentual,
        XPercent = XPercent,
        YPercent = YPercent,
        KeepUntilRemoved = ManterAteRemover,
        RestoreAfterRestart = RestaurarAposReiniciar,
        Image = new ConteudoImagem
        {
            Name = Path.GetFileName(Nome),
            MimeType = MimeType,
            DataBase64 = DadosBase64,
            RawData = Dados,
        },
    };

    public VideoMonitor ParaVideoProtocolo(int monitorIndex) => new()
    {
        MonitorIndex = monitorIndex,
        WidthPercent = (int)TamanhoPercentual,
        Video = new ConteudoVideo
        {
            Name = Path.GetFileName(Nome),
            MimeType = MimeType,
            DataBase64 = DadosBase64,
            RawData = Dados,
        },
    };
}
