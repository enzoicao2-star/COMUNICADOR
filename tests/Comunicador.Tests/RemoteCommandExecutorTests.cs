using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class RemoteCommandExecutorTests
{
    [Theory]
    [InlineData("hostname")]
    [InlineData("shutdown /s /t 60")]
    [InlineData("audio devices")]
    public void AcceptsNormalCommands(string command) => Assert.True(RemoteCommandExecutor.IsValid(command));

    [Fact]
    public void RejectsEmptyAndOversizedCommands()
    {
        Assert.False(RemoteCommandExecutor.IsValid("  "));
        Assert.False(RemoteCommandExecutor.IsValid(new string('x', 501)));
        Assert.False(RemoteCommandExecutor.IsValid("echo\0hidden"));
    }

    [Fact]
    public void RequiresShortExpiry()
    {
        Assert.True(RemoteCommandExecutor.IsFresh(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O")));
        Assert.False(RemoteCommandExecutor.IsFresh(DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O")));
        Assert.False(RemoteCommandExecutor.IsFresh(DateTimeOffset.UtcNow.AddHours(1).ToString("O")));
        Assert.False(RemoteCommandExecutor.IsFresh(null));
    }

    [Theory]
    [InlineData("volume 75", "volume", "75")]
    [InlineData("audio devices", "list", "")]
    [InlineData("audio select 2", "select", "2")]
    public void ParsesBuiltInAudioCommands(string input, string action, string value)
    {
        var parsed = RemoteCommandExecutor.ParseAudioCommand(input);
        Assert.Equal(action, parsed.Action);
        Assert.Equal(value, parsed.Value);
        Assert.Null(parsed.Error);
    }

    [Theory]
    [InlineData("volume 101")]
    [InlineData("audio select 0")]
    [InlineData("audio select qualquer")]
    public void RejectsInvalidAudioCommands(string input) =>
        Assert.NotNull(RemoteCommandExecutor.ParseAudioCommand(input).Error);

    [Fact]
    public async Task RunsHarmlessCmdAndReturnsOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await RemoteCommandExecutor.RunAsync("echo codex-remote-test");
        Assert.Contains("codex-remote-test", result);
        Assert.Contains("Código de saída: 0", result);
    }

    [Fact]
    public async Task ListsAudioWithoutChangingIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await RemoteCommandExecutor.RunAsync("audio devices");
        Assert.DoesNotContain("Falha ao controlar áudio", result);
        Assert.Contains("Código de saída: 0", result);
    }
}
