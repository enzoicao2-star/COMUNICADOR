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
    private readonly HashSet<string> _responsesSeen = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;
    private string? _lastUploadedProfile;

    public event Action? StateChanged;
    public event Action<CloudDelivery>? ResponseReceived;
    public bool IsAdmin { get; private set; }
    public string? AdminDeviceId { get; private set; }
    public string Status { get; private set; } = "Conectando ao Supabase…";

    public CloudSyncService(SupabaseClient client, AppSettings settings, PerfilComputadorRepository profiles)
    {
        _client = client;
        _settings = settings;
        _profiles = profiles;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    public async Task<CloudAdminResult> ToggleAdminAsync(string password, CancellationToken ct = default)
    {
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

    public bool CanEdit(string deviceId) => IsAdmin ||
        string.Equals(deviceId, _settings.PainelId, StringComparison.OrdinalIgnoreCase);

    public Task QueueReminderAsync(
        string targetDeviceId, DateTime deliverAt, string title, string message,
        bool allowReply, CancellationToken ct = default) =>
        _client.QueueDeliveryAsync(_settings.PainelId, targetDeviceId,
            new DateTimeOffset(deliverAt.ToUniversalTime()), new
            {
                kind = "reminder",
                sender = _settings.NomePainel,
                title,
                message,
                allow_reply = allowReply,
                display_mode = ProtocolConstants.DisplayMode.Toast,
            }, ct);

    public async Task SynchronizeOnceAsync(CancellationToken ct = default)
    {
        var admin = await _client.GetAdminStateAsync(ct).ConfigureAwait(false);
        SetAdmin(admin.IsAdmin, admin.AdminDeviceId);
        var cloudProfiles = await _client.GetProfilesAsync(ct).ConfigureAwait(false);
        _profiles.Mesclar(cloudProfiles.Select(profile => new PerfilComputadorSincronizado
        {
            ComputerId = profile.DeviceId,
            DisplayName = profile.DisplayName,
            IsOwner = string.Equals(profile.DeviceId, AdminDeviceId, StringComparison.OrdinalIgnoreCase),
            Badges = profile.Badges,
            UpdatedAt = profile.UpdatedAt.ToUniversalTime().ToString("o"),
            UpdatedBy = profile.DeviceId,
        }));
        Status = "Sincronização Supabase ativa.";
        StateChanged?.Invoke();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = await _client.RegisterDeviceAsync(
                    _settings.PainelId, Environment.MachineName,
                    ProtocolConstants.CurrentPanelVersion, ProtocolConstants.CurrentReceiverVersion, ct)
                    .ConfigureAwait(false);
                SetAdmin(state.IsAdmin, state.AdminDeviceId);
                var own = _profiles.Obter(_settings.PainelId);
                if (own is not null)
                {
                    var displayName = string.IsNullOrWhiteSpace(own.NomePublico)
                        ? _settings.NomePainel : own.NomePublico;
                    var fingerprint = JsonSerializer.Serialize(new { displayName, own.Badges });
                    if (!string.Equals(_lastUploadedProfile, fingerprint, StringComparison.Ordinal))
                    {
                        await _client.UpsertProfileAsync(_settings.PainelId,
                            displayName, own.Badges, ct).ConfigureAwait(false);
                        _lastUploadedProfile = fingerprint;
                    }
                }
                await SynchronizeOnceAsync(ct).ConfigureAwait(false);
                var responses = await _client.GetResponsesAsync(_settings.PainelId, ct).ConfigureAwait(false);
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
    }
}
