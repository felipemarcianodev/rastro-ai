using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Analysis;

// Processa a fila de levantamentos, um por vez. Falha de um levantamento nunca derruba o worker:
// o levantamento fica Failed com a mensagem, e o erro técnico vai para o log.
public class SurveyWorker(
    SurveyQueue queue,
    IServiceScopeFactory scopes,
    RepositoryCatalog catalog,
    ISolutionAnalyzer analyzer,
    IOptionsMonitor<RastroOptions> options,
    RastroMetrics metrics,
    TimeProvider clock,
    ILogger<SurveyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var surveyId in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(surveyId, stoppingToken);
    }

    // Ao subir: o que estava rodando foi interrompido; o que estava pendente volta para a fila.
    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();

        var interrupted = await db.Surveys.Where(s => s.Status == SurveyStatus.Running).ToListAsync(ct);
        foreach (var survey in interrupted)
            survey.Fail(SurveyFailure.Interrupted, null, clock.GetUtcNow());
        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Count} levantamento(s) interrompido(s) marcados como falha ao subir.", interrupted.Count);
        }

        var pending = await db.Surveys.Where(s => s.Status == SurveyStatus.Pending).OrderBy(s => s.CreatedAt).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in pending)
            await queue.EnqueueAsync(id, ct);
    }

    private async Task ProcessAsync(Guid surveyId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();

        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey is null || survey.Status != SurveyStatus.Pending)
        {
            logger.LogWarning("Levantamento {SurveyId} fora da fila esperada (ausente ou já processado).", surveyId);
            return;
        }

        survey.Start(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var watch = Stopwatch.StartNew();

        try
        {
            var resolved = catalog.Resolve(survey.Repository, survey.SolutionPath);
            if (resolved.Target is null)
                throw new AnalysisException(SurveyFailure.TargetUnavailable);

            var cross = survey.ReadCrossRepositories();
            // O texto dos achados nasce no idioma de agora e fica assim no levantamento (ADR-009).
            var language = EffectiveLanguage.Of(options.CurrentValue);
            FlowGraph graph;
            IReadOnlyList<SegmentInfo> segments = [];
            if (cross.Count == 0 && resolved.Target.Language == SourceLanguage.DotNet)
            {
                graph = await analyzer.AnalyzeAsync(resolved.Target, survey.EntryPoint, survey.MaxDepth, language, ct);
            }
            else
            {
                // Costura entre sistemas (Fase 25) e primeiro trecho Angular (Fase 26): o analisador tem escopo porque usa o banco (índice de rotas).
                var crossAnalyzer = scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>();
                var analysis = await crossAnalyzer.AnalyzeAsync(resolved.Target, survey.EntryPoint, survey.MaxDepth, cross, language, ct);
                graph = analysis.Graph;
                segments = analysis.Segments;
            }

            survey.Complete(graph, clock.GetUtcNow());
            foreach (var (segment, index) in segments.Select((x, i) => (x, i)))
                db.SurveySegments.Add(SurveySegment.Create(survey.Id, index + 1, segment.Repository, segment.SolutionPath, segment.EntryPoint, segment.CommitHash, segment.Branch));
            metrics.Completed(watch.Elapsed);
            logger.LogInformation("Levantamento {SurveyId} concluído: {NodeCount} nós, {UnresolvedCount} não resolvidos, {Seconds:0.0} s.",
                surveyId, survey.NodeCount, survey.UnresolvedCount, watch.Elapsed.TotalSeconds);
        }
        catch (AnalysisException ex)
        {
            survey.Fail(ex.Failure, ex.Args, clock.GetUtcNow());
            metrics.Failed("analysis");
            logger.LogWarning(ex, "Levantamento {SurveyId} falhou na análise.", surveyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            survey.Fail(SurveyFailure.Unexpected, null, clock.GetUtcNow());
            metrics.Failed("unexpected");
            logger.LogError(ex, "Levantamento {SurveyId} falhou com erro inesperado.", surveyId);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }
}
