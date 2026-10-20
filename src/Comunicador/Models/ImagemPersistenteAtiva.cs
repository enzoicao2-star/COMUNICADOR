using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Comunicador.Models;

/// <summary>Configuração leve guardada localmente para controlar uma imagem ativa no Receptor.</summary>
public sealed class ImagemPersistenteAtiva : ObservableModel
{
    private string _id = string.Empty;
    private string _nome = string.Empty;
    private string _computerId = string.Empty;
    private int _monitorIndex;
    private double _xPercent = 50;
    private double _yPercent = 50;
    private int _widthPercent = 30;
    private int _tempoSegundos = 30;
    private BitmapSource? _miniatura;
    private double _previewLeft, _previewTop, _previewWidth, _previewHeight;
    private string _prazoTexto = "Até eu remover";

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string ComputerId { get => _computerId; set => SetField(ref _computerId, value); }
    public string Nome { get => _nome; set => SetField(ref _nome, value); }
    public int MonitorIndex { get => _monitorIndex; set => SetField(ref _monitorIndex, value); }
    public double XPercent { get => _xPercent; set { if (SetField(ref _xPercent, Math.Clamp(value, 0, 100))) UpdatePreviewPosition(); } }
    public double YPercent { get => _yPercent; set { if (SetField(ref _yPercent, Math.Clamp(value, 0, 100))) UpdatePreviewPosition(); } }
    public int WidthPercent
    {
        get => _widthPercent;
        set
        {
            var oldWidth = _widthPercent;
            if (SetField(ref _widthPercent, Math.Clamp(value, 10, 100)))
            {
                OnPropertyChanged(nameof(TamanhoTexto));
                if (PreviewWidth > 0 && oldWidth > 0)
                {
                    var scale = (double)_widthPercent / oldWidth;
                    PreviewWidth = Math.Min(320, PreviewWidth * scale);
                    PreviewHeight = Math.Min(180, PreviewHeight * scale);
                    UpdatePreviewPosition();
                }
            }
        }
    }

    public string MonitorTexto => $"Monitor {MonitorIndex + 1}";
    public string TamanhoTexto => $"{WidthPercent}%";
    public int TempoSegundos { get => _tempoSegundos; set => SetField(ref _tempoSegundos, value); }
    public BitmapSource? Miniatura { get => _miniatura; private set => SetField(ref _miniatura, value); }
    public double PreviewLeft { get => _previewLeft; private set => SetField(ref _previewLeft, value); }
    public double PreviewTop { get => _previewTop; private set => SetField(ref _previewTop, value); }
    public double PreviewWidth { get => _previewWidth; private set => SetField(ref _previewWidth, value); }
    public double PreviewHeight { get => _previewHeight; private set => SetField(ref _previewHeight, value); }
    public string PrazoTexto { get => _prazoTexto; private set => SetField(ref _prazoTexto, value); }

    public void DefinirPrazo(int seconds) => PrazoTexto =
        $"Até {DateTimeOffset.Now.AddSeconds(seconds):HH:mm:ss}";

    private void UpdatePreviewPosition()
    {
        if (PreviewWidth <= 0 || PreviewHeight <= 0) return;
        PreviewLeft = Math.Clamp(XPercent * 3.2 - PreviewWidth / 2, 0, 320 - PreviewWidth);
        PreviewTop = Math.Clamp(YPercent * 1.8 - PreviewHeight / 2, 0, 180 - PreviewHeight);
    }

    public static ImagemPersistenteAtiva? FromSnapshot(string computerId, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        string? ReadString(string key) => data.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        double ReadNumber(string key, double fallback) => data.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number) ? number : fallback;
        var id = ReadString("id");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return null;
        var item = new ImagemPersistenteAtiva
        {
            Id = id, ComputerId = computerId,
            Nome = ReadString("name") ?? "Imagem",
            MonitorIndex = (int)Math.Clamp(ReadNumber("monitor_index", 0), 0, 15),
            XPercent = ReadNumber("x_percent", 50),
            YPercent = ReadNumber("y_percent", 50),
            WidthPercent = (int)Math.Clamp(ReadNumber("width_percent", 70), 10, 100),
        };
        item.PreviewLeft = Math.Clamp(ReadNumber("left_percent", 0), 0, 100) * 3.2;
        item.PreviewTop = Math.Clamp(ReadNumber("top_percent", 0), 0, 100) * 1.8;
        item.PreviewWidth = Math.Clamp(ReadNumber("display_width_percent", 25), 1, 100) * 3.2;
        item.PreviewHeight = Math.Clamp(ReadNumber("display_height_percent", 25), 1, 100) * 1.8;
        if (DateTimeOffset.TryParse(ReadString("expires_at"), out var expiry))
            item.PrazoTexto = $"Até {expiry.ToLocalTime():HH:mm:ss}";
        var encoded = ReadString("thumbnail_base64");
        if (!string.IsNullOrWhiteSpace(encoded) && encoded.Length <= 100_000)
        {
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                item.Miniatura = bitmap;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or NotSupportedException) { }
        }
        return item;
    }
}
