using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using System.IO;
using Xunit;

namespace Comunicador.Tests;

public sealed class PerfilComputadorRepositoryTests
{
    [Fact]
    public void Salvar_PerfilDeOutroPainel_NaoAlteraRepositorio()
    {
        WithRepository(repository =>
        {
            repository.Salvar("painel-remoto", "Nome forjado", false, [], "painel-local");
            Assert.Null(repository.Obter("painel-remoto"));
        });
    }

    [Fact]
    public void Mesclar_PerfilAssinadoPorOutroPainel_IgnoraAlteracao()
    {
        WithRepository(repository =>
        {
            var alterados = repository.Mesclar([
                new PerfilComputadorSincronizado
                {
                    ComputerId = "painel-a",
                    DisplayName = "Nome forjado",
                    UpdatedBy = "painel-b",
                    UpdatedAt = DateTime.UtcNow.ToString("o"),
                },
            ]);

            Assert.Equal(0, alterados);
            Assert.Null(repository.Obter("painel-a"));
        });
    }

    [Fact]
    public void Mesclar_PerfilPublicadoPeloProprioPainel_AceitaAlteracao()
    {
        WithRepository(repository =>
        {
            var alterados = repository.Mesclar([
                new PerfilComputadorSincronizado
                {
                    ComputerId = "painel-a",
                    DisplayName = "Maia",
                    UpdatedBy = "painel-a",
                    UpdatedAt = DateTime.UtcNow.ToString("o"),
                    Badges = [new BadgeUsuario { Texto = "OWNER" }],
                },
            ]);

            Assert.Equal(1, alterados);
            Assert.Equal("Maia", repository.Obter("painel-a")?.NomePublico);
        });
    }

    private static void WithRepository(Action<PerfilComputadorRepository> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new PerfilComputadorRepository(
                new JsonStore<PerfilComputador>(Path.Combine(directory, "perfis.json")));
            assertion(repository);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
