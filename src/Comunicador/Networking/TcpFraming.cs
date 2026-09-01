using System.IO;
using Comunicador.Protocol;

namespace Comunicador.Networking;

public static class TcpFraming
{
    public const byte Delimiter = (byte)'\n';

    /// <summary>
    /// Reads a single newline-delimited message from the stream. Returns null on clean EOF
    /// before any byte is read. Throws <see cref="InvalidOperationException"/> if the message
    /// exceeds <see cref="ProtocolConstants.MaxTcpMessageBytes"/> before a delimiter is found.
    /// </summary>
    public static async Task<byte[]?> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        var singleByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(singleByte.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : buffer.ToArray();
            }

            if (singleByte[0] == Delimiter)
            {
                return buffer.ToArray();
            }

            buffer.WriteByte(singleByte[0]);

            if (buffer.Length > ProtocolConstants.MaxTcpMessageBytes)
            {
                throw new InvalidOperationException("Mensagem excede o tamanho máximo permitido.");
            }
        }
    }

    public static async Task WriteMessageAsync(Stream stream, ComunicadorMessage message, CancellationToken ct = default)
    {
        var framed = MessageValidator.Frame(message);
        await stream.WriteAsync(framed, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
