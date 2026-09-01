using System.IO;
using System.Text;
using Comunicador.Networking;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public class TcpFramingTests
{
    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ping);
        msg.Token = "abc123";

        using var stream = new MemoryStream();
        await TcpFraming.WriteMessageAsync(stream, msg);
        stream.Position = 0;

        var payload = await TcpFraming.ReadMessageAsync(stream);
        Assert.NotNull(payload);

        var parsed = MessageValidator.TryParse(payload!, out var parsedMsg, out _);
        Assert.True(parsed);
        Assert.Equal(ProtocolConstants.MessageType.Ping, parsedMsg!.Type);
        Assert.Equal("abc123", parsedMsg.Token);
    }

    [Fact]
    public async Task Read_ReturnsNull_OnEmptyStream()
    {
        using var stream = new MemoryStream();
        var payload = await TcpFraming.ReadMessageAsync(stream);
        Assert.Null(payload);
    }

    [Fact]
    public async Task Read_ThrowsWhenNoDelimiterAndExceedsLimit()
    {
        var oversized = new byte[ProtocolConstants.MaxTcpMessageBytes + 10];
        Array.Fill(oversized, (byte)'a');
        using var stream = new MemoryStream(oversized);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await TcpFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Read_StopsAtDelimiter_IgnoringTrailingData()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2}\n");
        using var stream = new MemoryStream(bytes);

        var first = await TcpFraming.ReadMessageAsync(stream);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(first!));

        var second = await TcpFraming.ReadMessageAsync(stream);
        Assert.Equal("{\"b\":2}", Encoding.UTF8.GetString(second!));
    }
}
