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
    private bool _registradoNaNuvem;
    private bool _midiasBloqueadas;
    private List<MonitorInfo> _monitores = new();
    private string? _versaoReceptor;
    private bool _atualizandoReceptor;
    private int _progressoAtualizacaoReceptor;
    private string _etapaAtualizacaoReceptor = string.Empty;
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
    private bool _novoBadgeAnimacaoFlutuante;
    private bool _podeEditarPerfil;
    private bool _podeEditarNome;
    private string? _badgeEmEdicaoId;
    private string? _novoBadgeRoleId;
    private bool _reduzirMovimento;
    private bool _podeGerenciarAdmin;
    private bool _podeAdministrarRemotamente;
    private bool _podeUsarCmdRemoto;
    private List<string> _permissoesIndividuais = [];
    private bool _esteComputador;

    public string Id { get => _id; set => SetField(ref _id, value); }

    /// <summary>Identificadores antigos deste mesmo PC, guardados ao consolidar
    /// uma instalação que passou de receptor para painel.</summary>
    public List<string> LegacyDeviceIds { get; set; } = [];

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
                OnPropertyChanged(nameof(StatusVersaoPainel));
                OnPropertyChanged(nameof(StatusVersoes));
                OnPropertyChanged(nameof(TodasVersoesAtualizadas));
                OnPropertyChanged(nameof(TextoTipoInstalacao));
            }
        }
    }
    /// <summary>Indica que o receptor tem identidade registrada e pode receber entregas pela nuvem.</summary>
    public bool RegistradoNaNuvem
    {
        get => _registradoNaNuvem;
        set
        {
            if (SetField(ref _registradoNaNuvem, value))
            {
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
                OnPropertyChanged(nameof(RegistradoNaNuvem));
            }
        }
    }
    public bool MidiasBloqueadas
    {
        get => _midiasBloqueadas;
        set { if (SetField(ref _midiasBloqueadas, value)) OnPropertyChanged(nameof(StatusRecebimentoMidias)); }
    }
    [JsonIgnore] public string StatusRecebimentoMidias => MidiasBloqueadas
        ? "Mídias bloqueadas neste computador" : "Mídias permitidas neste computador";
    public List<MonitorInfo> Monitores
    {
        get => _monitores;
        set
        {
            if (SetField(ref _monitores, value ?? new()))
                OnPropertyChanged(nameof(QuantidadeMonitores));
        }
    }

    [JsonIgnore]
    public int QuantidadeMonitores => Monitores.Count;

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
                OnPropertyChanged(nameof(StatusVersoes));
                OnPropertyChanged(nameof(TodasVersoesAtualizadas));
                OnPropertyChanged(nameof(TemReceptor));
                OnPropertyChanged(nameof(TextoTipoInstalacao));
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
                OnPropertyChanged(nameof(StatusVersoes));
                OnPropertyChanged(nameof(TodasVersoesAtualizadas));
            }
        }
    }

    public bool EhOwner
    {
        get => _ehOwner;
        set
        {
            if (SetField(ref _ehOwner, value)) OnPropertyChanged(nameof(ExibirOwnerPadrao));
        }
    }
    public List<BadgeUsuario> Badges
    {
        get => _badges;
        set
        {
            var badges = value ?? new();
            // A nuvem consulta periodicamente os mesmos perfis. Preservar as
            // instâncias evita recriar controles/animações e piscar as badges.
            if (_badges.Count == badges.Count
                && _badges.Zip(badges).All(pair => MesmaBadge(pair.First, pair.Second))) return;
            foreach (var badge in badges) badge.ComputerId = Id;
            if (SetField(ref _badges, badges))
            {
                OnPropertyChanged(nameof(TemBadgeOwner));
                OnPropertyChanged(nameof(ExibirOwnerPadrao));
                OnPropertyChanged(nameof(EhAdminDelegado));
                OnPropertyChanged(nameof(TextoAcaoAdmin));
            }
        }
    }

    [JsonIgnore]
    public List<string> PermissoesIndividuais
    {
        get => _permissoesIndividuais;
        set
        {
            var atual = value ?? [];
            if (SetField(ref _permissoesIndividuais, atual))
            {
                OnPropertyChanged(nameof(TemPermissoesIndividuais));
                OnPropertyChanged(nameof(PermissaoIndividualEnviarMidias));
                OnPropertyChanged(nameof(PermissaoIndividualAlterarPapelParede));
                OnPropertyChanged(nameof(PermissaoIndividualCmdRemoto));
                OnPropertyChanged(nameof(PermissaoIndividualGerenciarPerfil));
                OnPropertyChanged(nameof(PermissaoIndividualGerenciarBadges));
                OnPropertyChanged(nameof(PermissaoIndividualInstalarPainel));
                OnPropertyChanged(nameof(PermissaoIndividualControlarAcesso));
                OnPropertyChanged(nameof(PermissaoIndividualAtualizarReceptor));
            }
        }
    }
    [JsonIgnore] public bool TemPermissoesIndividuais => PermissoesIndividuais.Count > 0;
    [JsonIgnore] public bool PermissaoIndividualEnviarMidias { get => TemPermissao("send_media"); set => DefinirPermissao("send_media", value); }
    [JsonIgnore] public bool PermissaoIndividualAlterarPapelParede { get => TemPermissao("change_wallpaper"); set => DefinirPermissao("change_wallpaper", value); }
    [JsonIgnore] public bool PermissaoIndividualCmdRemoto { get => TemPermissao("remote_command"); set => DefinirPermissao("remote_command", value); }
    [JsonIgnore] public bool PermissaoIndividualGerenciarPerfil { get => TemPermissao("manage_profiles"); set => DefinirPermissao("manage_profiles", value); }
    [JsonIgnore] public bool PermissaoIndividualGerenciarBadges { get => TemPermissao("manage_badges"); set => DefinirPermissao("manage_badges", value); }
    [JsonIgnore] public bool PermissaoIndividualInstalarPainel { get => TemPermissao("remote_install"); set => DefinirPermissao("remote_install", value); }
    [JsonIgnore] public bool PermissaoIndividualControlarAcesso { get => TemPermissao("remote_panel_access"); set => DefinirPermissao("remote_panel_access", value); }
    [JsonIgnore] public bool PermissaoIndividualAtualizarReceptor { get => TemPermissao("remote_receiver"); set => DefinirPermissao("remote_receiver", value); }

    private bool TemPermissao(string permission) => PermissoesIndividuais.Contains(permission, StringComparer.Ordinal);
    private void DefinirPermissao(string permission, bool ativa)
    {
        var updated = PermissoesIndividuais.Where(p => p != permission).ToList();
        if (ativa) updated.Add(permission);
        PermissoesIndividuais = updated;
    }

    private static bool MesmaBadge(BadgeUsuario a, BadgeUsuario b) =>
        a.Id == b.Id && a.Texto == b.Texto && a.Cor == b.Cor
        && a.Estilo == b.Estilo && a.Icone == b.Icone
        && a.IconePersonalizadoBase64 == b.IconePersonalizadoBase64
        && a.Brilho == b.Brilho && a.EfeitoMouse == b.EfeitoMouse
        && a.AnimacaoFlutuante == b.AnimacaoFlutuante && a.RoleId == b.RoleId;

    [JsonIgnore] public bool TemBadgeOwner => Badges.Any(b => b.Id == "owner");
    [JsonIgnore] public bool EhAdminDelegado => Badges.Any(b => b.Id == "admin");
    [JsonIgnore] public bool ExibirOwnerPadrao => EhOwner && !TemBadgeOwner;
    [JsonIgnore] public bool PodeEditarPerfil { get => _podeEditarPerfil; set => SetField(ref _podeEditarPerfil, value); }
    [JsonIgnore] public bool PodeEditarNome { get => _podeEditarNome; set => SetField(ref _podeEditarNome, value); }
    [JsonIgnore] public bool PodeGerenciarAdmin { get => _podeGerenciarAdmin; set => SetField(ref _podeGerenciarAdmin, value); }
    [JsonIgnore] public bool PodeAdministrarRemotamente { get => _podeAdministrarRemotamente; set => SetField(ref _podeAdministrarRemotamente, value); }
    [JsonIgnore] public bool PodeUsarCmdRemoto { get => _podeUsarCmdRemoto; set => SetField(ref _podeUsarCmdRemoto, value); }
    [JsonIgnore] public string TextoAcaoAdmin => EhAdminDelegado ? "Remover admin" : "Conceder admin";
    [JsonIgnore] public bool ReduzirMovimento { get => _reduzirMovimento; set => SetField(ref _reduzirMovimento, value); }
    [JsonIgnore]
    public string? BadgeEmEdicaoId
    {
        get => _badgeEmEdicaoId;
        set
        {
            if (SetField(ref _badgeEmEdicaoId, value))
            {
                OnPropertyChanged(nameof(EditandoBadge));
                OnPropertyChanged(nameof(AcaoBadgeTexto));
            }
        }
    }
    [JsonIgnore] public bool EditandoBadge => !string.IsNullOrWhiteSpace(BadgeEmEdicaoId);
    [JsonIgnore] public string AcaoBadgeTexto => EditandoBadge ? "Salvar alterações" : "Salvar badge";
    [JsonIgnore] public string? NovoBadgeRoleId { get => _novoBadgeRoleId; set => SetField(ref _novoBadgeRoleId, value); }

    [JsonIgnore]
    public bool PainelAtualizado => !TemPainel || VersaoPainel == ProtocolConstants.CurrentPanelVersion;

    [JsonIgnore]
    public string StatusVersaoPainel => !TemPainel
        ? "Somente receptor — painel não instalado"
        : string.IsNullOrWhiteSpace(VersaoPainel)
            ? "Painel antigo — versão desconhecida"
            : PainelAtualizado
                ? $"Painel {VersaoPainel} atualizado"
                : $"Painel {VersaoPainel} — atualização disponível";

    [JsonIgnore]
    public string StatusVersoes => TemPainel
        ? string.Join(" · ", new[] { StatusVersaoPainel, TemReceptor ? StatusVersaoReceptor : "Receptor não instalado" })
        : StatusVersaoReceptor;

    [JsonIgnore]
    public bool TemReceptor => !string.IsNullOrWhiteSpace(VersaoReceptor);

    [JsonIgnore]
    public string TextoTipoInstalacao => TemReceptor ? "PAINEL + RECEPTOR" : "PAINEL";

    [JsonIgnore]
    public bool TodasVersoesAtualizadas => PainelAtualizado && ReceptorAtualizado;

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
    [JsonIgnore] public bool NovoBadgeAnimacaoFlutuante { get => _novoBadgeAnimacaoFlutuante; set => SetField(ref _novoBadgeAnimacaoFlutuante, value); }

    [JsonIgnore]
    public bool AtualizandoReceptor
    {
        get => _atualizandoReceptor;
        set
        {
            if (SetField(ref _atualizandoReceptor, value))
            {
                OnPropertyChanged(nameof(StatusVersaoReceptor));
                OnPropertyChanged(nameof(StatusVersoes));
                OnPropertyChanged(nameof(PodeAtualizarReceptor));
            }
        }
    }

    [JsonIgnore]
    public int ProgressoAtualizacaoReceptor
    {
        get => _progressoAtualizacaoReceptor;
        set
        {
            if (SetField(ref _progressoAtualizacaoReceptor, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(StatusVersaoReceptor));
                OnPropertyChanged(nameof(StatusVersoes));
            }
        }
    }

    [JsonIgnore]
    public string EtapaAtualizacaoReceptor
    {
        get => _etapaAtualizacaoReceptor;
        set
        {
            if (SetField(ref _etapaAtualizacaoReceptor, value))
            {
                OnPropertyChanged(nameof(StatusVersaoReceptor));
                OnPropertyChanged(nameof(StatusVersoes));
            }
        }
    }

    [JsonIgnore]
    public bool ReceptorAtualizado => VersaoReceptor == ProtocolConstants.CurrentReceiverVersion;

    [JsonIgnore]
    public string StatusVersaoReceptor => AtualizandoReceptor
        ? $"{EtapaAtualizacaoReceptor} ({ProgressoAtualizacaoReceptor}%)"
        : ReceptorAtualizado
        ? $"Receptor {VersaoReceptor} atualizado"
        : string.IsNullOrWhiteSpace(VersaoReceptor)
            ? "Receptor antigo — atualização necessária"
            : $"Receptor {VersaoReceptor} — atualização necessária";

    [JsonIgnore]
    public bool PodeAtualizarReceptor => TemReceptor && !AtualizandoReceptor && (Pareado || RegistradoNaNuvem);

    public string EnderecoIp
    {
        get => _enderecoIp;
        set { if (SetField(ref _enderecoIp, value)) OnPropertyChanged(nameof(EnderecoIpExibicao)); }
    }
    [JsonIgnore] public string EnderecoIpExibicao => string.IsNullOrWhiteSpace(EnderecoIp)
        ? "registrado na nuvem" : EnderecoIp;
    public int PortaTcp { get => _portaTcp; set => SetField(ref _portaTcp, value); }
    [JsonIgnore]
    public double? PingMs
    {
        get => _pingMs;
        set
        {
            var textoAnterior = PingTexto;
            if (SetField(ref _pingMs, value) && textoAnterior != PingTexto)
                OnPropertyChanged(nameof(PingTexto));
        }
    }
    [JsonIgnore] public string PingTexto => Status == StatusComputador.Online && PingMs is { } ping
        ? $"{ping:0} ms" : "— ms";
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
    [JsonIgnore]
    public bool EsteComputador { get => _esteComputador; set => SetField(ref _esteComputador, value); }
    public StatusComputador Status
    {
        get => _status;
        set
        {
            var textoAnterior = PingTexto;
            if (SetField(ref _status, value) && textoAnterior != PingTexto)
                OnPropertyChanged(nameof(PingTexto));
        }
    }
    public DateTime UltimaVezVisto { get => _ultimaVezVisto; set => SetField(ref _ultimaVezVisto, value); }
}
