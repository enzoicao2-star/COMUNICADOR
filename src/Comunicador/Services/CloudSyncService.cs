using System.IO;
using System.Net.Http;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class CloudSyncService : IDisposable
{
    private readonly SupabaseClient _client;
    private readonly AppSettings _settings;
    private readonly PerfilComputadorRepository _profiles;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _deviceRegistrationGate = new(1, 1);
    private readonly HashSet<string> _responsesSeen = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;
    private string? _lastUploadedProfile;
    private CloudPanelProfile? _lastCloudOwnProfile;
    private string? _lastGlobalConfigJson;

    public event Action? StateChanged;
    public event Action<CloudDelivery>? ResponseReceived;
    public event Action<CloudDelivery>? DeliveryReceived;
    public event Action<IReadOnlyList<CloudDevice>>? DevicesReceived;
    public event Action<ConfiguracaoGlobalPrograma>? GlobalConfigReceived;
    public bool IsAdmin { get; private set; }
    public bool IsDelegatedAdmin { get; private set; }
    public string? AdminDeviceId { get; private set; }
    public string Status { get; private set; } = "Conectando ao Supabase…";
    public ConfiguracaoGlobalPrograma? GlobalConfig { get; private set; }

    public async Task SaveGlobalConfigAsync(ConfiguracaoGlobalPrograma config, CancellationToken ct = default)
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Somente o OWNER pode alterar as configurações globais.");
        await _client.SaveGlobalConfigAsync(config, ct).ConfigureAwait(false);
        ApplyGlobalConfig(config);
    }

    public Task<IReadOnlyList<AdminAuditEntry>> GetAdminAuditAsync(CancellationToken ct = default)
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Somente o OWNER pode ver a auditoria administrativa.");
        return _client.GetAdminAuditAsync(ct);
    }

    public Task<string> ChangeAdminPasswordAsync(string currentPassword, string newPassword,
        CancellationToken ct = default)
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Somente o OWNER pode trocar a senha administrativa.");
        return _client.ChangeAdminPasswordAsync(currentPassword, newPassword, ct);
    }

    public CloudSyncService(SupabaseClient client, AppSettings settings, PerfilComputadorRepository profiles)
    {
        _client = client;
        _settings = settings;
        _profiles = profiles;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    public async Task<CloudAdminResult> ToggleAdminAsync(string password, CancellationToken ct = default)
    {
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        var result = await _client.ToggleAdminAsync(password, ct).ConfigureAwait(false);
        SetAdmin(result.IsAdmin, result.AdminDeviceId);
        Status = result.Status switch
        {
            "created" => "Senha criada. Este computador agora é o admin supremo.",
            "transferred" => "Acesso administrativo transferido para este computador.",
            "disabled" => "Este computador deixou de ser administrador.",
            "invalid_password" => "Senha de administrador incorreta.",
            "password_too_short" => "A senha precisa ter pelo menos 8 caracteres.",
            _ => "Não foi possível alterar o administrador.",
        };
        StateChanged?.Invoke();
        return result;
    }

    public async Task SaveProfileAsync(Computador computer, CancellationToken ct = default)
    {
        await SaveProfileAsync(computer.Id, computer.NomeExibicao, computer.Badges, ct).ConfigureAwait(false);
    }

    public async Task SaveProfileAsync(
        string deviceId, string displayName, IEnumerable<BadgeUsuario> badges,
        CancellationToken ct = default)
    {
        // panel_profiles.device_id references devices.device_id. Register the
        // local panel first, including when the user saves before the sync loop's
        // first request has completed.
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        if (!CanEdit(deviceId)) throw new UnauthorizedAccessException("Somente o próprio painel ou o admin pode alterar este perfil.");
        var normalized = badges.Where(b => b.Id != "owner").Select(b => b.Clone()).ToList();
        if (string.Equals(deviceId, AdminDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            var owner = badges.FirstOrDefault(b => b.Id == "owner")?.Clone() ?? new BadgeUsuario
            {
                Id = "owner", Texto = "OWNER", Cor = "#F2B84B", Estilo = "Holográfica",
                Icone = "Coroa", Brilho = true, EfeitoMouse = true,
            };
            normalized.Insert(0, owner);
        }
        await _client.UpsertProfileAsync(deviceId, displayName, normalized, ct)
            .ConfigureAwait(false);
        if (string.Equals(deviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase))
            _lastUploadedProfile = JsonSerializer.Serialize(new { displayName, Badges = normalized });
        await SynchronizeOnceAsync(ct).ConfigureAwait(false);
    }

    public bool CanEdit(string deviceId)
    {
        if (IsAdmin || string.Equals(deviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase)) return true;
        if (!(HasPermission("manage_profiles") || HasPermission("manage_badges"))
            || string.Equals(deviceId, AdminDeviceId, StringComparison.OrdinalIgnoreCase)) return false;
        var target = _profiles.Obter(deviceId);
        return target is null || target.Badges.All(b => b.Id != "admin");
    }

    public bool HasPermission(string permission) => IsAdmin || HasPermissionForDevice(_settings.PainelId, permission);

    public async Task<bool> IsCurrentAdminDeviceAsync(string senderDeviceId, CancellationToken ct = default)
    {
        try
        {
            var state = await _client.GetAdminStateAsync(ct).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(state.AdminDeviceId)
                && string.Equals(state.AdminDeviceId, senderDeviceId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning($"Mídia remota recusada: falha ao validar OWNER: {ex.Message}");
            return false;
        }
    }

    public bool HasPermissionForDevice(string deviceId, string permission)
    {
        if (string.Equals(deviceId, AdminDeviceId, StringComparison.OrdinalIgnoreCase)) return true;
        var profile = _profiles.Obter(deviceId);
        if (permission == "manage_profiles" && profile?.Badges.Any(b => b.Id == "admin") == true) return true;
        if (GlobalConfig?.ModelosBadge is not { } roles || profile is null) return false;
        return profile.Badges.Any(b => b.RoleId is not null
            && roles.Any(role => role.Id == b.RoleId && role.Permissoes.Contains(permission)));
    }

    public Task QueueReminderAsync(
        string targetDeviceId, DateTime deliverAt, string title, string message,
        bool allowReply, CancellationToken ct = default) => QueueReminderCoreAsync(
            targetDeviceId, deliverAt, title, message, allowReply, ct);

    private async Task QueueReminderCoreAsync(
        string targetDeviceId, DateTime deliverAt, string title, string message,
        bool allowReply, CancellationToken ct)
    {
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        await _client.QueueDeliveryAsync(_settings.PainelId, targetDeviceId,
            new DateTimeOffset(deliverAt.ToUniversalTime()), new
            {
                kind = "reminder",
                sender = _settings.NomePainel,
                title,
                message,
                allow_reply = allowReply,
                display_mode = ProtocolConstants.DisplayMode.Toast,
            }, ct).ConfigureAwait(false);
    }

    public async Task QueueNotificationAsync(
        string targetDeviceId, EnvioPendente notification, CancellationToken ct = default)
    {
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        await _client.QueueDeliveryAsync(_settings.PainelId, targetDeviceId, DateTimeOffset.UtcNow,
            new
            {
                kind = "notification",
                sender = _settings.NomePainel,
                title = notification.Titulo,
                message = notification.Mensagem,
                allow_reply = notification.PermitirResposta,
                confirmation_required = notification.ConfirmacaoObrigatoria,
                buttons = notification.Botoes,
                display_mode = notification.ModoExibicao,
                image = notification.Imagem,
                screen_images = notification.ImagensPorMonitor,
                image_duration_seconds = notification.DuracaoSegundos,
                allow_manual_close = notification.PermitirFecharManualmente,
                appearance = notification.Aparencia,
                video = notification.Video,
                screen_videos = notification.VideosPorMonitor,
                video_loop = notification.RepetirVideo,
                audio = notification.Audio,
                audio_loop = notification.RepetirAudio,
            }, ct).ConfigureAwait(false);
    }

    public async Task QueueAdminCommandAsync(string targetDeviceId, string command, CancellationToken ct = default)
    {
        var permission = command switch
        {
            "install_panel" or "reinstall_panel" => "remote_install",
            "disable_panel" or "enable_panel" => "remote_panel_access",
            "reinstall_receiver" => "remote_receiver",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        if (!HasPermission(permission)) throw new UnauthorizedAccessException("Esta conta não tem permissão para esta ação.");
        if (string.Equals(targetDeviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha outro computador para esta ação.");
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        await _client.QueueDeliveryAsync(_settings.PainelId, targetDeviceId, DateTimeOffset.UtcNow,
            new { kind = "admin_command", command, sender = _settings.NomePainel }, ct).ConfigureAwait(false);
    }

    public async Task QueueRemoteCommandAsync(
        string targetDeviceId, string command, string requestId, CancellationToken ct = default)
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Somente o OWNER pode enviar comandos CMD remotos.");
        if (!RemoteCommandExecutor.IsValid(command))
            throw new ArgumentException("Digite um comando de até 500 caracteres.", nameof(command));
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Identificador da solicitação inválido.", nameof(requestId));
        if (string.Equals(targetDeviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha outro computador para esta ação.");
        await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
        await _client.QueueDeliveryAsync(_settings.PainelId, targetDeviceId, DateTimeOffset.UtcNow,
            new { kind = "admin_command", command = "run_cmd", request_id = requestId, line = command.Trim(),
                expires_at = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"), sender = _settings.NomePainel }, ct)
            .ConfigureAwait(false);
    }

    public Task RespondToDeliveryAsync(string deliveryId, string response, CancellationToken ct = default) =>
        _client.RespondToDeliveryAsync(deliveryId, response, ct);

    public async Task SynchronizeOnceAsync(CancellationToken ct = default,
        CloudAdminState? registeredState = null)
    {
        // Os três dados são independentes; buscar em paralelo elimina esperas de
        // rede em série. O registro do ciclo já traz o estado admin atualizado.
        var adminTask = registeredState is null
            ? _client.GetAdminStateAsync(ct)
            : Task.FromResult(registeredState);
        var profilesTask = _client.GetProfilesAsync(ct);
        var devicesTask = _client.GetDevicesAsync(ct);
        var configTask = GetGlobalConfigOptionalAsync(ct);
        await Task.WhenAll(adminTask, profilesTask, devicesTask, configTask).ConfigureAwait(false);
        var admin = await adminTask.ConfigureAwait(false);
        SetAdmin(admin.IsAdmin, admin.AdminDeviceId);
        var cloudProfiles = await profilesTask.ConfigureAwait(false);
        _lastCloudOwnProfile = cloudProfiles.FirstOrDefault(profile =>
            string.Equals(profile.DeviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase));
        var globalConfig = await configTask.ConfigureAwait(false);
        if (globalConfig is not null) ApplyGlobalConfig(globalConfig);
        IsDelegatedAdmin = cloudProfiles.FirstOrDefault(profile =>
            string.Equals(profile.DeviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase))
            ?.Badges.Any(b => b.Id == "admin" || b.RoleId is not null && globalConfig?.ModelosBadge
                .Any(role => role.Id == b.RoleId && role.Permissoes.Contains("manage_profiles")) == true) == true;
        _profiles.Mesclar(cloudProfiles.Select(profile => new PerfilComputadorSincronizado
        {
            ComputerId = profile.DeviceId,
            DisplayName = profile.DisplayName,
            IsOwner = string.Equals(profile.DeviceId, AdminDeviceId, StringComparison.OrdinalIgnoreCase),
            Badges = profile.Badges,
            UpdatedAt = profile.UpdatedAt.ToUniversalTime().ToString("o"),
            UpdatedBy = profile.DeviceId,
        }));
        DevicesReceived?.Invoke(await devicesTask.ConfigureAwait(false));
        Status = "Sincronização Supabase ativa.";
        StateChanged?.Invoke();
    }

    private async Task<ConfiguracaoGlobalPrograma?> GetGlobalConfigOptionalAsync(CancellationToken ct)
    {
        try { return await _client.GetGlobalConfigAsync(ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.Warning($"Configuração global indisponível; mantendo sincronização normal: {ex.Message}");
            return null;
        }
    }

    private void ApplyGlobalConfig(ConfiguracaoGlobalPrograma config)
    {
        var fingerprint = JsonSerializer.Serialize(config);
        if (string.Equals(fingerprint, _lastGlobalConfigJson, StringComparison.Ordinal)) return;
        _lastGlobalConfigJson = fingerprint;
        GlobalConfig = config;
        GlobalConfigReceived?.Invoke(config);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = await RegisterCurrentDeviceAsync(ct).ConfigureAwait(false);
                // Primeiro recebe o perfil compartilhado. Enviar o cache local antes
                // disso restaurava apelidos antigos por cima da edicao de outro painel.
                await SynchronizeOnceAsync(ct, state).ConfigureAwait(false);
                var own = _profiles.Obter(_settings.PainelId);
                if (own is null && _lastCloudOwnProfile is null)
                {
                    _profiles.Salvar(_settings.PainelId, _settings.NomePainel, IsAdmin,
                        [], _settings.PainelId);
                    own = _profiles.Obter(_settings.PainelId);
                }
                if (own is not null)
                {
                    var displayName = string.IsNullOrWhiteSpace(own.NomePublico)
                        ? _settings.NomePainel : own.NomePublico;
                    var fingerprint = JsonSerializer.Serialize(new { displayName, own.Badges });
                    var cloudFingerprint = _lastCloudOwnProfile is { } published
                        ? JsonSerializer.Serialize(new { displayName = published.DisplayName, Badges = published.Badges })
                        : null;
                    if (!string.Equals(fingerprint, cloudFingerprint, StringComparison.Ordinal)
                        && !string.Equals(_lastUploadedProfile, fingerprint, StringComparison.Ordinal)
                        && (_lastCloudOwnProfile is null
                            || own.AtualizadoEmUtc > _lastCloudOwnProfile.UpdatedAt.ToUniversalTime()))
                    {
                        await _client.UpsertProfileAsync(_settings.PainelId,
                            displayName, own.Badges, ct).ConfigureAwait(false);
                        _lastUploadedProfile = fingerprint;
                    }
                }
                var deliveriesTask = _client.ClaimDueDeliveriesAsync(ct);
                var responsesTask = _client.GetResponsesAsync(_settings.PainelId, ct);
                await Task.WhenAll(deliveriesTask, responsesTask).ConfigureAwait(false);
                var deliveries = await deliveriesTask.ConfigureAwait(false);
                foreach (var delivery in deliveries) DeliveryReceived?.Invoke(delivery);
                var responses = await responsesTask.ConfigureAwait(false);
                foreach (var response in responses.OrderBy(r => r.RespondedAt))
                {
                    if (!_responsesSeen.Add(response.Id)) continue;
                    await _client.AcknowledgeResponseAsync(response.Id, ct).ConfigureAwait(false);
                    ResponseReceived?.Invoke(response);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException
                or UnauthorizedAccessException or JsonException)
            {
                Status = $"Supabase indisponível: {ex.Message}";
                Logger.Error($"Falha na sincronização com o Supabase: {ex.Message}");
                StateChanged?.Invoke();
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<CloudAdminState> RegisterCurrentDeviceAsync(CancellationToken ct)
    {
        await _deviceRegistrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await _client.RegisterDeviceAsync(
                _settings.PainelId, Environment.MachineName,
                ProtocolConstants.CurrentPanelVersion, ProtocolConstants.CurrentReceiverVersion,
                !_settings.AceitarImagensDeOutrosPaineis, ct).ConfigureAwait(false);
            SetAdmin(state.IsAdmin, state.AdminDeviceId);
            return state;
        }
        finally
        {
            _deviceRegistrationGate.Release();
        }
    }

    private void SetAdmin(bool isAdmin, string? adminDeviceId)
    {
        var changed = IsAdmin != isAdmin ||
            !string.Equals(AdminDeviceId, adminDeviceId, StringComparison.OrdinalIgnoreCase);
        IsAdmin = isAdmin;
        AdminDeviceId = adminDeviceId;
        _settings.EstePainelEhOwner = isAdmin;
        if (changed) SettingsStore.Save(_settings);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
        _deviceRegistrationGate.Dispose();
    }
}
