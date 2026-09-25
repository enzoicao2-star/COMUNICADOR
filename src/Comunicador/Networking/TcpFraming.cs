using System.IO;
using System.Runtime.CompilerServices;
using Comunicador.Protocol;

namespace Comunicador.Networking;

public static class TcpFraming
{
    public const byte Delimiter = (byte)'\n';

    private sealed class ReaderState
    {
        public byte[] Pending { get; set; } = Array.Empty<byte>();
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private static readonly ConditionalWeakTable<Stream, ReaderState> ReaderStates = new();

    /// <summary>
    /// Reads a single newline-delimited message from the stream. Returns null on clean EOF
    /// before any byte is read. Throws <see cref="InvalidOperationException"/> if the message
    /// exceeds <see cref="ProtocolConstants.MaxTcpMessageBytes"/> before a delimiter is found.
    /// </summary>
    public static async Task<byte[]?> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        var state = ReaderStates.GetOrCreateValue(stream);
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var mensagem = new MemoryStream();

            if (state.Pending.Length > 0)
            {
                var delimitador = Array.IndexOf(state.Pending, Delimiter);
                if (delimitador >= 0)
                {
                    mensagem.Write(state.Pending, 0, delimitador);
                    state.Pending = state.Pending[(delimitador + 1)..];
                    return mensagem.ToArray();
                }

                mensagem.Write(state.Pending);
                state.Pending = Array.Empty<byte>();
            }

            var bloco = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(bloco.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return mensagem.Length == 0 ? null : mensagem.ToArray();
                }

                var delimitador = Array.IndexOf(bloco, Delimiter, 0, read);
                var quantidadeMensagem = delimitador >= 0 ? delimitador : read;
                mensagem.Write(bloco, 0, quantidadeMensagem);

                if (mensagem.Length > ProtocolConstants.MaxTcpMessageBytes)
                {
                    throw new InvalidOperationException("Mensagem excede o tamanho máximo permitido.");
                }

                if (delimitador >= 0)
                {
                    var restantes = read - delimitador - 1;
                    state.Pending = restantes > 0
                        ? bloco.AsSpan(delimitador + 1, restantes).ToArray()
                        : Array.Empty<byte>();
                    return mensagem.ToArray();
                }
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public static Task WriteMessageAsync(Stream stream, ComunicadorMessage message,
        CancellationToken ct = default, Action<long, long>? transferProgress = null)
    {
        return WriteFramedAsync(stream, MessageValidator.Frame(message), ct, transferProgress);
    }

    public static async Task WriteFramedAsync(Stream stream, byte[] framed,
        CancellationToken ct = default, Action<long, long>? transferProgress = null)
    {
        if (transferProgress is null)
        {
            await stream.WriteAsync(framed, ct).ConfigureAwait(false);
        }
        else
        {
            const int chunkSize = 32 * 1024;
            for (var offset = 0; offset < framed.Length; offset += chunkSize)
            {
                var length = Math.Min(chunkSize, framed.Length - offset);
                await stream.WriteAsync(framed.AsMemory(offset, length), ct).ConfigureAwait(false);
                transferProgress(offset + length, framed.Length);
            }
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
