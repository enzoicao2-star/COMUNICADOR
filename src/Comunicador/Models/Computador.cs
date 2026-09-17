using System.Text.Json.Serialization;
using Comunicador.Protocol;

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
    private bool _temPainel;
    private List<MonitorInfo> _monitores = new();
    private string? _versaoReceptor;
    private bool _atualizandoReceptor;
    private double? _pingMs;
    private string? _versaoPainel;
    private bool _ehOwner;
    private List<BadgeUsuario> _badges = new();
    private string _novoBadgeTexto = "Destaque";
    private string _novoBadgeCor = "#4C8DFF";
    private string _novoBadgeEstilo = "Holográfica";
    private string _novoBadgeIcone = "Estrela";
    private string? _novoBadgeIconePersonalizadoBase64;
    private bool _novoBadgeBrilho = true;
    private bool _novoBadgeEfeitoMouse = true;

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

    /// <summary>Nome público definido pelo usuário e sincronizado entre os painéis.</summary>
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

    /// <summary>Distingue o Comunicador completo de uma instalação somente receptora.</summary>
    public bool TemPainel
    {
        get => _temPainel;
        set
        {
            if (SetField(ref _temPainel, value))
            {
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
            }
        }
    }
    public List<MonitorInfo> Monitores { get => _monitores; set => SetField(ref _monitores, value ?? new()); }

    public string? VersaoReceptor
    {
        get => _versaoReceptor;
        set
        {
            if (SetField(ref _versaoReceptor, value))
            {
                OnPropertyChanged(nameof(ReceptorAtualizado));
                OnPropertyChanged(nameof(StatusVersaoReceptor));
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
            }
        }
    }

    public string? VersaoPainel
    {
        get => _versaoPainel;
        set
        {
            if (SetField(ref _versaoPainel, value))
            {
                OnPropertyChanged(nameof(StatusVersaoPainel));
                OnPropertyChanged(nameof(PainelAtualizado));
            }
        }
    }

    public bool EhOwner { get => _ehOwner; set => SetField(ref _ehOwner, value); }
    public List<BadgeUsuario> Badges
    {
        get => _badges;
        set
        {
            var badges = value ?? new();
            foreach (var badge in badges) badge.ComputerId = Id;
            SetField(ref _badges, badges);
        }
    }

    [JsonIgnore]
    public bool PainelAtualizado => !TemPainel || VersaoPainel == ProtocolConstants.CurrentPanelVersion;

    [JsonIgnore]
    public string StatusVersaoPainel => !TemPainel
        ? string.Empty
        : string.IsNullOrWhiteSpace(VersaoPainel)
            ? "Painel antigo — versão desconhecida"
            : PainelAtualizado
                ? $"Painel {VersaoPainel} atualizado"
                : $"Painel {VersaoPainel} — atualização disponível";

    [JsonIgnore] public string NovoBadgeTexto { get => _novoBadgeTexto; set => SetField(ref _novoBadgeTexto, value); }
    [JsonIgnore] public string NovoBadgeCor { get => _novoBadgeCor; set => SetField(ref _novoBadgeCor, value); }
    [JsonIgnore] public string NovoBadgeEstilo { get => _novoBadgeEstilo; set => SetField(ref _novoBadgeEstilo, value); }
    [JsonIgnore] public string NovoBadgeIcone { get => _novoBadgeIcone; set => SetField(ref _novoBadgeIcone, value); }
    [JsonIgnore] public string? NovoBadgeIconePersonalizadoBase64
    {
        get => _novoBadgeIconePersonalizadoBase64;
        set => SetField(ref _novoBadgeIconePersonalizadoBase64, value);
    }
    [JsonIgnore] public bool NovoBadgeBrilho { get => _novoBadgeBrilho; set => SetField(ref _novoBadgeBrilho, value); }
    [JsonIgnore] public bool NovoBadgeEfeitoMouse { get => _novoBadgeEfeitoMouse; set => SetField(ref _novoBadgeEfeitoMouse, value); }

    [JsonIgnore]
    public bool AtualizandoReceptor
    {
        get => _atualizandoReceptor;
        set
        {
            if (SetField(ref _atualizandoReceptor, value))
            {
                OnPropertyChanged(nameof(StatusVersaoReceptor));
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
            }
        }
    }

    [JsonIgnore]
    public bool ReceptorAtualizado => VersaoReceptor == ProtocolConstants.CurrentReceiverVersion;

    [JsonIgnore]
    public string StatusVersaoReceptor => AtualizandoReceptor
        ? "Atualizando receptor..."
        : ReceptorAtualizado
        ? $"Receptor {VersaoReceptor} atualizado"
        : string.IsNullOrWhiteSpace(VersaoReceptor)
            ? "Receptor antigo — atualização necessária"
            : $"Receptor {VersaoReceptor} — atualização necessária";

    [JsonIgnore]
    public bool PodeAtualizarReceptor => !TemPainel && !ReceptorAtualizado && !AtualizandoReceptor && Pareado;

    public string EnderecoIp { get => _enderecoIp; set => SetField(ref _enderecoIp, value); }
    public int PortaTcp { get => _portaTcp; set => SetField(ref _portaTcp, value); }
    [JsonIgnore]
    public double? PingMs { get => _pingMs; set => SetField(ref _pingMs, value); }
    public bool Pareado
    {
        get => _pareado;
        set
        {
            if (SetField(ref _pareado, value))
            {
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
            }
        }
    }
    public string? Token { get => _token; set => SetField(ref _token, value); }
    public StatusComputador Status { get => _status; set => SetField(ref _status, value); }
    public DateTime UltimaVezVisto { get => _ultimaVezVisto; set => SetField(ref _ultimaVezVisto, value); }
}
