using System.Text.Json.Serialization;

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
    private string? _apelido;
    private string _enderecoIp = string.Empty;
    private int _portaTcp;
    private bool _pareado;
    private string? _token;
    private StatusComputador _status = StatusComputador.Desconhecido;
    private DateTime _ultimaVezVisto = DateTime.UtcNow;
    private bool _emEdicao;

    public string Id { get => _id; set => SetField(ref _id, value); }

    /// <summary>Nome informado pela própria máquina (hostname).</summary>
    public string Nome
    {
        get => _nome;
        set
        {
            if (SetField(ref _nome, value))
            {
                OnPropertyChanged(nameof(NomeExibicao));
            }
        }
    }

    /// <summary>Nome dado pelo usuário para reconhecer a máquina ("PC da sala").
    /// Fica só neste painel; não é enviado pela rede nem altera o hostname.</summary>
    public string? Apelido
    {
        get => _apelido;
        set
        {
            if (SetField(ref _apelido, value))
            {
                OnPropertyChanged(nameof(NomeExibicao));
                OnPropertyChanged(nameof(TemApelido));
            }
        }
    }

    [JsonIgnore]
    public string NomeExibicao => string.IsNullOrWhiteSpace(Apelido) ? Nome : Apelido!;

    [JsonIgnore]
    public bool TemApelido => !string.IsNullOrWhiteSpace(Apelido);

    /// <summary>Só de UI: alterna o cartão entre exibir o nome e editá-lo.</summary>
    [JsonIgnore]
    public bool EmEdicao { get => _emEdicao; set => SetField(ref _emEdicao, value); }

    public string EnderecoIp { get => _enderecoIp; set => SetField(ref _enderecoIp, value); }
    public int PortaTcp { get => _portaTcp; set => SetField(ref _portaTcp, value); }
    public bool Pareado { get => _pareado; set => SetField(ref _pareado, value); }
    public string? Token { get => _token; set => SetField(ref _token, value); }
    public StatusComputador Status { get => _status; set => SetField(ref _status, value); }
    public DateTime UltimaVezVisto { get => _ultimaVezVisto; set => SetField(ref _ultimaVezVisto, value); }
}
