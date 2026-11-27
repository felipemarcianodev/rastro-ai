using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;

namespace Rastro.Infrastructure.Business;

// O que o draw.io guarda por levantamento e por origem (Fases 65 e 66), no padrão do BusinessLayoutService. Quem chama
// confere antes que o levantamento existe e os limites (DrawioLayoutLimits).
public class DrawioLayoutService(RastroDbContext db, TimeProvider clock, ILogger<DrawioLayoutService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DrawioLayoutDto> GetAsync(Guid surveyId, DrawioSource source, CancellationToken ct)
    {
        var row = await db.DrawioLayouts.AsNoTracking()
            .SingleOrDefaultAsync(l => l.SurveyId == surveyId && l.Source == source, ct);
        if (row is null) return new DrawioLayoutDto([], null, null);
        return new DrawioLayoutDto(ReadCells(surveyId, row.CellsJson), row.AnnotationsXml, row.UpdatedAt);
    }

    // Troca tudo o que havia para a origem. O último valor vence quando o mesmo rastroId vem repetido.
    public async Task SaveAsync(Guid surveyId, DrawioSource source, DrawioLayoutDto layout, CancellationToken ct)
    {
        var cells = (layout.Cells ?? []).GroupBy(c => c.RastroId, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        var json = JsonSerializer.Serialize(cells, Json);
        var annotations = string.IsNullOrEmpty(layout.Annotations) ? null : layout.Annotations;
        var now = clock.GetUtcNow();
        var row = await db.DrawioLayouts.SingleOrDefaultAsync(l => l.SurveyId == surveyId && l.Source == source, ct);
        if (row is null) db.DrawioLayouts.Add(DrawioLayout.Create(surveyId, source, json, annotations, now));
        else row.Replace(json, annotations, now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Desenho do draw.io guardado: levantamento {SurveyId}, origem {Source}, {Cells} balões.", surveyId, source, cells.Count);
    }

    // "Reorganizar": volta ao desenho gerado do código.
    public async Task ResetAsync(Guid surveyId, DrawioSource source, CancellationToken ct)
    {
        await db.DrawioLayouts.Where(l => l.SurveyId == surveyId && l.Source == source).ExecuteDeleteAsync(ct);
        logger.LogInformation("Desenho do draw.io apagado: levantamento {SurveyId}, origem {Source}.", surveyId, source);
    }

    // Texto gravado que não lê (banco editado à mão) vira "nada guardado": o desenho volta ao gerado.
    private IReadOnlyList<DrawioCellDto> ReadCells(Guid surveyId, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<DrawioCellDto>>(json, Json) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Posições do draw.io ilegíveis no levantamento {SurveyId}; o desenho volta ao gerado.", surveyId);
            return [];
        }
    }
}
