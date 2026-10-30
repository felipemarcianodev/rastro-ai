using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Http;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-CROSS-NNN: o índice de rotas guardado por commit (Fase 25).
[Collection(ApiCollection.Name)]
public class RouteIndexStoreTests(ApiFixture fixture)
{
    private const string Criar = "Fixture.ApiPagamentos.EstornosController.Criar";

    private static async Task<IReadOnlyList<RouteEntry>> GetAsync(ServiceProvider provider, string repository)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RouteIndexStore>().GetAsync(repository, CancellationToken.None);
    }

    private static async Task<int> RowsAsync(ServiceProvider provider, string repository)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RastroDbContext>().RouteIndexes.AsNoTracking().CountAsync(i => i.Repository == repository);
    }

    [Fact] // INF-CROSS-010
    public async Task Obter_MesmoCommit_LeDoBancoEMudouOCommitRefazOIndice()
    {
        await using var repo = await CrossRepo.CreateAsync("indice", "ApiPagamentos");
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), repo);

        var first = await GetAsync(provider, repo.Name);
        Assert.Contains(first, e => e.Method == "POST" && e.Template == "api/estornos" && e.Target == Criar);
        Assert.Equal(1, await RowsAsync(provider, repo.Name));

        // Arquivo novo ainda sem commit: o índice guardado para este commit continua valendo.
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "Extra.cs"), """
            using Microsoft.AspNetCore.Mvc;
            namespace Fixture.ApiPagamentos;
            [ApiController]
            [Route("api/extras")]
            public class ExtrasController : ControllerBase
            {
                [HttpGet]
                public string Listar() => "";
            }
            """);
        var cached = await GetAsync(provider, repo.Name);
        Assert.Equal(first.Count, cached.Count);
        Assert.Equal(1, await RowsAsync(provider, repo.Name));

        // Commit novo: chave nova, índice refeito com a rota nova, e o antigo fica guardado.
        await repo.NewCommitAsync();
        var refreshed = await GetAsync(provider, repo.Name);
        Assert.Equal(first.Count + 1, refreshed.Count);
        Assert.Contains(refreshed, e => e.Method == "GET" && e.Template == "api/extras");
        Assert.Equal(2, await RowsAsync(provider, repo.Name));
    }

    [Fact] // INF-CROSS-011
    public async Task Obter_PastaSemGit_MontaOIndiceSemGuardar()
    {
        await using var repo = await CrossRepo.CreateAsync("semgit", "ApiPagamentos", git: false);
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), repo);

        var entries = await GetAsync(provider, repo.Name);

        Assert.Contains(entries, e => e.Target == Criar);
        Assert.Equal(0, await RowsAsync(provider, repo.Name));
    }

    [Fact] // INF-CROSS-012
    public async Task Obter_RepositorioForaDaConfiguracao_DevolveVazio()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource());

        Assert.Empty(await GetAsync(provider, "nao-cadastrado"));
    }
}
