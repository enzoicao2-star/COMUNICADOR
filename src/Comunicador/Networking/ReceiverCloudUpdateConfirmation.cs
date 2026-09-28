using Comunicador.Services;

namespace Comunicador.Networking;

/// <summary>Confirma uma atualização remota somente quando o dispositivo publica
/// a versão esperada em um heartbeat mais recente que o pedido.</summary>
public sealed class ReceiverCloudUpdateConfirmation
{
    private readonly TaskCompletionSource<string> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string DeviceId { get; }
    public string ExpectedVersion { get; }
    public DateTimeOffset RequestedAt { get; }

    public ReceiverCloudUpdateConfirmation(
        string deviceId, string expectedVersion, DateTimeOffset requestedAt)
    {
        DeviceId = deviceId;
        ExpectedVersion = expectedVersion;
        RequestedAt = requestedAt;
    }

    public void Observe(CloudDevice device)
    {
        if (string.Equals(device.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(device.ReceiverVersion, ExpectedVersion, StringComparison.Ordinal)
            && device.LastSeenAt >= RequestedAt)
            _completion.TrySetResult(device.ReceiverVersion!);
    }

    public Task<string> WaitAsync(CancellationToken cancellationToken) =>
        _completion.Task.WaitAsync(cancellationToken);
}
