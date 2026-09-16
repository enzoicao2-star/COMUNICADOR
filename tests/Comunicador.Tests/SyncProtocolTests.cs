using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class SyncProtocolTests
{
    [Fact]
    public void SyncRequest_ComHistoricoELogValidos_EAceito()
    {
        var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.SyncRequest);
        request.Token = "token-pareado";
        request.IncludeHistory = true;
        request.IncludeLogs = true;
        request.HistoryEntries =
        [
            new HistoricoSincronizado
            {
                Id = Guid.NewGuid().ToString(), Timestamp = DateTime.UtcNow.ToString("o"),
                Direction = "enviada", ComputerId = "pc-1", ComputerName = "PC 1",
                Title = "Aviso", Message = "Teste", Status = "exibido",
            },
        ];
        request.LogEntries =
        [
            new RegistroLogSincronizado
            {
                Id = Guid.NewGuid().ToString(), Timestamp = DateTime.UtcNow.ToString("o"),
                Level = "erro", OriginType = "panel", OriginId = "painel-1",
                OriginName = "Painel", Category = "envio", Message = "Falhou",
            },
        ];

        Assert.True(MessageValidator.Validate(request).IsValid);
    }

    [Fact]
    public void SyncRequest_ComLogSemUuid_ERecusado()
    {
        var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.SyncRequest);
        request.Token = "token-pareado";
        request.IncludeHistory = false;
        request.IncludeLogs = true;
        request.LogEntries =
        [
            new RegistroLogSincronizado
            {
                Id = "invalido", Timestamp = DateTime.UtcNow.ToString("o"), Level = "erro",
                OriginType = "receiver", OriginId = "pc", OriginName = "PC",
                Category = "receptor", Message = "Erro",
            },
        ];

        Assert.False(MessageValidator.Validate(request).IsValid);
    }
}
