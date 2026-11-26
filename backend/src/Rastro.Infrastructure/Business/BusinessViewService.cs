using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Configuration;

namespace Rastro.Infrastructure.Business;

// Visão de negócio de um levantamento (Fase 55, ADR-010): lê o grafo e os trechos, aplica as opções do
// config.json e deriva os passos-chave no núcleo, sem IA. Comum à API e ao MCP. Nada é gravado: a visão é
// derivada a cada leitura. As opções são lidas ao vivo, porque o MCP é um processo longo.
public class BusinessViewService(RastroDbContext db, IOptionsMonitor<RastroOptions> options)
{
    // Nulo quando o levantamento não existe ou ainda não tem grafo (em análise ou com falha).
    public async Task<BusinessFlow?> GetAsync(Guid surveyId, UiLanguage language, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return null;

        var segments = await db.SurveySegments.AsNoTracking()
            .Where(g => g.SurveyId == surveyId)
            .OrderBy(g => g.Order)
            .Select(g => new BusinessSegmentSource(g.Repository, g.CommitHash, g.Branch))
            .ToListAsync(ct);
        var source = new BusinessSource(survey.Id, survey.Repository, survey.CommitHash, survey.Branch, segments);
        return BusinessFlowBuilder.Derive(graph, source, options.CurrentValue.BusinessView.ToOptions(), language);
    }
}
