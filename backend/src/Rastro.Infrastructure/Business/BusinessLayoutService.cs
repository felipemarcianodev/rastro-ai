using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;

namespace Rastro.Infrastructure.Business;

// Posição dos balões e pontos de dobra das ligações na visão de negócio (Fase 58). Vale por escopo e id: um
// levantamento sozinho (Survey) ou, na Fase 60, um quadro (Board). Quem chama confere antes que o dono do id existe.
public class BusinessLayoutService(RastroDbContext db, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<BusinessLayoutService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BusinessLayoutDto> GetAsync(LayoutScope scope, Guid scopeId, CancellationToken ct)
    {
        var steps = await db.BusinessStepLayouts.AsNoTracking()
            .Where(s => s.Scope == scope && s.ScopeId == scopeId)
            .OrderBy(s => s.StepId)
            .ToListAsync(ct);
        var edges = await db.BusinessEdgeLayouts.AsNoTracking()
            .Where(e => e.Scope == scope && e.ScopeId == scopeId)
            .OrderBy(e => e.FromStep).ThenBy(e => e.ToStep)
            .ToListAsync(ct);
        return new BusinessLayoutDto(
            steps.Select(s => new StepPositionDto(s.StepId, s.X, s.Y)).ToList(),
            edges.Select(e => new EdgePointsDto(e.FromStep, e.ToStep, ReadPoints(scopeId, e.PointsJson))).ToList());
    }

    // Troca em lote só o que veio (passos e ligações citados); o resto fica. Quem chama valida antes com BusinessLayoutLimits.
    public async Task SaveAsync(LayoutScope scope, Guid scopeId, BusinessLayoutDto layout, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Último valor vence quando o mesmo passo ou ligação vem repetido no lote.
        var steps = (layout.Steps ?? []).GroupBy(s => s.StepId, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        var stored = await db.BusinessStepLayouts
            .Where(s => s.Scope == scope && s.ScopeId == scopeId)
            .ToDictionaryAsync(s => s.StepId, StringComparer.Ordinal, ct);
        foreach (var step in steps)
        {
            if (stored.TryGetValue(step.StepId, out var existing)) existing.Move(step.X, step.Y, now);
            else db.BusinessStepLayouts.Add(BusinessStepLayout.Create(scope, scopeId, step.StepId, step.X, step.Y, now));
        }

        var edges = (layout.Edges ?? []).GroupBy(e => (e.From, e.To)).Select(g => g.Last()).ToList();
        var storedEdges = (await db.BusinessEdgeLayouts
            .Where(e => e.Scope == scope && e.ScopeId == scopeId)
            .ToListAsync(ct))
            .ToDictionary(e => (e.FromStep, e.ToStep));
        foreach (var edge in edges)
        {
            var json = JsonSerializer.Serialize(edge.Points ?? [], Json);
            if (storedEdges.TryGetValue((edge.From, edge.To), out var existing)) existing.SetPoints(json, now);
            else db.BusinessEdgeLayouts.Add(BusinessEdgeLayout.Create(scope, scopeId, edge.From, edge.To, json, now));
        }

        await db.SaveChangesAsync(ct);
    }

    // "Reorganizar": apaga as posições e os pontos de dobra do escopo.
    public async Task ResetAsync(LayoutScope scope, Guid scopeId, CancellationToken ct)
    {
        await db.BusinessStepLayouts.Where(s => s.Scope == scope && s.ScopeId == scopeId).ExecuteDeleteAsync(ct);
        await db.BusinessEdgeLayouts.Where(e => e.Scope == scope && e.ScopeId == scopeId).ExecuteDeleteAsync(ct);
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
