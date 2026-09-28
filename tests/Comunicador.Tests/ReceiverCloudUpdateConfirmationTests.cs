using Comunicador.Networking;
using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class ReceiverCloudUpdateConfirmationTests
{
    [Fact]
    public async Task SoConfirmaVersaoEsperadaEmHeartbeatPosteriorAoPedido()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var confirmation = new ReceiverCloudUpdateConfirmation(
            "joao-device", "2.5.11", requestedAt);

        confirmation.Observe(new CloudDevice
        {
            DeviceId = "outro-device",
            ReceiverVersion = "2.5.11",
            LastSeenAt = requestedAt.AddSeconds(5),
        });
        confirmation.Observe(new CloudDevice
        {
            DeviceId = "joao-device",
            ReceiverVersion = "2.5.11",
            LastSeenAt = requestedAt.AddSeconds(-1),
        });
        confirmation.Observe(new CloudDevice
        {
            DeviceId = "joao-device",
            ReceiverVersion = "2.5.10",
            LastSeenAt = requestedAt.AddSeconds(5),
        });

        Assert.False(confirmation.WaitAsync(CancellationToken.None).IsCompleted);

        confirmation.Observe(new CloudDevice
        {
            DeviceId = "JOAO-DEVICE",
            ReceiverVersion = "2.5.11",
            LastSeenAt = requestedAt.AddSeconds(5),
        });

        Assert.Equal("2.5.11", await confirmation.WaitAsync(CancellationToken.None));
    }
}
