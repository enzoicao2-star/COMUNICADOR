using System.IO;
using System.Reflection;
using Comunicador.Models;
using Comunicador.Services;
using Comunicador.Storage;
using Xunit;

namespace Comunicador.Tests;

public sealed class PanelCustomizationPermissionTests
{
    [Fact]
    public void Badge_ADM_pode_personalizar_sem_receber_permissao_de_envio_de_midia()
    {
        using var contexto = CriarContexto();
        contexto.Perfis.Salvar("adm", "Painel ADM", false,
            [new BadgeUsuario { Id = "admin", Texto = "ADM" }], "adm");
        DefinirAdmin(contexto.Cloud, false, "owner");

        Assert.True(contexto.Cloud.HasPermissionForDevice("adm", "customize_panel"));
        Assert.True(contexto.Cloud.HasPermissionForDevice("adm", "manage_profiles"));
        Assert.True(contexto.Cloud.HasPermissionForDevice("adm", "remote_command"));
        Assert.False(contexto.Cloud.HasPermissionForDevice("adm", "send_media"));
    }

    [Fact]
    public void Permissao_individual_de_cmd_e_reconhecida()
    {
        using var contexto = CriarContexto();
        contexto.Perfis.Salvar("cmd", "Painel CMD", false,
            [new BadgeUsuario
            {
                Id = "__individual_permissions",
                PermissoesIndividuais = ["remote_command"],
            }], "cmd");
        DefinirAdmin(contexto.Cloud, false, "owner");

        Assert.True(contexto.Cloud.HasPermissionForDevice("cmd", "remote_command"));
    }

    [Fact]
    public void Permissao_de_personalizacao_pode_ser_concedida_por_tag_ou_individualmente()
    {
        using var contexto = CriarContexto();
        var role = new ModeloBadgeGlobal { Id = "designer", Nome = "Designer", PersonalizarPainel = true };
        DefinirConfiguracaoGlobal(contexto.Cloud, new ConfiguracaoGlobalPrograma
        {
            ModelosBadge = [role],
        });
        contexto.Perfis.Salvar("designer", "Painel designer", false,
            [new BadgeUsuario { Id = "visual", RoleId = "designer" }], "designer");
        contexto.Perfis.Salvar("individual", "Painel individual", false,
            [new BadgeUsuario
            {
                Id = "__individual_permissions",
                PermissoesIndividuais = ["customize_panel"],
            }], "individual");
        DefinirAdmin(contexto.Cloud, false, "owner");

        Assert.True(contexto.Cloud.HasPermissionForDevice("designer", "customize_panel"));
        Assert.True(contexto.Cloud.HasPermissionForDevice("individual", "customize_panel"));
    }

    private static Contexto CriarContexto()
    {
        var path = Path.Combine(Path.GetTempPath(), $"panel-permission-{Guid.NewGuid():N}.json");
        var settings = new AppSettings { PainelId = "teste", NomePainel = "Teste" };
        var profiles = new PerfilComputadorRepository(new JsonStore<PerfilComputador>(path));
        var cloud = new CloudSyncService(new SupabaseClient(), settings, profiles);
        return new Contexto(path, profiles, cloud);
    }

    private static void DefinirAdmin(CloudSyncService cloud, bool isAdmin, string adminDeviceId)
    {
        typeof(CloudSyncService).GetProperty(nameof(CloudSyncService.IsAdmin))!
            .GetSetMethod(nonPublic: true)!.Invoke(cloud, [isAdmin]);
        typeof(CloudSyncService).GetProperty(nameof(CloudSyncService.AdminDeviceId))!
            .GetSetMethod(nonPublic: true)!.Invoke(cloud, [adminDeviceId]);
    }

    private static void DefinirConfiguracaoGlobal(CloudSyncService cloud, ConfiguracaoGlobalPrograma config) =>
        typeof(CloudSyncService).GetProperty(nameof(CloudSyncService.GlobalConfig))!
            .GetSetMethod(nonPublic: true)!.Invoke(cloud, [config]);

    private sealed record Contexto(
        string Path,
        PerfilComputadorRepository Perfis,
        CloudSyncService Cloud) : IDisposable
    {
        public void Dispose()
        {
            Cloud.Dispose();
            try { File.Delete(Path); }
            catch (IOException) { }
        }
    }
}
