namespace Comunicador.Networking;

public sealed record AnnounceInfo(string ComputerId, string ComputerName, string IpAddress, int TcpPort, bool Paired);

public sealed record PairResult(bool Accepted, string ComputerId, string ComputerName, string Token);

public sealed record NotificationResult(bool Delivered, bool WasShown, bool GotReply, string? ReplyText, string? ErrorMessage);

public sealed class ReceptorComunicacaoException : Exception
{
    public ReceptorComunicacaoException(string message) : base(message)
    {
    }

    public ReceptorComunicacaoException(string message, Exception inner) : base(message, inner)
    {
    }
}
