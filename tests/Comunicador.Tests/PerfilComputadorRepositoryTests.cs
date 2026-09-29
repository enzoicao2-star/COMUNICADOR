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

    [Fact]
    public void MesclarDaNuvem_ApelidoRemotoSubstituiCacheComRelogioAdiantado()
    {
        WithRepository(repository =>
        {
            repository.Mesclar([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome antigo", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.AddDays(2).ToString("o"),
            }]);

            var changed = repository.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome novo", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            }]);

            Assert.Equal(1, changed);
            Assert.Equal("Nome novo", repository.Obter("painel-a")?.NomePublico);
        });
    }

    [Fact]
    public void MesclarDaRede_CacheAntigoNaoSubstituiApelidoConfirmadoPeloBanco()
    {
        WithRepository(repository =>
        {
            repository.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome atual", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            }]);

            var changed = repository.Mesclar([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome antigo", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.AddDays(2).ToString("o"),
            }]);

            Assert.Equal(0, changed);
            Assert.Equal("Nome atual", repository.Obter("painel-a")?.NomePublico);
        });
    }

    [Fact]
    public void MesclarDaNuvem_ConfirmaPerfilIgualRecebidoDaRede()
    {
        WithRepository(repository =>
        {
            var timestamp = DateTime.UtcNow.ToString("o");
            repository.Mesclar([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome atual", UpdatedBy = "painel-a",
                UpdatedAt = timestamp,
            }]);
            repository.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome atual", UpdatedBy = "painel-a",
                UpdatedAt = timestamp,
            }]);

            Assert.True(repository.Obter("painel-a")?.SincronizadoPelaNuvem);
        });
    }

    [Fact]
    public void EdicaoOffline_PermanecePendenteAteConfirmacaoMesmoAposReabrir()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonStore<PerfilComputador>(Path.Combine(directory, "perfis.json"));
            var repository = new PerfilComputadorRepository(store);
            repository.Salvar("painel-a", "Nome offline", false, [], "painel-a");
            var pending = Assert.Single(repository.ObterPendentes());

            var reopened = new PerfilComputadorRepository(store);
            Assert.Single(reopened.ObterPendentes());
            reopened.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome antigo", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.AddDays(2).ToString("o"),
            }]);
            Assert.Equal("Nome offline", reopened.Obter("painel-a")?.NomePublico);

            reopened.ConfirmarSincronizacao("painel-a", "revisao-antiga");
            Assert.Single(reopened.ObterPendentes());
            reopened.ConfirmarSincronizacao("painel-a", pending.RevisaoLocalPendente);
            Assert.Empty(reopened.ObterPendentes());
            reopened.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-a", DisplayName = "Nome de outro painel", UpdatedBy = "painel-a",
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            }]);
            Assert.Equal("Nome de outro painel", reopened.Obter("painel-a")?.NomePublico);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EdicaoRemotaDoOwner_FicaPendenteParaReenvio()
    {
        WithRepository(repository =>
        {
            repository.Salvar("painel-b", "Novo apelido", false, [], "painel-a", adminOverride: true);
            var pending = Assert.Single(repository.ObterPendentes());
            Assert.Equal("painel-b", pending.ComputerId);
            Assert.Equal("Novo apelido", pending.NomePublico);

            repository.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "painel-b", DisplayName = "Novo apelido", UpdatedBy = "painel-b",
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            }]);
            Assert.Empty(repository.ObterPendentes());
        });
    }

    [Fact]
    public void ConfirmarSincronizacao_AtrasadaNaoDescartaEdicaoMaisNova()
    {
        WithRepository(repository =>
        {
            repository.Salvar("painel-a", "Primeiro nome", false, [], "painel-a");
            var primeiraRevisao = Assert.Single(repository.ObterPendentes()).RevisaoLocalPendente;
            repository.Salvar("painel-a", "Segundo nome", false, [], "painel-a");

            repository.ConfirmarSincronizacao("painel-a", primeiraRevisao);

            Assert.Equal("Segundo nome", Assert.Single(repository.ObterPendentes()).NomePublico);
        });
    }

    [Fact]
    public void PerfisDeVersoesAntigas_PublicaApenasApelidoRegistradoSemPerfilNaNuvem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonStore<PerfilComputador>(Path.Combine(directory, "perfis.json"));
            store.Save([
                new PerfilComputador { ComputerId = "registrado", NomePublico = "Apelido antigo" },
                new PerfilComputador { ComputerId = "removido", NomePublico = "Fantasma" },
                new PerfilComputador { ComputerId = "ja-publicado", NomePublico = "Nome desatualizado" },
            ]);
            var repository = new PerfilComputadorRepository(store);
            repository.MesclarDaNuvem([new PerfilComputadorSincronizado
            {
                ComputerId = "ja-publicado", DisplayName = "Nome do banco",
                UpdatedBy = "ja-publicado", UpdatedAt = DateTime.UtcNow.ToString("o"),
            }]);

            var preparados = repository.PrepararPerfisLegados(
                ["registrado", "ja-publicado"], ["ja-publicado"]);

            Assert.Equal(1, preparados);
            Assert.Equal("Apelido antigo", Assert.Single(repository.ObterPendentes()).NomePublico);
            Assert.Equal("Nome do banco", repository.Obter("ja-publicado")?.NomePublico);
            Assert.Null(repository.Obter("removido")?.RevisaoLocalPendente);
            Assert.Single(new PerfilComputadorRepository(store).ObterPendentes());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
