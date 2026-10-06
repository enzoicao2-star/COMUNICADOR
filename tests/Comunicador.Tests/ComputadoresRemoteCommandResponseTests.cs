using System.Reflection;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Services;
using Comunicador.ViewModels;
using Xunit;

namespace Comunicador.Tests;

public sealed class ComputadoresRemoteCommandResponseTests
{
    [Fact]
    public void Formata_resposta_rapida_do_comando_para_o_computador_e_pedido_atuais()
    {
        using var payload = JsonDocument.Parse("""
            {"command":"run_cmd","request_id":"pedido-1","line":"hostname"}
            """);
        var response = new CloudDelivery
        {
            TargetDeviceId = "abcdef12",
            Payload = payload.RootElement.Clone(),
            ResponseText = "Código de saída: 0\nPC-JOAO",
        };

        var result = Format(response, "ABCDEF12", "pedido-1");

        Assert.Equal("> hostname\nComando concluído com sucesso.\nCódigo de saída: 0\nPC-JOAO", result);
    }

    [Theory]
    [InlineData("pc-1", "pedido-anterior", "pc-1", "pedido-atual")]
    [InlineData("pc-anterior", "pedido-atual", "pc-atual", "pedido-atual")]
    public void Ignora_resposta_que_nao_corresponde_ao_pedido_atual(
        string responseTargetId, string responseRequestId, string expectedTargetId, string expectedRequestId)
    {
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            command = "run_cmd",
            request_id = responseRequestId,
            line = "hostname",
        }));
        var response = new CloudDelivery
        {
            TargetDeviceId = responseTargetId,
            Payload = payload.RootElement.Clone(),
            ResponseText = "resposta antiga",
        };

        Assert.Null(Format(response, expectedTargetId, expectedRequestId));
    }

    [Fact]
    public void Consolida_a_identidade_anterior_do_receptor_quando_o_mesmo_pc_ganha_painel()
    {
        var devices = new[]
        {
            new CloudDevice { DeviceId = "painel-id", MachineName = "PC-TESTE", HasPanel = true },
            new CloudDevice { DeviceId = "receptor-id", MachineName = "pc-teste", HasPanel = false },
        };

        var aliases = FindMigratedReceiverAliases(devices);

        Assert.Equal(new[] { "receptor-id" }, aliases["painel-id"]);
    }

    [Fact]
    public void Nao_consolida_quando_mais_de_um_receptor_tem_o_mesmo_nome()
    {
        var devices = new[]
        {
            new CloudDevice { DeviceId = "painel-id", MachineName = "PC-TESTE", HasPanel = true },
            new CloudDevice { DeviceId = "receptor-1", MachineName = "PC-TESTE", HasPanel = false },
            new CloudDevice { DeviceId = "receptor-2", MachineName = "PC-TESTE", HasPanel = false },
        };

        Assert.Empty(FindMigratedReceiverAliases(devices));
    }

    private static string? Format(CloudDelivery response, string targetId, string requestId) =>
        (string?)typeof(ComputadoresViewModel)
            .GetMethod("TryFormatRemoteCommandResponse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [response, targetId, requestId]);

    private static Dictionary<string, string[]> FindMigratedReceiverAliases(IReadOnlyList<CloudDevice> devices) =>
        (Dictionary<string, string[]>)typeof(ComputadoresViewModel)
            .GetMethod("FindMigratedReceiverAliases", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [devices])!;
}
