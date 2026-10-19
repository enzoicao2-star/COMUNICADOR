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

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string ComputerId { get => _computerId; set => SetField(ref _computerId, value); }
    public string Nome { get => _nome; set => SetField(ref _nome, value); }
    public int MonitorIndex { get => _monitorIndex; set => SetField(ref _monitorIndex, value); }
    public double XPercent { get => _xPercent; set => SetField(ref _xPercent, Math.Clamp(value, 0, 100)); }
    public double YPercent { get => _yPercent; set => SetField(ref _yPercent, Math.Clamp(value, 0, 100)); }
    public int WidthPercent
    {
        get => _widthPercent;
        set
        {
            if (SetField(ref _widthPercent, Math.Clamp(value, 10, 100)))
                OnPropertyChanged(nameof(TamanhoTexto));
        }
    }

    public string MonitorTexto => $"Monitor {MonitorIndex + 1}";
    public string TamanhoTexto => $"{WidthPercent}%";
}
