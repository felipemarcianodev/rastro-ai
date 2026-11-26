using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;

namespace Rastro.Infrastructure.Business;

// Posição dos balões e pontos de dobra das ligações na visão de negócio (Fase 58). Vale por escopo e id: um
// levantamento sozinho (Survey) ou um quadro (Board, Fase 60). No quadro o id do passo é por levantamento, então cada
// posição leva a faixa (laneSurveyId); no escopo Survey a faixa é Guid.Empty. Quem chama confere antes que o dono do id
// existe e, no quadro, que a faixa é do quadro.
public class BusinessLayoutService(RastroDbContext db, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<BusinessLayoutService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Escopo Survey (ou uma só faixa do quadro, com laneSurveyId).
    public async Task<BusinessLayoutDto> GetAsync(LayoutScope scope, Guid scopeId, CancellationToken ct, Guid laneSurveyId = default)
    {
        var steps = await db.BusinessStepLayouts.AsNoTracking()
            .Where(s => s.Scope == scope && s.ScopeId == scopeId && s.LaneSurveyId == laneSurveyId)
            .OrderBy(s => s.StepId)
            .ToListAsync(ct);
        var edges = await db.BusinessEdgeLayouts.AsNoTracking()
            .Where(e => e.Scope == scope && e.ScopeId == scopeId && e.LaneSurveyId == laneSurveyId)
            .OrderBy(e => e.FromStep).ThenBy(e => e.ToStep)
            .ToListAsync(ct);
        return new BusinessLayoutDto(
            steps.Select(s => new StepPositionDto(s.StepId, s.X, s.Y)).ToList(),
            edges.Select(e => new EdgePointsDto(e.FromStep, e.ToStep, ReadPoints(scopeId, e.PointsJson))).ToList());
    }

    // O quadro inteiro: todas as faixas.
    public async Task<BoardLayoutDto> GetBoardAsync(Guid boardId, CancellationToken ct)
    {
        var steps = await db.BusinessStepLayouts.AsNoTracking()
            .Where(s => s.Scope == LayoutScope.Board && s.ScopeId == boardId)
            .OrderBy(s => s.LaneSurveyId).ThenBy(s => s.StepId)
            .ToListAsync(ct);
        var edges = await db.BusinessEdgeLayouts.AsNoTracking()
            .Where(e => e.Scope == LayoutScope.Board && e.ScopeId == boardId)
            .OrderBy(e => e.LaneSurveyId).ThenBy(e => e.FromStep).ThenBy(e => e.ToStep)
            .ToListAsync(ct);
        return new BoardLayoutDto(
            steps.Select(s => new BoardStepPositionDto(s.LaneSurveyId, s.StepId, s.X, s.Y)).ToList(),
            edges.Select(e => new BoardEdgePointsDto(e.LaneSurveyId, e.FromStep, e.ToStep, ReadPoints(boardId, e.PointsJson))).ToList());
    }

    // Troca em lote só o que veio (passos e ligações citados); o resto fica. Quem chama valida antes com BusinessLayoutLimits.
    public async Task SaveAsync(LayoutScope scope, Guid scopeId, BusinessLayoutDto layout, CancellationToken ct, Guid laneSurveyId = default)
    {
        await ApplyAsync(scope, scopeId, laneSurveyId, layout.Steps ?? [], layout.Edges ?? [], clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
    }

    // Quadro: o lote traz a faixa em cada item. Quem chama confere antes os limites e que as faixas são do quadro.
    public async Task SaveBoardAsync(Guid boardId, BoardLayoutDto layout, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var steps = (layout.Steps ?? []).ToLookup(s => s.LaneSurveyId);
        var edges = (layout.Edges ?? []).ToLookup(e => e.LaneSurveyId);
        foreach (var lane in steps.Select(g => g.Key).Union(edges.Select(g => g.Key)))
        {
            await ApplyAsync(LayoutScope.Board, boardId, lane,
                steps[lane].Select(s => new StepPositionDto(s.StepId, s.X, s.Y)).ToList(),
                edges[lane].Select(e => new EdgePointsDto(e.From, e.To, e.Points ?? [])).ToList(),
                now, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(LayoutScope scope, Guid scopeId, Guid lane, IReadOnlyList<StepPositionDto> stepItems,
        IReadOnlyList<EdgePointsDto> edgeItems, DateTimeOffset now, CancellationToken ct)
    {
        // Último valor vence quando o mesmo passo ou ligação vem repetido no lote.
        var steps = stepItems.GroupBy(s => s.StepId, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        var stored = await db.BusinessStepLayouts
            .Where(s => s.Scope == scope && s.ScopeId == scopeId && s.LaneSurveyId == lane)
            .ToDictionaryAsync(s => s.StepId, StringComparer.Ordinal, ct);
        foreach (var step in steps)
        {
            if (stored.TryGetValue(step.StepId, out var existing)) existing.Move(step.X, step.Y, now);
            else db.BusinessStepLayouts.Add(BusinessStepLayout.Create(scope, scopeId, step.StepId, step.X, step.Y, now, lane));
        }

        var edges = edgeItems.GroupBy(e => (e.From, e.To)).Select(g => g.Last()).ToList();
        var storedEdges = (await db.BusinessEdgeLayouts
            .Where(e => e.Scope == scope && e.ScopeId == scopeId && e.LaneSurveyId == lane)
            .ToListAsync(ct))
            .ToDictionary(e => (e.FromStep, e.ToStep));
        foreach (var edge in edges)
        {
            var json = JsonSerializer.Serialize(edge.Points ?? [], Json);
            if (storedEdges.TryGetValue((edge.From, edge.To), out var existing)) existing.SetPoints(json, now);
            else db.BusinessEdgeLayouts.Add(BusinessEdgeLayout.Create(scope, scopeId, edge.From, edge.To, json, now, lane));
        }
    }

    // "Reorganizar": apaga as posições e os pontos de dobra do escopo. No quadro, com laneSurveyId só apaga a faixa;
    // sem ele (nulo), todas as faixas.
    public async Task ResetAsync(LayoutScope scope, Guid scopeId, CancellationToken ct, Guid? laneSurveyId = null)
    {
        await db.BusinessStepLayouts
            .Where(s => s.Scope == scope && s.ScopeId == scopeId && (laneSurveyId == null || s.LaneSurveyId == laneSurveyId))
            .ExecuteDeleteAsync(ct);
        await db.BusinessEdgeLayouts
            .Where(e => e.Scope == scope && e.ScopeId == scopeId && (laneSurveyId == null || e.LaneSurveyId == laneSurveyId))
            .ExecuteDeleteAsync(ct);
    }

    // Texto gravado que não lê (banco editado à mão) vira "sem pontos de dobra": a ligação volta ao traçado automático.
    private IReadOnlyList<LayoutPoint> ReadPoints(Guid scopeId, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<LayoutPoint>>(json, Json) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Pontos de dobra ilegíveis no layout do escopo {ScopeId}; a ligação volta ao traçado automático.", scopeId);
            return [];
        }
    }
}
