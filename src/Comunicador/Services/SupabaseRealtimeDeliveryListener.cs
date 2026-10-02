using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Comunicador.Services;

/// <summary>
/// Keeps one authenticated Supabase Realtime socket open for delivery inserts
/// and responses. REST remains the source of truth; events only wake its poller.
/// </summary>
public sealed class SupabaseRealtimeDeliveryListener
{
    private static readonly Uri RealtimeUri = new(
        $"wss://yofxuiajeyxeacophgdy.supabase.co/realtime/v1/websocket?apikey={Uri.EscapeDataString(SupabaseClient.PublishableKey)}&vsn=2.0.0");

    private readonly SupabaseClient _client;
    private readonly string _deviceId;

    public SupabaseRealtimeDeliveryListener(SupabaseClient client, string deviceId)
    {
        _client = client;
        _deviceId = deviceId;
    }

    public async Task RunAsync(Action onDeliveryActivity, CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(onDeliveryActivity, ct).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException
                or JsonException or InvalidOperationException or ArgumentException or TimeoutException)
            {
                Logger.Warning($"Canal rápido de entregas desconectado; usando busca de segurança: {ex.Message}");
                try { await Task.Delay(retryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                retryDelay = TimeSpan.FromSeconds(Math.Min(30, retryDelay.TotalSeconds * 2));
            }
        }
    }

    private async Task ListenAsync(Action onDeliveryActivity, CancellationToken ct)
    {
        var accessToken = await _client.GetRealtimeAccessTokenAsync(ct).ConfigureAwait(false);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await socket.ConnectAsync(RealtimeUri, connectTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("A conexão Realtime excedeu 15 segundos.");
            }
        }

        var topic = $"realtime:comunicador-{_deviceId}";
        var reference = "1";
        var joinPayload = new
        {
            config = new
            {
                broadcast = new { ack = false, self = false },
                presence = new { enabled = false },
                postgres_changes = new object[]
                {
                    new { @event = "*", schema = "public", table = "deliveries", filter = $"target_device_id=eq.{_deviceId}", select = new[] { "id" } },
                    new { @event = "*", schema = "public", table = "deliveries", filter = $"sender_device_id=eq.{_deviceId}", select = new[] { "id" } },
                },
                @private = false,
            },
            access_token = accessToken,
        };
        await SendFrameAsync(socket, new object?[] { reference, reference, topic, "phx_join", joinPayload }, ct)
            .ConfigureAwait(false);

        var joined = false;
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(20));
        var heartbeatTask = heartbeat.WaitForNextTickAsync(ct).AsTask();
        var receiveTask = ReceiveFrameAsync(socket, ct);
        var nextReference = 2;
        var reauthenticateAt = DateTimeOffset.UtcNow.AddMinutes(45);

        while (!ct.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(receiveTask, heartbeatTask).ConfigureAwait(false);
            if (completed == heartbeatTask)
            {
                if (!await heartbeatTask.ConfigureAwait(false)) return;
                var heartbeatReference = (nextReference++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await SendFrameAsync(socket,
                    new object?[] { reference, heartbeatReference, "phoenix", "heartbeat", new { } }, ct)
                    .ConfigureAwait(false);
                heartbeatTask = heartbeat.WaitForNextTickAsync(ct).AsTask();

                if (DateTimeOffset.UtcNow >= reauthenticateAt)
                {
                    var renewedToken = await _client.GetRealtimeAccessTokenAsync(ct).ConfigureAwait(false);
                    await SendFrameAsync(socket,
                        new object?[] { reference, (nextReference++).ToString(), topic, "access_token",
                            new { access_token = renewedToken } }, ct).ConfigureAwait(false);
                    reauthenticateAt = DateTimeOffset.UtcNow.AddMinutes(45);
                }
                continue;
            }

            var frame = await receiveTask.ConfigureAwait(false);
            receiveTask = ReceiveFrameAsync(socket, ct);
            using var document = JsonDocument.Parse(frame);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() < 5)
                continue;

            var message = document.RootElement;
            var eventName = message[3].GetString();
            var payload = message[4];
            if (eventName == "phx_reply")
            {
                var status = payload.TryGetProperty("status", out var statusNode)
                    ? statusNode.GetString()
                    : null;
                if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                    throw new WebSocketException($"Supabase Realtime recusou a inscrição: {payload}");
                if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    joined = true;
                    Logger.Info("Canal rápido de entregas Supabase conectado.");
                    onDeliveryActivity();
                }
            }
            else if (joined && eventName == "postgres_changes")
            {
                onDeliveryActivity();
            }
            else if (eventName == "phx_error" || eventName == "phx_close")
            {
                throw new WebSocketException("O Supabase encerrou o canal de entregas.");
            }
        }
    }

    private static async Task SendFrameAsync(ClientWebSocket socket, object frame, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReceiveFrameAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("O Supabase encerrou a conexão Realtime.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException("O Supabase enviou um quadro Realtime incompatível.");
            if (output.Length + result.Count > 256 * 1024)
                throw new WebSocketException("O quadro Realtime ultrapassou o limite esperado.");
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return output.ToArray();
    }
}
