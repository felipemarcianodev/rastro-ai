using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Configuration;

namespace Rastro.Infrastructure.Business;

// Visão de negócio de um levantamento (Fase 55, ADR-010): lê o grafo e os trechos, aplica as opções do
// config.json e deriva os passos-chave no núcleo, sem IA. Comum à API e ao MCP. A derivação em si não grava nada;
// por cima dela entram o que o usuário, o agente e o provedor gravaram (Fase 59): o tipo trocado vale no desenho,
// o texto gravado vale no lugar do texto da regra e o hash do código acusa "o código mudou". As opções são lidas
// ao vivo, porque o MCP é um processo longo.
public class BusinessViewService(RastroDbContext db, IOptionsMonitor<RastroOptions> options, BusinessCodeReader code)
{
    // Nulo quando o levantamento não existe ou ainda não tem grafo (em análise ou com falha). Com applyLabels falso,
    // devolve os textos da regra (quem sugere texto precisa do que a regra diria).
    public async Task<BusinessFlow?> GetAsync(Guid surveyId, UiLanguage language, CancellationToken ct, bool applyLabels = true)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return null;

        var segments = await db.SurveySegments.AsNoTracking()
            .Where(g => g.SurveyId == surveyId)
            .OrderBy(g => g.Order)
            .Select(g => new BusinessSegmentSource(g.Repository, g.CommitHash, g.Branch))
            .ToListAsync(ct);
        var source = new BusinessSource(survey.Id, survey.Repository, survey.CommitHash, survey.Branch, segments);

        var kinds = await db.BusinessKindOverrides.AsNoTracking().Where(k => k.SurveyId == surveyId).ToListAsync(ct);
        var overlay = new BusinessOverlay(
            kinds.Where(k => k.State == LabelState.Confirmed).ToDictionary(k => k.NodeId, k => k.Kind, StringComparer.Ordinal),
            kinds.Where(k => k.State == LabelState.Proposed).ToDictionary(k => k.NodeId, k => new KindProposal(k.Kind, k.Reason), StringComparer.Ordinal));

        var flow = BusinessFlowBuilder.Derive(graph, source, options.CurrentValue.BusinessView.ToOptions(), language, overlay);
        return applyLabels ? await ApplyLabelsAsync(flow, ct) : flow;
    }

    private async Task<BusinessFlow> ApplyLabelsAsync(BusinessFlow flow, CancellationToken ct)
    {
        var stored = await db.BusinessLabels.AsNoTracking().Where(l => l.SurveyId == flow.SurveyId).ToListAsync(ct);
        if (stored.Count == 0) return flow;

        var byKey = stored.ToDictionary(l => l.NodeId, StringComparer.Ordinal);
        var withLabel = flow.Steps
            .Select(s => (Step: s, Label: byKey.GetValueOrDefault(KeyOf(flow.SurveyId, s))))
            .ToList();

        // Só os passos com texto gravado leem código (cada arquivo uma vez).
        var windows = await code.ReadAsync(withLabel.Where(x => x.Label is not null && x.Step.Ref is not null).Select(x => x.Step.Ref!), ct);

        var steps = withLabel.Select(x =>
        {
            if (x.Label is not { } label) return x.Step;
            var changed = label.CodeHash.Length > 0
                          && x.Step.Ref is { } reference
                          && windows.GetValueOrDefault(reference) is { } window
                          && !string.Equals(window.Hash, label.CodeHash, StringComparison.Ordinal);
            return x.Step with { Label = label.Text, LabelOrigin = label.Origin, LabelState = label.State, CodeChanged = changed };
        }).ToList();
        return flow with { Steps = steps };
    }

    // A chave com que o texto e o tipo do passo ficam gravados (o Fim leva "end|" na frente).
    public static string KeyOf(Guid surveyId, BusinessStep step) =>
        string.Equals(step.Id, BusinessFlowBuilder.StepIdOf(surveyId, BusinessFlowBuilder.StorageKey(step.NodeId, true)), StringComparison.Ordinal)
            ? BusinessFlowBuilder.StorageKey(step.NodeId, true)
            : step.NodeId;
}
