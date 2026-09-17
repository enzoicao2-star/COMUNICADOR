using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class MidiaMensagemTests
{
    private static ComunicadorMessage Base(string modo)
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
        msg.Token = "token";
        msg.Sender = "PAINEL";
        msg.Title = string.Empty;
        msg.Message = string.Empty;
        msg.AllowReply = false;
        msg.DisplayMode = modo;
        return msg;
    }

    private static ConteudoVideo Video() => new()
    {
        Name = "video.mp4",
        MimeType = "video/mp4",
        DataBase64 = Convert.ToBase64String(new byte[]
        {
            0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
        }),
    };

    private static ConteudoAudio Audio() => new()
    {
        Name = "som.wav",
        MimeType = "audio/wav",
        DataBase64 = Convert.ToBase64String(new byte[]
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 4, 0, 0, 0,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E',
        }),
    };

    [Fact]
    public void VideoSemLoop_TerminaComOArquivo()
    {
        var msg = Base(ProtocolConstants.DisplayMode.CenterVideo);
        msg.Video = Video();
        msg.VideoLoop = false;
        msg.AllowManualClose = true;

        Assert.True(MessageValidator.Validate(msg).IsValid);
    }

    [Fact]
    public void VideoEmLoop_ExigeDuracao()
    {
        var msg = Base(ProtocolConstants.DisplayMode.CenterVideo);
        msg.Video = Video();
        msg.VideoLoop = true;
        msg.AllowManualClose = true;

        Assert.False(MessageValidator.Validate(msg).IsValid);
        msg.ImageDurationSeconds = 30;
        Assert.True(MessageValidator.Validate(msg).IsValid);
    }

    [Fact]
    public void AudioPodeTocarAteAcabarOuPararNoTempoDefinido()
    {
        var msg = Base(ProtocolConstants.DisplayMode.Audio);
        msg.Audio = Audio();
        msg.AudioLoop = false;
        Assert.True(MessageValidator.Validate(msg).IsValid);

        msg.ImageDurationSeconds = 12;
        Assert.True(MessageValidator.Validate(msg).IsValid);
    }

    [Fact]
    public void AudioEmLoop_ExigeDuracao()
    {
        var msg = Base(ProtocolConstants.DisplayMode.Audio);
        msg.Audio = Audio();
        msg.AudioLoop = true;
        Assert.False(MessageValidator.Validate(msg).IsValid);
    }
}
