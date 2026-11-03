using Comunicador.Protocol;
using Comunicador.Models;
using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class SyncProtocolTests
{
    [Fact]
    public void HistoricoPreservaHorarioDoEstadoEntrePaineis()
    {
        var momento = new DateTime(2026, 11, 3, 14, 30, 0, DateTimeKind.Utc);
        var original = new HistoricoEntry
        {
            Id = Guid.NewGuid().ToString(), Status = StatusEnvio.RecebidoPeloReceptor,
            StatusAtualizadoEm = momento,
        };
        var restaurado = original.ToSync().ToModel();
        Assert.Equal(StatusEnvio.RecebidoPeloReceptor, restaurado.Status);
        Assert.Equal(momento, restaurado.StatusAtualizadoEm?.ToUniversalTime());
    }

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
