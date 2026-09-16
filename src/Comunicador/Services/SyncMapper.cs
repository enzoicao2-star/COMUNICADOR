using Comunicador.Models;
using Comunicador.Protocol;

namespace Comunicador.Services;

public static class SyncMapper
{
    public static HistoricoSincronizado ToSync(this HistoricoEntry item) => new()
    {
        Id = item.Id,
        Timestamp = item.Timestamp.ToUniversalTime().ToString("o"),
        Direction = item.Direcao.ToString().ToLowerInvariant(),
        ComputerId = item.ComputadorId,
        ComputerName = item.ComputadorNome,
        Title = item.Titulo,
        Message = item.Mensagem,
        Status = item.Status.ToString().ToLowerInvariant(),
        ReplyText = item.RespostaTexto,
        ErrorDetail = item.ErroDetalhe,
    };

    public static HistoricoEntry ToModel(this HistoricoSincronizado item)
    {
        Enum.TryParse<DirecaoHistorico>(item.Direction, true, out var direcao);
        Enum.TryParse<StatusEnvio>(item.Status, true, out var status);
        return new HistoricoEntry
        {
            Id = item.Id,
            Timestamp = DateTime.TryParse(item.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var data)
                ? data.ToLocalTime() : DateTime.Now,
            Direcao = direcao,
            ComputadorId = item.ComputerId,
            ComputadorNome = item.ComputerName,
            Titulo = item.Title,
            Mensagem = item.Message,
            Status = status,
            RespostaTexto = item.ReplyText,
            ErroDetalhe = item.ErrorDetail,
        };
    }

    public static RegistroLogSincronizado ToSync(this LogEntry item) => new()
    {
        Id = item.Id,
        Timestamp = item.TimestampUtc.ToUniversalTime().ToString("o"),
        Level = item.Nivel.ToString().ToLowerInvariant(),
        OriginType = item.TipoOrigem,
        OriginId = item.OrigemId,
        OriginName = item.OrigemNome,
        Category = item.Categoria,
        Message = item.Mensagem,
        Details = item.Detalhes,
    };

    public static LogEntry ToModel(this RegistroLogSincronizado item)
    {
        Enum.TryParse<NivelLog>(item.Level, true, out var nivel);
        return new LogEntry
        {
            Id = item.Id,
            TimestampUtc = DateTime.TryParse(item.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var data)
                ? data.ToUniversalTime() : DateTime.UtcNow,
            Nivel = nivel,
            TipoOrigem = item.OriginType,
            OrigemId = item.OriginId,
            OrigemNome = item.OriginName,
            Categoria = item.Category,
            Mensagem = item.Message,
            Detalhes = item.Details,
        };
    }
}
