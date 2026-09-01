namespace Comunicador.Models;

public enum StatusComputador
{
    Desconhecido,
    Online,
    Offline,
}

public sealed class Computador : ObservableModel
{
    private string _id = string.Empty;
    private string _nome = string.Empty;
    private string _enderecoIp = string.Empty;
    private int _portaTcp;
    private bool _pareado;
    private string? _token;
    private StatusComputador _status = StatusComputador.Desconhecido;
    private DateTime _ultimaVezVisto = DateTime.UtcNow;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Nome { get => _nome; set => SetField(ref _nome, value); }
    public string EnderecoIp { get => _enderecoIp; set => SetField(ref _enderecoIp, value); }
    public int PortaTcp { get => _portaTcp; set => SetField(ref _portaTcp, value); }
    public bool Pareado { get => _pareado; set => SetField(ref _pareado, value); }
    public string? Token { get => _token; set => SetField(ref _token, value); }
    public StatusComputador Status { get => _status; set => SetField(ref _status, value); }
    public DateTime UltimaVezVisto { get => _ultimaVezVisto; set => SetField(ref _ultimaVezVisto, value); }
}
