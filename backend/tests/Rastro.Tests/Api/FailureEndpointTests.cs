using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rastro.Core.Localization;
using Rastro.Api.Controllers;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Proposals;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Api;

// Cenários API-FAIL-NNN: falha do levantamento, recusa de proposta e erro de rascunho viajam como
// código e parâmetros (Fase 49), e o banco da versão anterior ganha as colunas na subida.
[Collection(ApiCollection.Name)]
public class FailureEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private static async Task<SurveyDetail> WaitFinishedAsync(HttpClient client, Guid id)
    {
        for (var i = 0; i < 150; i++)
        {
            var detail = await client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json);
            if (detail!.Survey.Status is SurveyStatus.Completed or SurveyStatus.Failed) return detail;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Levantamento {id} não terminou em 30 s.");
    }

    private static async Task<SurveyDetail> CreateAndWaitAsync(HttpClient client, string entry)
    {
        var response = await client.PostAsJsonAsync("/api/surveys",
            new { repository = ApiFixture.RepositoryName, solutionPath = ApiFixture.SolutionFile, entryPoint = entry });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        return await WaitFinishedAsync(client, created.Id);
    }

    private sealed class ThrowingAnalyzer : ISolutionAnalyzer
    {
        public Task<FlowGraph> AnalyzeAsync(ResolvedTarget target, string entryPoint, int maxDepth, UiLanguage language, CancellationToken ct) =>
            throw new InvalidOperationException("detalhe técnico que não pode ir para a tela");
    }

    [Fact] // API-FAIL-001
    public async Task Levantar_PontoDeEntradaInexistente_GravaOCodigoEOsParametros()
    {
        var done = await CreateAndWaitAsync(Client, "AlertJob.NaoExiste");

        Assert.Equal(SurveyStatus.Failed, done.Survey.Status);
        Assert.Equal((int)SurveyFailure.EntryNotFound, done.Survey.Failure);
        Assert.Equal(["AlertJob.NaoExiste", "NaoExiste"], done.Survey.FailureArgs);
        Assert.Contains("nenhum método chamado NaoExiste", done.Survey.Error);
    }

    [Fact] // API-FAIL-002
    public async Task Levantar_ErroInesperado_GravaOCodigoGenericoSemODetalheTecnico()
    {
        var client = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISolutionAnalyzer>();
            services.AddSingleton<ISolutionAnalyzer>(new ThrowingAnalyzer());
        })).CreateClient();

        var done = await CreateAndWaitAsync(client, "AlertJob.ExecuteAsync");

        Assert.Equal((int)SurveyFailure.Unexpected, done.Survey.Failure);
        Assert.Empty(done.Survey.FailureArgs);
        Assert.DoesNotContain("detalhe técnico", done.Survey.Error);
    }

    [Fact] // API-FAIL-003
    public async Task Levantar_Sucesso_NaoTemCodigoDeFalha()
    {
        var done = await CreateAndWaitAsync(Client, "AlertJob.ExecuteAsync");

        Assert.Equal(SurveyStatus.Completed, done.Survey.Status);
        Assert.Null(done.Survey.Failure);
        Assert.Empty(done.Survey.FailureArgs);
    }

    [Fact] // API-FAIL-004
    public async Task Migration_BancoDaVersaoAnterior_GanhaAsColunasEMantemOTextoAntigo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rastro-tests", $"{Guid.NewGuid():N}-migration");
        Directory.CreateDirectory(folder);
        var connection = $"Data Source={Path.Combine(folder, "rastro.db")};Pooling=False";
        var options = new DbContextOptionsBuilder<RastroDbContext>();
        RastroDbContext.Configure(options, connection);

        try
        {
            // O banco como a versão anterior o deixou: migrations até a anterior a esta, e um levantamento que falhou.
            await using (var old = new RastroDbContext(options.Options))
            {
                await old.GetService<IMigrator>().MigrateAsync("20261106082611_BancoVivoDialeto");
                await old.Database.ExecuteSqlRawAsync(
                    "INSERT INTO surveys (id, repository, solution_path, entry_point, max_depth, status, error, schema_version, node_count, edge_count, unresolved_count, created_at) " +
                    "VALUES ({0}, 'loja', 'Loja.slnx', 'Job.Run', 5, 3, 'Texto antigo em português.', 1, 0, 0, 0, 0)", Guid.NewGuid().ToString());
            }

            await using var current = new RastroDbContext(options.Options);
            await current.Database.MigrateAsync();

            var survey = await current.Surveys.AsNoTracking().SingleAsync();
            Assert.Equal("Texto antigo em português.", survey.Error);
            Assert.Null(survey.Failure);
            Assert.Empty(survey.ReadFailureArgs());
            Assert.Empty(await current.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact] // API-FAIL-005
    public async Task Rascunho_Invalido_DevolveOsErrosEmCodigoAlemDoTexto()
    {
        var done = await CreateAndWaitAsync(Client, "AlertJob.ExecuteAsync");
        var graph = done.Graph!;
        var broken = graph with { Nodes = graph.Nodes.Skip(1).ToList() };

        var response = await Client.PutAsJsonAsync($"/api/surveys/{done.Survey.Id}/draft", broken, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var issues = body.GetProperty("issues");
        Assert.Equal(body.GetProperty("errors").GetArrayLength(), issues.GetArrayLength());
        Assert.Contains(issues.EnumerateArray(), i => i.GetProperty("code").GetInt32() == (int)DraftError.CodeStepMissing && i.GetProperty("args").GetArrayLength() == 1);
    }

    [Fact] // API-FAIL-006
    public async Task Propor_NoInexistente_RecusaComCodigoEGravaNaProposta()
    {
        var done = await CreateAndWaitAsync(Client, "AlertJob.ExecuteAsync");
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<ProposalService>().ProposeEdgeAsync(
            done.Survey.Id, "no-que-nao-existe", "AlertJob.Enviar", "Alerts/AlertJob.cs", 10, "Teste de recusa.",
            ProposalOrigin.Agent, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Equal(ProposalRefusal.NodeNotFound, outcome.Refusal);
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        var stored = await db.SurveyProposals.AsNoTracking().SingleAsync(p => p.Id == outcome.ProposalId);
        Assert.Equal(ProposalRefusal.NodeNotFound, stored.Refusal);
        Assert.Equal("Nó não encontrado neste levantamento.", stored.RejectionReason);
    }
}
