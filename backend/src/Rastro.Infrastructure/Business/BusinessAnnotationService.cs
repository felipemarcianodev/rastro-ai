using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;
using Rastro.Core.Localization;

namespace Rastro.Infrastructure.Business;

public enum BusinessEditError
{
    None = 0,
    SurveyNotFound = 1,
    StepNotFound = 2,
    LabelInvalid = 3,
    KindInvalid = 4,
    ReasonInvalid = 5,
    ProposalNotFound = 6,
    AlreadyConfirmed = 7
}

// Resultado de uma edição: o passo (rotas de texto) ou o fluxo todo (rotas de tipo, que mudam o desenho).
public sealed record BusinessEditOutcome(BusinessEditError Error, BusinessStep? Step = null, BusinessFlow? Flow = null)
{
    public bool Ok => Error == BusinessEditError.None;
    public static BusinessEditOutcome Fail(BusinessEditError error) => new(error);
}

// Texto e tipo gravados dos passos da visão de negócio (Fase 59). Quem escreve é o usuário (confirmado de imediato),
// o agente (MCP) ou o provedor de IA: os dois últimos só PROPÕEM (estado Proposed) e nunca tocam no que o usuário
// já confirmou (ADR-001). Confirmar e descartar só existem aqui para as rotas da tela, nunca para o MCP.
// O passo se cita pelo Id do passo (16 hexadecimais, seguro na URL) ou pelo id do nó do grafo; o servidor resolve
// o nó, nunca um caminho.
public class BusinessAnnotationService(
    RastroDbContext db,
    BusinessViewService view,
    BusinessCodeReader code,
    TimeProvider clock,
    ILogger<BusinessAnnotationService> logger)
{
    // ---------- texto ----------

    // O usuário escreve o texto (origem User, confirmado). Vazio apaga o texto gravado e volta ao da regra.
    public async Task<BusinessEditOutcome> SetLabelAsync(Guid surveyId, string segment, string? text, UiLanguage language, CancellationToken ct)
    {
        var clean = BusinessLabelLimits.CleanLabel(text);
        if (clean is null) return BusinessEditOutcome.Fail(BusinessEditError.LabelInvalid);

        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);
        if (target.Step is null) return BusinessEditOutcome.Fail(BusinessEditError.StepNotFound);

        var stored = await db.BusinessLabels.FirstOrDefaultAsync(l => l.SurveyId == surveyId && l.NodeId == target.Key, ct);
        if (clean.Length == 0)
        {
            if (stored is not null) db.BusinessLabels.Remove(stored);
        }
        else
        {
            await StoreLabelAsync(surveyId, target.Key, target.Step, stored, clean, LabelOrigin.User, LabelState.Confirmed, ct);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Texto do passo gravado no levantamento {SurveyId} (origem {Origin}).", surveyId, LabelOrigin.User);
        return await StepAsync(surveyId, target.Key, language, ct);
    }

    // O usuário confirma a proposta atual (da regra, da IA ou do agente). Confirmar de novo um texto cujo código
    // mudou aceita o código novo (o hash é renovado).
    public async Task<BusinessEditOutcome> ConfirmLabelAsync(Guid surveyId, string segment, UiLanguage language, CancellationToken ct)
    {
        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);
        if (target.Step is null) return BusinessEditOutcome.Fail(BusinessEditError.StepNotFound);

        var stored = await db.BusinessLabels.FirstOrDefaultAsync(l => l.SurveyId == surveyId && l.NodeId == target.Key, ct);
        await StoreLabelAsync(surveyId, target.Key, target.Step, stored, stored?.Text ?? target.Step.Label,
            stored?.Origin ?? LabelOrigin.Rule, LabelState.Confirmed, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Texto do passo confirmado no levantamento {SurveyId}.", surveyId);
        return await StepAsync(surveyId, target.Key, language, ct);
    }

    // O agente (MCP) ou o provedor propõe um texto: fica como Proposed. Recusa se o usuário já confirmou um texto.
    public async Task<BusinessEditOutcome> ProposeLabelAsync(Guid surveyId, string segment, string? text, LabelOrigin origin,
        UiLanguage language, CancellationToken ct)
    {
        if (origin is not (LabelOrigin.Ai or LabelOrigin.Agent)) throw new ArgumentOutOfRangeException(nameof(origin));
        var clean = BusinessLabelLimits.CleanLabel(text);
        if (string.IsNullOrEmpty(clean)) return BusinessEditOutcome.Fail(BusinessEditError.LabelInvalid);

        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);
        if (target.Step is null) return BusinessEditOutcome.Fail(BusinessEditError.StepNotFound);

        var stored = await db.BusinessLabels.FirstOrDefaultAsync(l => l.SurveyId == surveyId && l.NodeId == target.Key, ct);
        if (stored?.State == LabelState.Confirmed) return BusinessEditOutcome.Fail(BusinessEditError.AlreadyConfirmed);

        await StoreLabelAsync(surveyId, target.Key, target.Step, stored, clean, origin, LabelState.Proposed, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Texto proposto para um passo do levantamento {SurveyId} (origem {Origin}).", surveyId, origin);
        return await StepAsync(surveyId, target.Key, language, ct);
    }

    // Várias propostas de uma vez (o "sugerir textos" do provedor): uma derivação, uma leitura de código e uma
    // gravação para o lote. Cada item é o Id do passo e o texto; o que não vale ou aponta para passo confirmado,
    // ausente ou repetido é contado como recusado. Só PROPÕE (estado Proposed).
    public async Task<(int Proposed, int Skipped)> ProposeLabelsAsync(Guid surveyId, IReadOnlyList<(string StepId, string? Text)> items,
        LabelOrigin origin, UiLanguage language, CancellationToken ct)
    {
        if (origin is not (LabelOrigin.Ai or LabelOrigin.Agent)) throw new ArgumentOutOfRangeException(nameof(origin));

        var flow = await view.GetAsync(surveyId, language, ct);
        if (flow is null) return (0, items.Count);
        var steps = flow.Steps.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var stored = await db.BusinessLabels.Where(l => l.SurveyId == surveyId).ToDictionaryAsync(l => l.NodeId, StringComparer.Ordinal, ct);

        var valid = new List<(BusinessStep Step, string Key, string Text)>();
        var skipped = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (stepId, text) in items)
        {
            var clean = BusinessLabelLimits.CleanLabel(text);
            if (string.IsNullOrEmpty(clean) || !steps.TryGetValue(stepId, out var step) || !seen.Add(stepId))
            {
                skipped++;
                continue;
            }
            var key = BusinessViewService.KeyOf(surveyId, step);
            if (stored.GetValueOrDefault(key)?.State == LabelState.Confirmed)
            {
                skipped++;
                continue;
            }
            valid.Add((step, key, clean));
        }

        var windows = await code.ReadAsync(valid.Where(v => v.Step.Ref is not null).Select(v => v.Step.Ref!), ct);
        var now = clock.GetUtcNow();
        foreach (var (step, key, text) in valid)
        {
            var hash = step.Ref is { } reference ? windows.GetValueOrDefault(reference)?.Hash ?? string.Empty : string.Empty;
            if (stored.TryGetValue(key, out var existing)) existing.Set(hash, text, origin, LabelState.Proposed, now);
            else db.BusinessLabels.Add(BusinessLabel.Create(surveyId, key, hash, text, origin, LabelState.Proposed, now));
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Proposed} textos propostos no levantamento {SurveyId} (origem {Origin}), {Skipped} recusados.",
            valid.Count, surveyId, origin, skipped);
        return (valid.Count, skipped);
    }

    private async Task StoreLabelAsync(Guid surveyId, string key, BusinessStep step, BusinessLabel? stored, string text,
        LabelOrigin origin, LabelState state, CancellationToken ct)
    {
        var hash = string.Empty;
        if (step.Ref is { } reference)
        {
            var windows = await code.ReadAsync([reference], ct);
            hash = windows.GetValueOrDefault(reference)?.Hash ?? string.Empty;
        }

        var now = clock.GetUtcNow();
        if (stored is null) db.BusinessLabels.Add(BusinessLabel.Create(surveyId, key, hash, text, origin, state, now));
        else stored.Set(hash, text, origin, state, now);
    }

    // ---------- tipo ----------

    // O usuário troca o tipo (confirmado); kind nulo volta ao detectado. Vale também para um nó que ainda não é
    // passo-chave ("Mostrar na visão de negócio").
    public async Task<BusinessEditOutcome> SetKindAsync(Guid surveyId, string segment, int? kind, UiLanguage language, CancellationToken ct)
    {
        if (kind is { } value && !Enum.IsDefined(typeof(BusinessKind), value)) return BusinessEditOutcome.Fail(BusinessEditError.KindInvalid);

        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);

        var stored = await db.BusinessKindOverrides.FirstOrDefaultAsync(k => k.SurveyId == surveyId && k.NodeId == target.Key, ct);
        if (kind is null)
        {
            if (stored is not null) db.BusinessKindOverrides.Remove(stored);
        }
        else
        {
            var chosen = (BusinessKind)kind.Value;
            var now = clock.GetUtcNow();
            if (stored is null)
                db.BusinessKindOverrides.Add(BusinessKindOverride.Create(surveyId, target.Key, chosen, LabelOrigin.User, LabelState.Confirmed, string.Empty, now));
            else
                stored.Set(chosen, LabelOrigin.User, LabelState.Confirmed, string.Empty, now);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tipo do passo trocado pelo usuário no levantamento {SurveyId}.", surveyId);
        return await FlowAsync(surveyId, language, ct);
    }

    public async Task<BusinessEditOutcome> ConfirmKindAsync(Guid surveyId, string segment, UiLanguage language, CancellationToken ct)
    {
        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);

        var stored = await db.BusinessKindOverrides.FirstOrDefaultAsync(k => k.SurveyId == surveyId && k.NodeId == target.Key, ct);
        if (stored is not { State: LabelState.Proposed }) return BusinessEditOutcome.Fail(BusinessEditError.ProposalNotFound);

        stored.Set(stored.Kind, stored.Origin, LabelState.Confirmed, stored.Reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proposta de tipo confirmada no levantamento {SurveyId}.", surveyId);
        return await FlowAsync(surveyId, language, ct);
    }

    public async Task<BusinessEditOutcome> DismissKindAsync(Guid surveyId, string segment, UiLanguage language, CancellationToken ct)
    {
        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);

        var stored = await db.BusinessKindOverrides.FirstOrDefaultAsync(k => k.SurveyId == surveyId && k.NodeId == target.Key, ct);
        if (stored is not { State: LabelState.Proposed }) return BusinessEditOutcome.Fail(BusinessEditError.ProposalNotFound);

        db.BusinessKindOverrides.Remove(stored);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proposta de tipo descartada no levantamento {SurveyId}.", surveyId);
        return await FlowAsync(surveyId, language, ct);
    }

    // O agente (MCP) propõe a troca de tipo de um passo que já aparece na visão. Só propõe: o desenho não muda
    // até o usuário confirmar, e um tipo que o usuário já confirmou não é tocado.
    public async Task<BusinessEditOutcome> ProposeKindAsync(Guid surveyId, string segment, int kind, string? reason,
        UiLanguage language, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(BusinessKind), kind)) return BusinessEditOutcome.Fail(BusinessEditError.KindInvalid);
        var cleanReason = BusinessLabelLimits.CleanReason(reason);
        if (cleanReason is null) return BusinessEditOutcome.Fail(BusinessEditError.ReasonInvalid);

        var target = await ResolveAsync(surveyId, segment, language, ct);
        if (target.Error != BusinessEditError.None) return BusinessEditOutcome.Fail(target.Error);
        if (target.Step is null) return BusinessEditOutcome.Fail(BusinessEditError.StepNotFound);

        var stored = await db.BusinessKindOverrides.FirstOrDefaultAsync(k => k.SurveyId == surveyId && k.NodeId == target.Key, ct);
        if (stored?.State == LabelState.Confirmed) return BusinessEditOutcome.Fail(BusinessEditError.AlreadyConfirmed);

        var now = clock.GetUtcNow();
        if (stored is null)
            db.BusinessKindOverrides.Add(BusinessKindOverride.Create(surveyId, target.Key, (BusinessKind)kind, LabelOrigin.Agent, LabelState.Proposed, cleanReason, now));
        else
            stored.Set((BusinessKind)kind, LabelOrigin.Agent, LabelState.Proposed, cleanReason, now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tipo proposto por agente para um passo do levantamento {SurveyId}.", surveyId);
        return await StepAsync(surveyId, target.Key, language, ct);
    }

    // ---------- resolução do passo ----------

    private sealed record Target(BusinessEditError Error, string Key, BusinessStep? Step);

    // O segmento da rota é o Id do passo (hash de 16 hexadecimais) ou o id do nó do grafo. Resolve contra os nós do
    // próprio levantamento: nada do segmento vira caminho nem consulta. Step é o passo atual (se o nó aparece na visão).
    private async Task<Target> ResolveAsync(Guid surveyId, string segment, UiLanguage language, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return new Target(BusinessEditError.SurveyNotFound, string.Empty, null);

        var key = KeyOfSegment(surveyId, graph, segment);
        if (key is null) return new Target(BusinessEditError.StepNotFound, string.Empty, null);

        var flow = await view.GetAsync(surveyId, language, ct);
        var step = flow?.Steps.FirstOrDefault(s => string.Equals(BusinessViewService.KeyOf(surveyId, s), key, StringComparison.Ordinal));
        return new Target(BusinessEditError.None, key, step);
    }

    private static string? KeyOfSegment(Guid surveyId, Rastro.Core.Graph.FlowGraph graph, string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment.Length > 600) return null;

        // O Fim mora no nó de entrada: o Id dele vem de "end|{entrada}".
        var endKey = BusinessFlowBuilder.StorageKey(graph.EntryNodeId, true);
        if (string.Equals(BusinessFlowBuilder.StepIdOf(surveyId, endKey), segment, StringComparison.Ordinal)) return endKey;

        foreach (var node in graph.Nodes)
            if (string.Equals(BusinessFlowBuilder.StepIdOf(surveyId, node.Id), segment, StringComparison.Ordinal)) return node.Id;

        return graph.Nodes.Any(n => string.Equals(n.Id, segment, StringComparison.Ordinal)) ? segment : null;
    }

    private async Task<BusinessEditOutcome> StepAsync(Guid surveyId, string key, UiLanguage language, CancellationToken ct)
    {
        var flow = await view.GetAsync(surveyId, language, ct);
        var step = flow?.Steps.FirstOrDefault(s => string.Equals(BusinessViewService.KeyOf(surveyId, s), key, StringComparison.Ordinal));
        return step is null ? BusinessEditOutcome.Fail(BusinessEditError.StepNotFound) : new BusinessEditOutcome(BusinessEditError.None, step);
    }

    private async Task<BusinessEditOutcome> FlowAsync(Guid surveyId, UiLanguage language, CancellationToken ct)
    {
        var flow = await view.GetAsync(surveyId, language, ct);
        return flow is null ? BusinessEditOutcome.Fail(BusinessEditError.SurveyNotFound) : new BusinessEditOutcome(BusinessEditError.None, Flow: flow);
    }
}
