using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public class MessageValidatorTests
{
    private static ComunicadorMessage ValidNotification()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
        msg.Token = "abc123";
        msg.Sender = "PAINEL-PC";
        msg.Title = "Aviso";
        msg.Message = "Olá";
        msg.AllowReply = true;
        return msg;
    }

    [Fact]
    public void ValidNotification_Passes()
    {
        var result = MessageValidator.Validate(ValidNotification());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingField_Fails()
    {
        var msg = ValidNotification();
        msg.Title = null;
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, result.Code);
    }

    [Fact]
    public void UnknownType_Fails()
    {
        var msg = ValidNotification();
        msg.Type = "algo_invalido";
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.UnknownType, result.Code);
    }

    [Fact]
    public void InvalidId_Fails()
    {
        var msg = ValidNotification();
        msg.Id = "nao-e-um-uuid";
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.InvalidId, result.Code);
    }

    [Fact]
    public void TitleTooLong_Fails()
    {
        var msg = ValidNotification();
        msg.Title = new string('x', ProtocolConstants.MaxTitleLength + 1);
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.FieldTooLong, result.Code);
    }

    [Fact]
    public void UnsupportedProtocolVersion_Fails()
    {
        var msg = ValidNotification();
        msg.ProtocolVersion = 99;
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.ProtocolVersionUnsupported, result.Code);
    }

    [Fact]
    public void PayloadTooLarge_Fails()
    {
        var result = MessageValidator.ValidateSize(ProtocolConstants.MaxTcpMessageBytes + 1, isUdp: false);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.PayloadTooLarge, result.Code);
    }

    [Fact]
    public void UdpPayloadTooLarge_Fails()
    {
        var result = MessageValidator.ValidateSize(ProtocolConstants.MaxUdpMessageBytes + 1, isUdp: true);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.PayloadTooLarge, result.Code);
    }

    [Fact]
    public void InvalidJson_FailsToParse()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{isso nao e json valido");
        var success = MessageValidator.TryParse(bytes, out _, out var result);
        Assert.False(success);
        Assert.Equal(ProtocolConstants.ErrorCode.InvalidJson, result.Code);
    }

    [Fact]
    public void PairResponseAccepted_RequiresToken()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.PairResponse);
        msg.Accepted = true;
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, result.Code);
    }

    [Fact]
    public void PairResponseRejected_DoesNotRequireToken()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.PairResponse);
        msg.Accepted = false;
        var result = MessageValidator.Validate(msg);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Discover_RequiresPanelId()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Discover);
        msg.SenderName = "PAINEL-PC";
        var result = MessageValidator.Validate(msg);
        Assert.False(result.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, result.Code);
    }

    [Fact]
    public void FrameEndsWithNewline()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ping);
        msg.Token = "abc";
        var framed = MessageValidator.Frame(msg);
        Assert.Equal((byte)'\n', framed[^1]);
    }
}
