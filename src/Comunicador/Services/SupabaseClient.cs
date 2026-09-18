using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class SupabaseClient
{
    public const string ProjectUrl = "https://yofxuiajeyxeacophgdy.supabase.co";
    public const string PublishableKey = "sb_publishable_9vE6ehPLNhoByGInnUAlug_Ndd_fTam";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private CloudSession? _session;

    public async Task<CloudAdminState> RegisterDeviceAsync(
        string deviceId, string machineName, string? panelVersion, string? receiverVersion,
        CancellationToken ct = default)
    {
        var result = await RpcAsync<List<CloudAdminState>>("register_device", new
        {
            p_device_id = deviceId,
            p_machine_name = machineName,
            p_panel_version = panelVersion,
            p_receiver_version = receiverVersion,
        }, ct).ConfigureAwait(false);
        return result.FirstOrDefault() ?? new CloudAdminState();
    }

    public async Task<CloudAdminResult> ToggleAdminAsync(string password, CancellationToken ct = default)
    {
        var result = await RpcAsync<List<CloudAdminResult>>("admin_login_toggle", new
        {
            p_password = password,
        }, ct).ConfigureAwait(false);
        return result.FirstOrDefault() ?? new CloudAdminResult { Status = "unavailable" };
    }

    public async Task<CloudAdminState> GetAdminStateAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync<List<CloudAdminState>>("get_admin_state", new { }, ct)
            .ConfigureAwait(false);
        return result.FirstOrDefault() ?? new CloudAdminState();
    }

    public async Task<IReadOnlyList<CloudPanelProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get,
            "/rest/v1/panel_profiles?select=device_id,display_name,badges,updated_at", null, ct)
            .ConfigureAwait(false);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await DeserializeAsync<List<CloudPanelProfile>>(response, ct).ConfigureAwait(false) ?? [];
    }

    public async Task UpsertProfileAsync(
        string deviceId, string displayName, IEnumerable<BadgeUsuario> badges,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            device_id = deviceId,
            display_name = displayName,
            badges = badges.Take(ProtocolConstants.MaxBadgesPerComputer).Select(b => b.Clone()).ToList(),
        }, JsonOptions);
        using var request = await CreateRequestAsync(HttpMethod.Post,
            "/rest/v1/panel_profiles?on_conflict=device_id", body, ct).ConfigureAwait(false);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task QueueDeliveryAsync(
        string senderDeviceId, string targetDeviceId, DateTimeOffset deliverAt,
        object payload, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            sender_device_id = senderDeviceId,
            target_device_id = targetDeviceId,
            deliver_at = deliverAt.ToUniversalTime(),
            payload,
        }, JsonOptions);
        using var request = await CreateRequestAsync(HttpMethod.Post, "/rest/v1/deliveries", body, ct)
            .ConfigureAwait(false);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CloudDelivery>> GetResponsesAsync(
        string senderDeviceId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(senderDeviceId);
        using var request = await CreateRequestAsync(HttpMethod.Get,
            $"/rest/v1/deliveries?select=id,target_device_id,payload,response_text,responded_at,status" +
            $"&sender_device_id=eq.{id}&status=eq.responded&sender_notified_at=is.null&order=responded_at.asc&limit=50", null, ct)
            .ConfigureAwait(false);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await DeserializeAsync<List<CloudDelivery>>(response, ct).ConfigureAwait(false) ?? [];
    }

    public async Task AcknowledgeResponseAsync(string deliveryId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { p_delivery_id = deliveryId }, JsonOptions);
        using var request = await CreateRequestAsync(HttpMethod.Post,
            "/rest/v1/rpc/acknowledge_response", body, ct).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<T> RpcAsync<T>(string function, object payload, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = await CreateRequestAsync(HttpMethod.Post, $"/rest/v1/rpc/{function}", body, ct)
            .ConfigureAwait(false);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await DeserializeAsync<T>(response, ct).ConfigureAwait(false))!;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, ProjectUrl + path);
        request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<CloudSession> EnsureSessionAsync(CancellationToken ct)
    {
        await _sessionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _session ??= LoadSession();
            if (_session is not null && _session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds())
                return _session;

            if (_session is not null && !string.IsNullOrWhiteSpace(_session.RefreshToken))
            {
                try
                {
                    _session = await RequestSessionAsync(
                        $"/auth/v1/token?grant_type=refresh_token",
                        JsonSerializer.Serialize(new { refresh_token = _session.RefreshToken }), ct).ConfigureAwait(false);
                    SaveSession(_session);
                    return _session;
                }
                catch (HttpRequestException)
                {
                    _session = null;
                }
            }

            _session = await RequestSessionAsync("/auth/v1/signup", "{}", ct).ConfigureAwait(false);
            SaveSession(_session);
            return _session;
        }
        finally { _sessionGate.Release(); }
    }

    private async Task<CloudSession> RequestSessionAsync(string path, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ProjectUrl + path);
        request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var session = await DeserializeAsync<CloudSession>(response, ct).ConfigureAwait(false)
            ?? throw new HttpRequestException("O Supabase não retornou uma sessão.");
        session.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, session.ExpiresIn);
        return session;
    }

    private static CloudSession? LoadSession()
    {
        try
        {
            if (!File.Exists(AppPaths.CloudSessionFile)) return null;
            return JsonSerializer.Deserialize<CloudSession>(File.ReadAllText(AppPaths.CloudSessionFile), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void SaveSession(CloudSession session)
    {
        AppPaths.EnsureCreated();
        var temporary = AppPaths.CloudSessionFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(temporary, AppPaths.CloudSessionFile, true);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (detail.Length > 500) detail = detail[..500];
        throw new HttpRequestException($"Supabase respondeu {(int)response.StatusCode}: {detail}");
    }
}

public sealed class CloudSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
}
public class CloudAdminState
{
    public bool IsAdmin { get; set; }
    public string? AdminDeviceId { get; set; }
}
public sealed class CloudAdminResult : CloudAdminState
{
    public string Status { get; set; } = string.Empty;
}
public sealed class CloudPanelProfile
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<BadgeUsuario> Badges { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}
public sealed class CloudDelivery
{
    public string Id { get; set; } = string.Empty;
    public string TargetDeviceId { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
    public string? ResponseText { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
