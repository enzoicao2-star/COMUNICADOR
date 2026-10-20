using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class RealtimeHeartbeatTests
{
    [Fact]
    public void ApenasConfirmacaoDaInscricaoAnunciaNovaConexao()
    {
        Assert.True(SupabaseRealtimeDeliveryListener.IsJoinAcknowledgement("1", "1", false, "ok"));
        Assert.False(SupabaseRealtimeDeliveryListener.IsJoinAcknowledgement("2", "1", false, "ok"));
        Assert.False(SupabaseRealtimeDeliveryListener.IsJoinAcknowledgement("3", "1", false, "ok"));
        Assert.False(SupabaseRealtimeDeliveryListener.IsJoinAcknowledgement("1", "1", true, "ok"));
        Assert.False(SupabaseRealtimeDeliveryListener.IsJoinAcknowledgement("1", "1", false, "error"));
    }
}
