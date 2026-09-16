namespace Comunicador.Networking;

public sealed record AnnounceInfo(
    string ComputerId, string ComputerName, string IpAddress, int TcpPort, bool Paired,
    bool HasPanel = false, IReadOnlyList<Protocol.MonitorInfo>? Monitors = null,
    string? ReceiverVersion = null);

public sealed record PairResult(
    bool Accepted, string ComputerId, string ComputerName, string Token,
    bool HasPanel = false, IReadOnlyList<Protocol.MonitorInfo>? Monitors = null,
    string? ReceiverVersion = null);

public sealed record NotificationResult(bool Delivered, bool WasShown, bool GotReply, string? ReplyText, string? ErrorMessage);

public sealed record ReceiverUpdateResult(
    bool Success, string Status, string ReceiverVersion, string? Message);

public sealed record SyncResult(
    bool Success,
    IReadOnlyList<Protocol.HistoricoSincronizado> HistoryEntries,
    IReadOnlyList<Protocol.RegistroLogSincronizado> LogEntries,
    string? ErrorMessage);

public sealed class ReceptorComunicacaoException : Exception
{
    public ReceptorComunicacaoException(string message) : base(message)
    {
    }

    public ReceptorComunicacaoException(string message, Exception inner) : base(message, inner)
    {
    }
}
