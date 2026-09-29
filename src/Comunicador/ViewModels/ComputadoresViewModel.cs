using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Microsoft.Win32;

namespace Comunicador.ViewModels;

public sealed class ComputadoresViewModel : ViewModelBase
{
    public event Action<double?>? PingMedioAtualizado;
    private readonly JsonStore<Computador> _store;
    private readonly DiscoveryService _discovery;
    private readonly ReceptorClient _client;
    private readonly AtualizadorReceptor _atualizador;
    private readonly AppSettings _settings;
    private readonly PerfilComputadorRepository _perfis;
    private readonly CloudSyncService _cloud;
    private readonly ConcurrentDictionary<string, ReceiverCloudUpdateConfirmation> _cloudReceiverUpdates =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _statusMensagem;
    private string _novoIp = string.Empty;
    private string _novaPorta = ProtocolConstants.TcpPort.ToString();
    private Computador? _computadorGerenciado;
    private string _comandoRemoto = string.Empty;
    private string _resultadoComandoRemoto = "Digite um comando e clique em Executar.";
    private string? _comandoRemotoRequestId;
    private string? _comandoRemotoTargetId;
    private bool _autoridadeAplicada;
    private bool _ultimoIsAdmin;
    private string? _ultimoAdminDeviceId;
    private long _ultimaConfiguracaoGlobalRevision;

    public ObservableCollection<Computador> Computadores { get; } = new();

    public Computador? ComputadorGerenciado
    {
        get => _computadorGerenciado;
        set
        {
            if (SetField(ref _computadorGerenciado, value))
            {
                ComandoRemoto = string.Empty;
                ResultadoComandoRemoto = "Digite um comando e clique em Executar.";
            }
        }
    }

    public bool PodeGerenciarGlobalmente => _cloud.IsAdmin;

    public string ComandoRemoto
    {
        get => _comandoRemoto;
        set => SetField(ref _comandoRemoto, value);
    }

    public string ResultadoComandoRemoto
    {
        get => _resultadoComandoRemoto;
        set => SetField(ref _resultadoComandoRemoto, value);
    }

    public string? StatusMensagem
    {
        get => _statusMensagem;
        set => SetField(ref _statusMensagem, value);
    }

    public string NovoIp
    {
        get => _novoIp;
        set => SetField(ref _novoIp, value);
    }

    public string NovaPorta
    {
        get => _novaPorta;
        set => SetField(ref _novaPorta, value);
    }

    public ICommand PairearCommand { get; }
    public ICommand RemoverCommand { get; }
    public ICommand AtualizarAgoraCommand { get; }
    public ICommand AdicionarManualCommand { get; }
    public ICommand RenomearCommand { get; }
    public ICommand ConfirmarRenomeCommand { get; }
    public ICommand AtualizarReceptorCommand { get; }
    public ICommand AdicionarBadgeCommand { get; }
    public ICommand EditarBadgeCommand { get; }
    public ICommand NovaBadgeCommand { get; }
    public ICommand RemoverBadgeCommand { get; }
    public ICommand CarregarIconeBadgeCommand { get; }
    public ICommand SalvarPerfilCommand { get; }
    public ICommand FecharEdicaoCommand { get; }
    public ICommand AlternarAdminDelegadoCommand { get; }
    public ICommand AtribuirTagAdminCommand { get; }
    public ICommand RemoverTagAdminCommand { get; }
    public ICommand InstalarPainelRemotoCommand { get; }
    public ICommand ReinstalarPainelRemotoCommand { get; }
    public ICommand BloquearPainelRemotoCommand { get; }
    public ICommand HabilitarPainelRemotoCommand { get; }
    public ICommand EnviarCmdRemotoCommand { get; }

    public IReadOnlyList<string> EstilosBadge { get; } = ["Holográfica", "Metal", "Pílula", "Contorno", "Selo"];
    public IReadOnlyList<string> IconesBadge { get; } =
        ["Coroa", "Estrela", "Escudo", "Raio", "Diamante", "Fogo", "Coração", "Usuário", "Código", "Música", "Jogo", "Casa", "Medalha", "Chave", "Globo"];
    public IReadOnlyList<ModeloBadgeGlobal> ModelosBadgeGlobal => _cloud.GlobalConfig?.ModelosBadge ?? [];

    public ComputadoresViewModel(
        JsonStore<Computador> store, DiscoveryService discovery, ReceptorClient client,
        AtualizadorReceptor atualizador, AppSettings settings,
        PerfilComputadorRepository perfis, CloudSyncService cloud)
    {
        _store = store;
        _discovery = discovery;
        _client = client;
        _atualizador = atualizador;
        _settings = settings;
        _perfis = perfis;
        _cloud = cloud;
        _cloud.ResponseReceived += OnRemoteCommandResponse;
        _cloud.DevicesReceived += OnCloudDevicesReceived;
        _novaPorta = settings.PortaTcp.ToString();

        foreach (var computador in _store.Load())
        {
            if (computador.Monitores.Count == 0)
            {
                computador.Monitores = MonitoresOuPadrao(null);
            }
            AplicarPerfil(computador);
            Computadores.Add(computador);
        }

        _perfis.Alterado += AplicarPerfis;
        _cloud.StateChanged += AplicarAutoridadeCloud;

        InstalarPainelRemotoCommand = new AsyncRelayCommand(param => EnviarComandoAdminAsync(param, "install_panel"),
            param => PodeEnviarComandoAdmin(param, "install_panel"));
        ReinstalarPainelRemotoCommand = new AsyncRelayCommand(param => EnviarComandoAdminAsync(param, "reinstall_panel"),
            param => PodeEnviarComandoAdmin(param, "reinstall_panel"));
        BloquearPainelRemotoCommand = new AsyncRelayCommand(param => EnviarComandoAdminAsync(param, "disable_panel"),
            param => PodeEnviarComandoAdmin(param, "disable_panel"));
        HabilitarPainelRemotoCommand = new AsyncRelayCommand(param => EnviarComandoAdminAsync(param, "enable_panel"),
            param => PodeEnviarComandoAdmin(param, "enable_panel"));
        EnviarCmdRemotoCommand = new AsyncRelayCommand(EnviarCmdRemotoAsync,
            param => _cloud.IsAdmin && param is Computador computador
                && !string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase));

        _discovery.ReceptorDescoberto += OnReceptorDescoberto;

        PairearCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador)
            {
                await PairearAsync(computador).ConfigureAwait(true);
            }
        });

        RemoverCommand = new RelayCommand(param =>
        {
            if (param is Computador computador)
            {
                Computadores.Remove(computador);
                Persist();
                PublicarPingMedio();
            }
        });

        AtualizarAgoraCommand = new AsyncRelayCommand(ProcurarAsync);

        AtualizarReceptorCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador)
            {
                await AtualizarReceptorAsync(computador).ConfigureAwait(true);
            }
        }, param => _cloud.HasPermission("remote_receiver")
            && param is Computador { PodeAtualizarReceptor: true });

        AdicionarManualCommand = new RelayCommand(_ => AdicionarManual(), _ => PodeAdicionarManual());

        RenomearCommand = new RelayCommand(param =>
        {
            if (param is Computador computador && PodeEditar(computador))
            {
                // um de cada vez, para nao ficar varios cartoes em edicao
                foreach (var outro in Computadores)
                {
                    outro.EmEdicao = false;
                }

                computador.EmEdicao = true;
            }
        }, param => param is Computador computador && PodeEditar(computador));

        ConfirmarRenomeCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador && PodeEditar(computador))
            {
                await SalvarPerfilAsync(computador).ConfigureAwait(true);
                computador.EmEdicao = false;
            }
        }, param => param is Computador computador && PodeEditar(computador));

        AdicionarBadgeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador) SalvarBadge(computador);
        });
        EditarBadgeCommand = new RelayCommand(param =>
        {
            if (param is BadgeUsuario badge) CarregarBadgeNoEditor(badge);
        });
        NovaBadgeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador && PodeEditar(computador)) ResetarEditorBadge(computador);
        });
        RemoverBadgeCommand = new RelayCommand(param =>
        {
            if (param is not BadgeUsuario badge) return;
            var computador = Computadores.FirstOrDefault(c => c.Id == badge.ComputerId || c.Badges.Contains(badge));
            if (computador is null || !PodeEditar(computador)) return;
            if ((badge.Id is "owner" or "admin" || badge.RoleId is not null) && !_cloud.IsAdmin)
            {
                StatusMensagem = "Somente o OWNER pode remover badges reservadas.";
                return;
            }
            computador.Badges = computador.Badges.Where(b => b.Id != badge.Id).ToList();
            if (computador.BadgeEmEdicaoId == badge.Id) ResetarEditorBadge(computador);
            StatusMensagem = $"Badge '{badge.Texto}' removida da prévia. Clique em Aplicar mudanças para sincronizar.";
        });
        CarregarIconeBadgeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador && PodeEditar(computador)) CarregarIconePersonalizado(computador);
        });
        SalvarPerfilCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador && PodeEditar(computador))
            {
                await SalvarPerfilAsync(computador).ConfigureAwait(true);
                computador.EmEdicao = false;
            }
        }, param => param is Computador computador && PodeEditar(computador));
        FecharEdicaoCommand = new RelayCommand(param =>
        {
            if (param is not Computador computador || !PodeEditar(computador)) return;
            AplicarPerfil(computador);
            ResetarEditorBadge(computador);
            computador.EmEdicao = false;
            StatusMensagem = "Edição fechada. Alterações ainda não aplicadas foram descartadas.";
        });
        AlternarAdminDelegadoCommand = new RelayCommand(param =>
        {
            if (!_cloud.IsAdmin || param is not Computador computador || computador.EhOwner) return;
            if (computador.EhAdminDelegado)
            {
                computador.Badges = computador.Badges.Where(b => b.Id != "admin").ToList();
                if (computador.BadgeEmEdicaoId == "admin") ResetarEditorBadge(computador);
                StatusMensagem = $"Permissão de admin removida de {computador.NomeExibicao}. Clique em Aplicar mudanças.";
            }
            else
            {
                if (computador.Badges.Count >= ProtocolConstants.MaxBadgesPerComputer)
                    computador.Badges = computador.Badges.Where(b => b.Id is "owner" or "admin")
                        .Concat(computador.Badges.Where(b => b.Id is not ("owner" or "admin")).Take(3)).ToList();
                computador.Badges = computador.Badges.Append(CriarBadgeAdmin(computador.Id)).ToList();
                CarregarBadgeNoEditor(computador.Badges.First(b => b.Id == "admin"));
                StatusMensagem = $"Admin concedido a {computador.NomeExibicao}. Personalize a tag e clique em Aplicar mudanças.";
            }
        }, param => _cloud.IsAdmin && param is Computador { EhOwner: false });
        AtribuirTagAdminCommand = new AsyncRelayCommand(async param =>
        {
            if (!_cloud.IsAdmin || param is not Computador computador || computador.EhOwner) return;
            var model = ModelosBadgeGlobal.FirstOrDefault(m => m.Id == computador.NovoBadgeRoleId);
            if (model is null) { StatusMensagem = "Selecione uma tag padrão válida."; return; }
            var badges = computador.Badges.Where(b => b.RoleId != model.Id).ToList();
            if (badges.Count >= ProtocolConstants.MaxBadgesPerComputer)
            {
                StatusMensagem = "Este computador já tem quatro badges. Remova uma antes de atribuir a tag.";
                return;
            }
            badges.Add(new BadgeUsuario
            {
                Id = Guid.NewGuid().ToString("N"), ComputerId = computador.Id, RoleId = model.Id,
                Texto = model.Texto, Cor = model.Cor, Icone = model.Icone, Estilo = model.Estilo,
                Brilho = model.Brilho, EfeitoMouse = model.EfeitoMouse,
                AnimacaoFlutuante = model.AnimacaoFlutuante,
            });
            computador.Badges = badges;
            await SalvarPerfilAsync(computador).ConfigureAwait(true);
        }, param => _cloud.IsAdmin && param is Computador { EhOwner: false });
        RemoverTagAdminCommand = new AsyncRelayCommand(async param =>
        {
            if (!_cloud.IsAdmin || param is not Computador computador || computador.EhOwner
                || string.IsNullOrWhiteSpace(computador.NovoBadgeRoleId)) return;
            var badges = computador.Badges.Where(b => b.RoleId != computador.NovoBadgeRoleId).ToList();
            if (badges.Count == computador.Badges.Count) return;
            computador.Badges = badges;
            await SalvarPerfilAsync(computador).ConfigureAwait(true);
        }, param => _cloud.IsAdmin && param is Computador { EhOwner: false });
    }

    private void SalvarBadge(Computador computador)
    {
        if (!PodeEditar(computador))
        {
            StatusMensagem = "Somente o próprio painel pode alterar suas badges.";
            return;
        }
        var existente = string.IsNullOrWhiteSpace(computador.BadgeEmEdicaoId)
            ? null
            : computador.Badges.FirstOrDefault(b => b.Id == computador.BadgeEmEdicaoId);
        if ((existente?.Id is "owner" or "admin" || existente?.RoleId is not null) && !_cloud.IsAdmin)
        {
            StatusMensagem = "Somente o OWNER pode modificar badges reservadas.";
            return;
        }
        if (existente is null && computador.Badges.Count >= ProtocolConstants.MaxBadgesPerComputer)
        {
            StatusMensagem = "Cada computador pode ter até 4 badges.";
            return;
        }
        var texto = computador.NovoBadgeTexto.Trim();
        if (string.IsNullOrWhiteSpace(texto)) texto = "Badge";
        if (texto.Length > ProtocolConstants.MaxBadgeTextLength) texto = texto[..ProtocolConstants.MaxBadgeTextLength];
        if (!ThemeService.TryNormalizeColor(computador.NovoBadgeCor, out var cor))
        {
            StatusMensagem = "Cor inválida. Use uma cor como #4C8DFF.";
            return;
        }
        var badge = new BadgeUsuario
        {
            Id = existente?.Id ?? Guid.NewGuid().ToString("N"),
            ComputerId = computador.Id,
            Texto = texto,
            Cor = cor,
            Estilo = EstilosBadge.Contains(computador.NovoBadgeEstilo) ? computador.NovoBadgeEstilo : "Holográfica",
            Icone = IconesBadge.Contains(computador.NovoBadgeIcone) ? computador.NovoBadgeIcone : "Estrela",
            IconePersonalizadoBase64 = computador.NovoBadgeIconePersonalizadoBase64,
            Brilho = computador.NovoBadgeBrilho,
            EfeitoMouse = computador.NovoBadgeEfeitoMouse,
            AnimacaoFlutuante = computador.NovoBadgeAnimacaoFlutuante,
            RoleId = _cloud.IsAdmin ? existente?.RoleId : null,
        };
        computador.Badges = existente is null
            ? computador.Badges.Append(badge).ToList()
            : computador.Badges.Select(b => b.Id == existente.Id ? badge : b).ToList();
        StatusMensagem = existente is null
            ? $"Badge '{badge.Texto}' adicionada à prévia. Clique em Aplicar mudanças para sincronizar."
            : $"Badge '{badge.Texto}' atualizada na prévia. Clique em Aplicar mudanças para sincronizar.";
        ResetarEditorBadge(computador);
    }

    private void CarregarBadgeNoEditor(BadgeUsuario badge)
    {
        var computador = Computadores.FirstOrDefault(c => c.Id == badge.ComputerId || c.Badges.Contains(badge));
        if (computador is null || !PodeEditar(computador)) return;
        if ((badge.Id is "owner" or "admin" || badge.RoleId is not null) && !_cloud.IsAdmin)
        {
            StatusMensagem = "Somente o OWNER pode modificar esta badge.";
            return;
        }
        computador.BadgeEmEdicaoId = badge.Id;
        computador.NovoBadgeTexto = badge.Texto;
        computador.NovoBadgeCor = badge.Cor;
        computador.NovoBadgeEstilo = badge.Estilo;
        computador.NovoBadgeIcone = badge.Icone;
        computador.NovoBadgeIconePersonalizadoBase64 = badge.IconePersonalizadoBase64;
        computador.NovoBadgeBrilho = badge.Brilho;
        computador.NovoBadgeEfeitoMouse = badge.EfeitoMouse;
        computador.NovoBadgeAnimacaoFlutuante = badge.AnimacaoFlutuante;
        computador.NovoBadgeRoleId = badge.RoleId;
        StatusMensagem = $"Editando a badge '{badge.Texto}'.";
    }

    private static void ResetarEditorBadge(Computador computador)
    {
        computador.BadgeEmEdicaoId = null;
        computador.NovoBadgeTexto = "Destaque";
        computador.NovoBadgeCor = "#4C8DFF";
        computador.NovoBadgeEstilo = "Holográfica";
        computador.NovoBadgeIcone = "Estrela";
        computador.NovoBadgeIconePersonalizadoBase64 = null;
        computador.NovoBadgeBrilho = true;
        computador.NovoBadgeEfeitoMouse = true;
        computador.NovoBadgeAnimacaoFlutuante = false;
        computador.NovoBadgeRoleId = null;
    }

    private void CarregarIconePersonalizado(Computador computador)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher ícone personalizado para a badge",
            Filter = "Imagens|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length == 0 || bytes.Length > ProtocolConstants.MaxCustomBadgeIconBytes)
            {
                StatusMensagem = "O ícone personalizado precisa ter no máximo 64 KB.";
                return;
            }
            var png = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var jpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            if (!png && !jpeg)
            {
                StatusMensagem = "Ícone inválido. Escolha PNG ou JPEG.";
                return;
            }
            computador.NovoBadgeIconePersonalizadoBase64 = Convert.ToBase64String(bytes);
            StatusMensagem = $"Ícone {Path.GetFileName(dialog.FileName)} carregado para a próxima badge.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMensagem = $"Não foi possível carregar o ícone: {ex.Message}";
        }
    }

    private async Task SalvarPerfilAsync(Computador computador)
    {
        if (!PodeEditar(computador))
        {
            StatusMensagem = "Somente o próprio computador ou o administrador pode alterar este perfil.";
            return;
        }
        var nome = !computador.PodeEditarNome
            ? _perfis.Obter(computador.Id)?.NomePublico ?? computador.Nome
            : string.IsNullOrWhiteSpace(computador.Apelido) ? computador.Nome : computador.Apelido!;
        var ehAdminAlvo = string.Equals(computador.Id, _cloud.AdminDeviceId, StringComparison.OrdinalIgnoreCase);
        var badges = computador.Badges.Where(b => b.Id != "owner").ToList();
        if (ehAdminAlvo)
        {
            var owner = computador.Badges.FirstOrDefault(b => b.Id == "owner") ?? CriarBadgeOwner(computador.Id);
            badges.Insert(0, owner);
        }
        computador.Badges = badges.Take(ProtocolConstants.MaxBadgesPerComputer).ToList();
        computador.EhOwner = ehAdminAlvo;
        _perfis.Salvar(computador.Id, nome, ehAdminAlvo, computador.Badges,
            _settings.PainelId, adminOverride: _cloud.IsAdmin);
        Persist();
        try
        {
            await _cloud.SaveProfileAsync(computador).ConfigureAwait(true);
            StatusMensagem = $"Nome e badges de {computador.NomeExibicao} sincronizados com todos os painéis.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            StatusMensagem = $"Salvo neste painel; sincronização pendente: {ex.Message}";
        }
    }

    private static BadgeUsuario CriarBadgeOwner(string computerId) => new()
    {
        Id = "owner", ComputerId = computerId, Texto = "OWNER", Cor = "#F2B84B",
        Estilo = "Holográfica", Icone = "Coroa", Brilho = true, EfeitoMouse = true,
    };

    private static BadgeUsuario CriarBadgeAdmin(string computerId) => new()
    {
        Id = "admin", ComputerId = computerId, Texto = "ADMIN", Cor = "#6C63FF",
        Estilo = "Holográfica", Icone = "Escudo", Brilho = true, EfeitoMouse = true,
        AnimacaoFlutuante = true,
    };

    private void AplicarPerfis() => UiDispatcher.Invoke(() =>
    {
        foreach (var computador in Computadores) AplicarPerfil(computador);
        Persist();
    });

    private void AplicarPerfil(Computador computador)
    {
        computador.PodeEditarPerfil = PodeEditar(computador);
        computador.PodeEditarNome = computador.PodeEditarPerfil
            && (_cloud.IsAdmin
                || string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
                || _cloud.HasPermission("manage_profiles"));
        computador.ReduzirMovimento = _settings.ReduzirMovimento;
        var ehAdmin = string.Equals(computador.Id, _cloud.AdminDeviceId, StringComparison.OrdinalIgnoreCase);
        computador.EhOwner = ehAdmin;
        computador.PodeGerenciarAdmin = _cloud.IsAdmin && !ehAdmin;
        computador.PodeUsarCmdRemoto = _cloud.IsAdmin
            && !string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase);
        computador.PodeAdministrarRemotamente = !_cloud.IsAdmin
            ? new[] { "remote_install", "remote_panel_access", "remote_receiver" }.Any(_cloud.HasPermission)
            : !ehAdmin;
        var perfil = _perfis.Obter(computador.Id);
        if (perfil is null)
        {
            computador.Apelido = null;
            var atuais = computador.Badges.Where(b => b.Id != "owner").ToList();
            if (ehAdmin) atuais.Insert(0, CriarBadgeOwner(computador.Id));
            computador.Badges = atuais.Take(ProtocolConstants.MaxBadgesPerComputer).ToList();
            return;
        }
        computador.Apelido = string.IsNullOrWhiteSpace(perfil.NomePublico) ? null : perfil.NomePublico;
        var badges = perfil.Badges.Where(b => b.Id != "owner").Select(b =>
        {
            var clone = b.Clone();
            clone.ComputerId = computador.Id;
            var role = _cloud.GlobalConfig?.ModelosBadge.FirstOrDefault(model => model.Id == clone.RoleId);
            if (role is not null)
            {
                clone.Texto = role.Texto;
                clone.Cor = role.Cor;
                clone.Icone = role.Icone;
                clone.Estilo = role.Estilo;
                clone.Brilho = role.Brilho;
                clone.EfeitoMouse = role.EfeitoMouse;
                clone.AnimacaoFlutuante = role.AnimacaoFlutuante;
            }
            return clone;
        }).ToList();
        if (ehAdmin)
        {
            var owner = perfil.Badges.FirstOrDefault(b => b.Id == "owner")?.Clone()
                ?? CriarBadgeOwner(computador.Id);
            owner.ComputerId = computador.Id;
            badges.Insert(0, owner);
        }
        computador.Badges = badges.Take(ProtocolConstants.MaxBadgesPerComputer).ToList();
    }

    private bool PodeEnviarComandoAdmin(object? param, string command) =>
        param is Computador computador
        && !string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
        && command switch
        {
            "install_panel" => _cloud.HasPermission("remote_install") && !computador.TemPainel && computador.Pareado,
            "reinstall_panel" => _cloud.HasPermission("remote_install") && computador.TemPainel,
            "disable_panel" or "enable_panel" => _cloud.HasPermission("remote_panel_access") && computador.TemPainel,
            "reinstall_receiver" => _cloud.HasPermission("remote_receiver")
                && !computador.TemPainel && (computador.Pareado || computador.RegistradoNaNuvem),
            _ => false,
        };

    private async Task EnviarComandoAdminAsync(object? param, string command)
    {
        if (!PodeEnviarComandoAdmin(param, command) || param is not Computador computador)
        {
            StatusMensagem = "Esta ação está disponível somente ao OWNER e para o tipo de computador compatível.";
            return;
        }
        var acao = command switch
        {
            "install_panel" => "Instalação do painel",
            "reinstall_panel" => "Reinstalação do painel",
            "disable_panel" => "Bloqueio de entrada do painel",
            "enable_panel" => "Liberação de entrada do painel",
            _ => "Ação remota",
        };
        try
        {
            await _cloud.QueueAdminCommandAsync(computador.Id, command).ConfigureAwait(true);
            StatusMensagem = $"{acao} enviada a {computador.NomeExibicao}. O resultado aparecerá aqui quando o computador responder.";
        }
        catch (Exception ex)
        {
            StatusMensagem = $"Não foi possível enviar a ação para {computador.NomeExibicao}: {ex.Message}";
        }
    }

    private async Task EnviarCmdRemotoAsync(object? param)
    {
        if (!_cloud.IsAdmin || param is not Computador computador || !computador.PodeUsarCmdRemoto)
            return;
        if (!RemoteCommandExecutor.IsValid(ComandoRemoto))
        {
            ResultadoComandoRemoto = "Digite um comando de até 500 caracteres.";
            return;
        }
        try
        {
            var line = ComandoRemoto.Trim();
            var requestId = Guid.NewGuid().ToString("N");
            _comandoRemotoRequestId = requestId;
            _comandoRemotoTargetId = computador.Id;
            ResultadoComandoRemoto = $"> {line}\nAguardando resposta de {computador.NomeExibicao}…";
            await _cloud.QueueRemoteCommandAsync(computador.Id, line, requestId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _comandoRemotoRequestId = null;
            _comandoRemotoTargetId = null;
            ResultadoComandoRemoto = $"Falha no envio: {ex.Message}";
        }
    }

    private void OnRemoteCommandResponse(CloudDelivery response)
    {
        UiDispatcher.Invoke(() =>
        {
            var result = TryFormatRemoteCommandResponse(
                response, _comandoRemotoTargetId, _comandoRemotoRequestId);
            if (result is null) return;
            ResultadoComandoRemoto = result;
            _comandoRemotoRequestId = null;
            _comandoRemotoTargetId = null;
        });
    }

    private static string? TryFormatRemoteCommandResponse(
        CloudDelivery response, string? expectedTargetId, string? expectedRequestId)
    {
        var payload = response.Payload;
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object
            || !payload.TryGetProperty("command", out var command)
            || command.ValueKind != System.Text.Json.JsonValueKind.String
            || command.GetString() != "run_cmd"
            || !string.Equals(expectedTargetId, response.TargetDeviceId, StringComparison.OrdinalIgnoreCase)
            || !payload.TryGetProperty("request_id", out var requestId)
            || requestId.ValueKind != System.Text.Json.JsonValueKind.String
            || !string.Equals(expectedRequestId, requestId.GetString(), StringComparison.Ordinal))
            return null;

        var line = payload.TryGetProperty("line", out var lineNode) ? lineNode.GetString() : null;
        return $"> {line}\n{response.ResponseText ?? "Sem resposta do computador."}";
    }

    private bool PodeEditar(Computador computador) => _cloud.CanEdit(computador.Id);

    private void AplicarAutoridadeCloud() => UiDispatcher.Invoke(() =>
    {
        if (_autoridadeAplicada && _ultimoIsAdmin == _cloud.IsAdmin
            && string.Equals(_ultimoAdminDeviceId, _cloud.AdminDeviceId,
                StringComparison.OrdinalIgnoreCase)
            && _ultimaConfiguracaoGlobalRevision == _cloud.GlobalConfigRevision) return;
        _autoridadeAplicada = true;
        _ultimoIsAdmin = _cloud.IsAdmin;
        _ultimoAdminDeviceId = _cloud.AdminDeviceId;
        _ultimaConfiguracaoGlobalRevision = _cloud.GlobalConfigRevision;
        foreach (var computador in Computadores) AplicarPerfil(computador);
        OnPropertyChanged(nameof(ModelosBadgeGlobal));
        CommandManager.InvalidateRequerySuggested();
    });

    private void OnCloudDevicesReceived(IReadOnlyList<CloudDevice> devices) => UiDispatcher.Invoke(() =>
    {
        var changed = false;
        var aliasesByPanelId = FindMigratedReceiverAliases(devices);
        var aliasIds = aliasesByPanelId.Values.SelectMany(ids => ids)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceId)) continue;
            if (aliasIds.Contains(device.DeviceId)) continue;
            if (_cloudReceiverUpdates.TryGetValue(device.DeviceId, out var updateConfirmation))
                updateConfirmation.Observe(device);
            var computador = Computadores.FirstOrDefault(c =>
                string.Equals(c.Id, device.DeviceId, StringComparison.OrdinalIgnoreCase));
            var created = computador is null;
            var migratedIdentity = false;
            if (computador is null && aliasesByPanelId.TryGetValue(device.DeviceId, out var oldIds))
            {
                computador = Computadores.FirstOrDefault(c => oldIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase));
                if (computador is not null)
                {
                    var oldId = computador.Id;
                    computador.Id = device.DeviceId;
                    AddLegacyId(computador, oldId);
                    foreach (var badge in computador.Badges) badge.ComputerId = device.DeviceId;
                    migratedIdentity = true;
                    changed = true;
                }
            }
            if (computador is null)
            {
                computador = new Computador
                {
                    Id = device.DeviceId,
                    Nome = device.MachineName,
                    EnderecoIp = string.Empty,
                    PortaTcp = _settings.PortaTcp,
                    Status = device.LastSeenAt > DateTimeOffset.UtcNow.AddSeconds(-45)
                        ? StatusComputador.Online : StatusComputador.Offline,
                    UltimaVezVisto = device.LastSeenAt.UtcDateTime,
                };
                Computadores.Add(computador);
                changed = true;
            }
            if (aliasesByPanelId.TryGetValue(device.DeviceId, out var legacyIds))
            {
                foreach (var legacyId in legacyIds)
                {
                    if (AddLegacyId(computador, legacyId)) changed = true;
                    var duplicate = Computadores.FirstOrDefault(c =>
                        !ReferenceEquals(c, computador)
                        && string.Equals(c.Id, legacyId, StringComparison.OrdinalIgnoreCase));
                    if (duplicate is null) continue;
                    MergeComputerDetails(computador, duplicate);
                    Computadores.Remove(duplicate);
                    migratedIdentity = true;
                    changed = true;
                }
            }
            if (!computador.Pareado)
            {
                var status = device.LastSeenAt > DateTimeOffset.UtcNow.AddSeconds(-45)
                    ? StatusComputador.Online : StatusComputador.Offline;
                if (computador.Status != status) { computador.Status = status; changed = true; }
                computador.UltimaVezVisto = device.LastSeenAt.UtcDateTime;
            }
            else if (migratedIdentity)
            {
                computador.Status = device.LastSeenAt > DateTimeOffset.UtcNow.AddSeconds(-45)
                    ? StatusComputador.Online : StatusComputador.Offline;
                computador.UltimaVezVisto = device.LastSeenAt.UtcDateTime;
                computador.PingMs = null;
            }
            if (!string.Equals(computador.Nome, device.MachineName, StringComparison.Ordinal))
            {
                computador.Nome = device.MachineName;
                changed = true;
            }
            var hasPanel = device.HasPanel;
            if (computador.TemPainel != hasPanel) { computador.TemPainel = hasPanel; changed = true; }
            if (!computador.RegistradoNaNuvem)
            {
                computador.RegistradoNaNuvem = true;
                changed = true;
            }
            if (computador.MidiasBloqueadas != device.MediaBlocked)
            {
                computador.MidiasBloqueadas = device.MediaBlocked;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(device.PanelVersion)
                && computador.VersaoPainel != device.PanelVersion)
            {
                computador.VersaoPainel = device.PanelVersion;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(device.ReceiverVersion)
                && computador.VersaoReceptor != device.ReceiverVersion)
            {
                computador.VersaoReceptor = device.ReceiverVersion;
                changed = true;
            }
            if (created || migratedIdentity) AplicarPerfil(computador);
        }
        if (changed)
        {
            Persist();
            CommandManager.InvalidateRequerySuggested();
        }
    });

    private static Dictionary<string, string[]> FindMigratedReceiverAliases(
        IReadOnlyList<CloudDevice> devices)
    {
        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in devices
            .Where(d => !string.IsNullOrWhiteSpace(d.MachineName) && !string.IsNullOrWhiteSpace(d.DeviceId))
            .GroupBy(d => d.MachineName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var panels = group.Where(d => d.HasPanel).ToArray();
            var receivers = group.Where(d => !d.HasPanel).ToArray();
            if (panels.Length == 1 && receivers.Length == 1
                && !string.Equals(panels[0].DeviceId, receivers[0].DeviceId, StringComparison.OrdinalIgnoreCase))
                aliases[panels[0].DeviceId] = [receivers[0].DeviceId];
        }
        return aliases;
    }

    private static bool AddLegacyId(Computador computador, string legacyId)
    {
        if (string.IsNullOrWhiteSpace(legacyId)
            || computador.LegacyDeviceIds.Contains(legacyId, StringComparer.OrdinalIgnoreCase)) return false;
        computador.LegacyDeviceIds.Add(legacyId);
        return true;
    }

    private static void MergeComputerDetails(Computador primary, Computador duplicate)
    {
        if (string.IsNullOrWhiteSpace(primary.EnderecoIp) && !string.IsNullOrWhiteSpace(duplicate.EnderecoIp))
            primary.EnderecoIp = duplicate.EnderecoIp;
        if (primary.PortaTcp <= 0) primary.PortaTcp = duplicate.PortaTcp;
        if (string.IsNullOrWhiteSpace(primary.Token)) primary.Token = duplicate.Token;
        primary.Pareado |= duplicate.Pareado;
        if (primary.Monitores.Count == 0 && duplicate.Monitores.Count > 0)
            primary.Monitores = duplicate.Monitores;
        if (primary.Badges.Count == 0 && duplicate.Badges.Count > 0)
            primary.Badges = duplicate.Badges.Select(b => b.Clone()).ToList();
        if (string.IsNullOrWhiteSpace(primary.Apelido)) primary.Apelido = duplicate.Apelido;
        foreach (var id in duplicate.LegacyDeviceIds) AddLegacyId(primary, id);
        AddLegacyId(primary, duplicate.Id);
    }

    public IReadOnlyList<Computador> Snapshot() => Computadores.ToList();

    /// <summary>Um receptor abriu conexao para este painel e se registrou. Ele ja chega
    /// pareado e online, sem precisar de descoberta nem de porta aberta no lado dele.</summary>
    public void RegistrarViaConexaoReversa(ConexaoReversa conexao)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var existente = Computadores.FirstOrDefault(c => c.Id == conexao.ComputerId)
                ?? Computadores.FirstOrDefault(c => !c.Pareado && c.EnderecoIp == conexao.EnderecoIp);

            if (existente is null)
            {
                Computadores.Add(new Computador
                {
                    Id = conexao.ComputerId,
                    Nome = conexao.ComputerName,
                    EnderecoIp = conexao.EnderecoIp,
                    PortaTcp = _settings.PortaTcp,
                    Pareado = true,
                    // sem guardar o token a notificacao sai sem ele e o receptor a rejeita
                    Token = conexao.Token,
                    TemPainel = conexao.HasPanel,
                    Monitores = MonitoresOuPadrao(conexao.Monitors),
                    VersaoReceptor = conexao.ReceiverVersion,
                    VersaoPainel = conexao.PanelVersion,
                    EhOwner = conexao.IsOwner,
                    Status = StatusComputador.Online,
                    UltimaVezVisto = DateTime.UtcNow,
                });
                StatusMensagem = $"{conexao.ComputerName} conectou-se e já está pronto para receber mensagens.";
            }
            else
            {
                existente.Id = conexao.ComputerId;
                existente.Nome = conexao.ComputerName;
                existente.EnderecoIp = conexao.EnderecoIp;
                existente.Pareado = true;
                existente.Token = conexao.Token;
                existente.TemPainel = conexao.HasPanel;
                existente.Monitores = MonitoresOuPadrao(conexao.Monitors);
                existente.VersaoReceptor = conexao.ReceiverVersion;
                existente.VersaoPainel = conexao.PanelVersion;
                existente.EhOwner = conexao.IsOwner;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
                StatusMensagem = $"{conexao.ComputerName} reconectou-se.";
            }

            var atualizado = Computadores.First(c => c.Id == conexao.ComputerId);
            RemoverDuplicadosDe(atualizado);
            AplicarPerfil(atualizado);
            Persist();
            PublicarPingMedio();
        });
    }

    public void AtualizarStatus(string computadorId, StatusComputador status, double? pingMs)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var computador = Computadores.FirstOrDefault(c => c.Id == computadorId);
            if (computador is not null)
            {
                computador.Status = status;
                computador.PingMs = status == StatusComputador.Online ? pingMs : null;
                computador.UltimaVezVisto = DateTime.UtcNow;
            }

            PublicarPingMedio();
        });
    }

    private void PublicarPingMedio()
    {
        var onlineRemotos = Computadores
            .Where(c => c.Pareado && c.Status == StatusComputador.Online && !EhComputadorLocal(c))
            .ToList();
        var pings = onlineRemotos
            .Where(c => c.PingMs.HasValue)
            .Select(c => c.PingMs!.Value)
            .ToList();

        PingMedioAtualizado?.Invoke(pings.Count > 0
            ? pings.Average()
            : onlineRemotos.Count > 0 ? double.NaN : null);
    }

    private void RemoverDuplicadosDe(Computador principal)
    {
        var duplicados = Computadores.Where(c => !ReferenceEquals(c, principal)
            && !string.Equals(c.Id, principal.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.EnderecoIp, principal.EnderecoIp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Nome, principal.Nome, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var duplicado in duplicados)
        {
            if (string.IsNullOrWhiteSpace(principal.Token) && !string.IsNullOrWhiteSpace(duplicado.Token))
                principal.Token = duplicado.Token;
            principal.Pareado |= duplicado.Pareado;
            if (principal.Monitores.Count == 0 && duplicado.Monitores.Count > 0)
                principal.Monitores = duplicado.Monitores;
            Computadores.Remove(duplicado);
        }
    }

    private bool EhComputadorLocal(Computador computador) =>
        string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(computador.Nome, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private void OnReceptorDescoberto(AnnounceInfo info)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            // primeiro tenta casar pelo id real; se não achar, casa uma entrada adicionada
            // manualmente (mesmo IP:porta, ainda sem id real) pra não duplicar a linha.
            var existente = Computadores.FirstOrDefault(c => c.Id == info.ComputerId)
                ?? Computadores.FirstOrDefault(c => !c.Pareado && c.EnderecoIp == info.IpAddress && c.PortaTcp == info.TcpPort);

            if (existente is null)
            {
                Computadores.Add(new Computador
                {
                    Id = info.ComputerId,
                    Nome = info.ComputerName,
                    EnderecoIp = info.IpAddress,
                    PortaTcp = info.TcpPort,
                    Pareado = info.Paired,
                    TemPainel = info.HasPanel,
                    Monitores = MonitoresOuPadrao(info.Monitors),
                    VersaoReceptor = info.ReceiverVersion,
                    VersaoPainel = info.PanelVersion,
                    EhOwner = info.IsOwner,
                    Status = StatusComputador.Online,
                    UltimaVezVisto = DateTime.UtcNow,
                });
                StatusMensagem = $"Novo computador encontrado: {info.ComputerName}";
            }
            else
            {
                existente.Id = info.ComputerId;
                existente.EnderecoIp = info.IpAddress;
                existente.PortaTcp = info.TcpPort;
                existente.Nome = info.ComputerName;
                existente.TemPainel = info.HasPanel;
                existente.Monitores = MonitoresOuPadrao(info.Monitors);
                existente.VersaoReceptor = info.ReceiverVersion;
                existente.VersaoPainel = info.PanelVersion;
                existente.EhOwner = info.IsOwner;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
            }

            var atualizado = Computadores.First(c => c.Id == info.ComputerId);
            RemoverDuplicadosDe(atualizado);
            AplicarPerfil(atualizado);
            Persist();
            PublicarPingMedio();
        });
    }

    /// <summary>Faz o broadcast UDP e, em seguida, varre a rede na porta do receptor.
    /// A varredura cobre o caso do broadcast nao passar (firewall, isolamento de AP),
    /// que e a causa mais comum de "nao encontra o outro computador".</summary>
    private async Task ProcurarAsync()
    {
        StatusMensagem = "Procurando na rede...";
        await _discovery.BroadcastOnceAsync().ConfigureAwait(true);

        var scanner = new LanScanner(_settings.PortaTcp);
        var encontrados = await scanner.VarrerAsync().ConfigureAwait(true);

        var novos = 0;
        foreach (var ip in encontrados)
        {
            if (Computadores.Any(c => c.EnderecoIp == ip && c.PortaTcp == _settings.PortaTcp))
            {
                continue;
            }

            Computadores.Add(new Computador
            {
                Id = Guid.NewGuid().ToString(),
                Nome = ip,
                EnderecoIp = ip,
                PortaTcp = _settings.PortaTcp,
                Pareado = false,
                Status = StatusComputador.Online,
                UltimaVezVisto = DateTime.UtcNow,
            });
            novos++;
        }

        if (novos > 0)
        {
            Persist();
            StatusMensagem = $"{novos} computador(es) encontrado(s) na varredura. Clique em \"Parear\".";
        }
        else if (encontrados.Count > 0)
        {
            StatusMensagem = "Nenhum computador novo — os encontrados já estão na lista.";
        }
        else
        {
            StatusMensagem =
                $"Nenhum receptor encontrado na rede (porta {_settings.PortaTcp}). " +
                "Confirme que o receptor está rodando no outro computador.";
        }
    }

    private bool PodeAdicionarManual() =>
        !string.IsNullOrWhiteSpace(NovoIp) && int.TryParse(NovaPorta, out var porta) && porta is > 0 and <= 65535;

    private void AdicionarManual()
    {
        var ip = NovoIp.Trim();
        var porta = int.Parse(NovaPorta);

        var duplicado = Computadores.Any(c => c.EnderecoIp == ip && c.PortaTcp == porta);
        if (duplicado)
        {
            StatusMensagem = $"{ip}:{porta} já está na lista.";
            return;
        }

        Computadores.Add(new Computador
        {
            Id = Guid.NewGuid().ToString(),
            Nome = ip,
            EnderecoIp = ip,
            PortaTcp = porta,
            Pareado = false,
            Status = StatusComputador.Desconhecido,
            UltimaVezVisto = DateTime.UtcNow,
        });
        Persist();

        StatusMensagem = $"{ip}:{porta} adicionado. Clique em \"Parear\" para conectar.";
        NovoIp = string.Empty;
        NovaPorta = ProtocolConstants.TcpPort.ToString();
    }

    private async Task PairearAsync(Computador computador)
    {
        try
        {
            StatusMensagem = $"Pareando com {computador.Nome}...";
            var resultado = await _client.PairAsync(computador.EnderecoIp, computador.PortaTcp).ConfigureAwait(true);
            computador.Id = resultado.ComputerId;
            computador.Token = resultado.Token;
            computador.Pareado = true;
            computador.Nome = resultado.ComputerName;
            computador.TemPainel = resultado.HasPanel;
            computador.Monitores = MonitoresOuPadrao(resultado.Monitors);
            computador.VersaoReceptor = resultado.ReceiverVersion;
            computador.VersaoPainel = resultado.PanelVersion;
            computador.EhOwner = resultado.IsOwner;
            computador.Status = StatusComputador.Online;
            AplicarPerfil(computador);
            Persist();
            StatusMensagem = $"Pareado com {computador.Nome}.";
        }
        catch (ReceptorComunicacaoException ex)
        {
            StatusMensagem = $"Falha ao parear com {computador.Nome}: {ex.Message} " +
                "Verifique se o Firewall do Windows permite conexões na porta TCP " +
                $"{computador.PortaTcp} nas duas máquinas.";
        }
    }

    private async Task AtualizarReceptorAsync(Computador computador)
    {
        computador.ProgressoAtualizacaoReceptor = 0;
        computador.EtapaAtualizacaoReceptor = "Iniciando atualização";
        computador.AtualizandoReceptor = true;
        StatusMensagem = $"Iniciando a atualização de {computador.NomeExibicao}...";
        CommandManager.InvalidateRequerySuggested();
        try
        {
            void Report(ReceiverUpdateProgress progress) => UiDispatcher.Invoke(() =>
            {
                computador.ProgressoAtualizacaoReceptor = progress.Percent;
                computador.EtapaAtualizacaoReceptor = progress.Stage;
                StatusMensagem = $"{computador.NomeExibicao}: {progress.Stage} ({progress.Percent}%).";
            });

            var resultado = await _atualizador.AtualizarAsync(computador, Report).ConfigureAwait(true);
            if (!resultado.Success && resultado.Status != "not_applicable"
                && _cloud.HasPermission("remote_receiver"))
            {
                Logger.Warning($"Atualização direta do receptor de {computador.NomeExibicao} falhou; "
                    + $"tentando a instalação oficial pela nuvem: {resultado.Message}", "atualizacao");
                resultado = await AtualizarReceptorPelaNuvemAsync(computador, Report).ConfigureAwait(true);
            }

            if (resultado.Status == "update_pending")
            {
                StatusMensagem = $"Pedido de atualização enviado para {computador.NomeExibicao}. "
                    + "O receptor ainda não confirmou; a solicitação continua na nuvem e o status "
                    + "será atualizado quando ele concluir.";
                Logger.Warning(StatusMensagem, "atualizacao");
                return;
            }

            if (resultado.Success)
            {
                computador.VersaoReceptor = string.IsNullOrWhiteSpace(resultado.ReceiverVersion)
                    ? ProtocolConstants.CurrentReceiverVersion
                    : resultado.ReceiverVersion;
                Persist();
                StatusMensagem = $"{computador.NomeExibicao} foi atualizado e reconectou na versão {computador.VersaoReceptor}.";
                Logger.Info(
                    $"Receptor de {computador.NomeExibicao} atualizado para {computador.VersaoReceptor}.",
                    "atualizacao");
            }
            else
            {
                var detalhe = string.IsNullOrWhiteSpace(resultado.Message)
                    ? resultado.Status
                    : resultado.Message;
                StatusMensagem = $"Não foi possível atualizar {computador.NomeExibicao}: {detalhe}.";
                Logger.Error(
                    $"Falha ao atualizar o receptor de {computador.NomeExibicao}.",
                    "atualizacao", detalhe);
            }
        }
        catch (Exception ex)
        {
            StatusMensagem = $"Não foi possível atualizar {computador.NomeExibicao}: {ex.Message}";
            Logger.Error($"Falha inesperada na atualização de {computador.NomeExibicao}.",
                "atualizacao", ex.ToString());
        }
        finally
        {
            computador.AtualizandoReceptor = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task<ReceiverUpdateResult> AtualizarReceptorPelaNuvemAsync(
        Computador computador, Action<ReceiverUpdateProgress> report)
    {
        var confirmation = new ReceiverCloudUpdateConfirmation(
            computador.Id, ProtocolConstants.CurrentReceiverVersion, DateTimeOffset.UtcNow.AddSeconds(-2));
        if (!_cloudReceiverUpdates.TryAdd(computador.Id, confirmation))
            return new(false, "update_in_progress", computador.VersaoReceptor ?? string.Empty,
                "Já existe uma atualização pela nuvem aguardando este receptor.");

        try
        {
            await _cloud.QueueAdminCommandAsync(computador.Id, "reinstall_receiver").ConfigureAwait(true);
            report(new(18, "Solicitação enviada pela nuvem; aguardando o receptor instalar e reconectar"));

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                var version = await confirmation.WaitAsync(timeout.Token).ConfigureAwait(true);
                return new(true, "updated", version, "Atualização confirmada pela versão registrada na nuvem.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return new(false, "update_pending", computador.VersaoReceptor ?? string.Empty,
                    "O pedido foi enfileirado, mas o receptor ainda não confirmou a nova versão.");
            }
        }
        catch (Exception ex)
        {
            return new(false, "cloud_delivery_failed", computador.VersaoReceptor ?? string.Empty,
                $"A atualização direta falhou e não foi possível solicitar pela nuvem: {ex.Message}");
        }
        finally
        {
            _cloudReceiverUpdates.TryRemove(computador.Id, out _);
        }
    }

    private void Persist() => _store.Save(Computadores);

    private static List<MonitorInfo> MonitoresOuPadrao(IReadOnlyList<MonitorInfo>? monitores) =>
        monitores is { Count: > 0 }
            ? monitores.ToList()
            : new List<MonitorInfo>
            {
                new() { Index = 0, Name = "Monitor principal", Primary = true },
            };
}
