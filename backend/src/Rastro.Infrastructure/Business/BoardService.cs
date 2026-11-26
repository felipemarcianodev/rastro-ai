using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Core.Surveys;
using Rastro.Infrastructure.Surveys;

namespace Rastro.Infrastructure.Business;

public enum BoardError
{
    None = 0,
    NotFound = 1,
    NameInvalid = 2,
    GlossaryInvalid = 3,
    LanesInvalid = 4,
    SurveyNotFound = 5,
    SurveyNotReady = 6,
    LaneExists = 7,
    LaneNotFound = 8,
    LayoutInvalid = 9
}

public sealed record BoardResult<T>(BoardError Error, T? Value = default)
{
    public bool Ok => Error == BoardError.None;
    public static BoardResult<T> Fail(BoardError error) => new(error);
}

public sealed record BoardSummary(Guid Id, string Name, int LaneCount, DateTimeOffset UpdatedAt);

// Faixa pronta para a tela: Repositories reúne o repositório do levantamento e o de cada trecho; Flow é a visão de
// negócio do levantamento (nulo quando ainda não há grafo: em análise ou com falha).
public sealed record BoardLaneView(Guid SurveyId, int Order, string Title, string EntryPoint, IReadOnlyList<string> Repositories,
    SurveyStatus Status, bool Stale, BusinessFlow? Flow);

public sealed record BoardDetail(Guid Id, string Name, string? Glossary, IReadOnlyList<BoardLaneView> Lanes, BoardLayoutDto Layout);

// Uma faixa do PUT do quadro: só renomeia, reordena e tira. Title vazio usa o ponto de entrada.
public sealed record BoardLaneInput(Guid SurveyId, int Order, string? Title);

// Passo do levantamento antigo que não existe no novo (a posição dele foi descartada).
public sealed record RemovedStep(string StepId, string NodeId, string Label);

public sealed record LaneReplacement(IReadOnlyList<RemovedStep> RemovedSteps);

public sealed record BoardSuggestTarget(string? Glossary, IReadOnlyList<Guid> SurveyIds);

// Quadro de fluxos (Fase 60): vários levantamentos lado a lado. O quadro vive no banco local e guarda nome, glossário,
// a ordem e o título das faixas; a visão de negócio é a de cada levantamento e a posição dos balões é por quadro.
public class BoardService(
    RastroDbContext db,
    BusinessViewService view,
    BusinessLayoutService layouts,
    SurveyStalenessService staleness,
    TimeProvider clock,
    ILogger<BoardService> logger)
{
    public async Task<IReadOnlyList<BoardSummary>> ListAsync(CancellationToken ct)
    {
        var boards = await db.Boards.AsNoTracking().OrderByDescending(b => b.UpdatedAt).ToListAsync(ct);
        var counts = await db.BoardLanes.AsNoTracking().GroupBy(l => l.BoardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.BoardId, g => g.Count, ct);
        return boards.Select(b => new BoardSummary(b.Id, b.Name, counts.GetValueOrDefault(b.Id), b.UpdatedAt)).ToList();
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Boards.AnyAsync(b => b.Id == id, ct);

    public async Task<BoardResult<Guid>> CreateAsync(string? name, IReadOnlyList<Guid>? surveyIds, IReadOnlyList<string?>? titles, CancellationToken ct)
    {
        var cleanName = BoardLimits.CleanName(name);
        if (cleanName is null) return BoardResult<Guid>.Fail(BoardError.NameInvalid);

        var ids = surveyIds ?? [];
        if (ids.Count is 0 or > BoardLimits.MaxLanes || ids.Distinct().Count() != ids.Count || (titles?.Count ?? 0) > ids.Count)
            return BoardResult<Guid>.Fail(BoardError.LanesInvalid);

        var cleanTitles = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            var title = BoardLimits.CleanTitle(titles is not null && i < titles.Count ? titles[i] : null);
            if (title is null) return BoardResult<Guid>.Fail(BoardError.LanesInvalid);
            cleanTitles.Add(title);
        }

        var surveys = await ReadySurveysAsync(ids, ct);
        if (surveys.Error != BoardError.None) return BoardResult<Guid>.Fail(surveys.Error);

        var now = clock.GetUtcNow();
        var board = Board.Create(cleanName, null, now);
        db.Boards.Add(board);
        for (var i = 0; i < ids.Count; i++)
            db.BoardLanes.Add(BoardLane.Create(board.Id, ids[i], i, TitleOrEntry(cleanTitles[i], surveys.Value![ids[i]])));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Quadro {BoardId} criado com {Lanes} faixas.", board.Id, ids.Count);
        return new BoardResult<Guid>(BoardError.None, board.Id);
    }

    // Nulo quando o quadro não existe.
    public async Task<BoardDetail?> GetAsync(Guid id, UiLanguage language, CancellationToken ct)
    {
        var board = await db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return null;

        var lanes = await db.BoardLanes.AsNoTracking().Where(l => l.BoardId == id).OrderBy(l => l.Order).ToListAsync(ct);
        var surveyIds = lanes.Select(l => l.SurveyId).ToList();
        var surveys = await db.Surveys.AsNoTracking().Where(s => surveyIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var segments = (await db.SurveySegments.AsNoTracking().Where(g => surveyIds.Contains(g.SurveyId)).OrderBy(g => g.Order).ToListAsync(ct))
            .ToLookup(g => g.SurveyId);

        var views = new List<BoardLaneView>();
        foreach (var lane in lanes)
        {
            // A faixa some junto com o levantamento (FK), então o levantamento existe; a guarda cobre a corrida.
            if (!surveys.TryGetValue(lane.SurveyId, out var survey)) continue;
            var laneSegments = segments[survey.Id].ToList();
            var repositories = new[] { survey.Repository }.Concat(laneSegments.Select(g => g.Repository))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var freshness = await staleness.CheckAsync(survey, laneSegments, ct);
            var flow = await view.GetAsync(survey.Id, language, ct);
            views.Add(new BoardLaneView(survey.Id, lane.Order, lane.Title, survey.EntryPoint, repositories, survey.Status, freshness.Stale, flow));
        }
        return new BoardDetail(board.Id, board.Name, board.Glossary, views, await layouts.GetBoardAsync(id, ct));
    }

    public async Task<BoardResult<Guid>> UpdateAsync(Guid id, string? name, string? glossary, IReadOnlyList<BoardLaneInput>? lanes, CancellationToken ct)
    {
        var board = await db.Boards.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return BoardResult<Guid>.Fail(BoardError.NotFound);

        var cleanName = BoardLimits.CleanName(name);
        if (cleanName is null) return BoardResult<Guid>.Fail(BoardError.NameInvalid);
        var (cleanGlossary, glossaryValid) = BoardLimits.CleanGlossary(glossary);
        if (!glossaryValid) return BoardResult<Guid>.Fail(BoardError.GlossaryInvalid);

        var input = lanes ?? [];
        var stored = await db.BoardLanes.Where(l => l.BoardId == id).ToListAsync(ct);
        var byId = stored.ToDictionary(l => l.SurveyId);
        if (input.Count is 0 or > BoardLimits.MaxLanes
            || input.Select(l => l.SurveyId).Distinct().Count() != input.Count
            || input.Any(l => !byId.ContainsKey(l.SurveyId)))
            return BoardResult<Guid>.Fail(BoardError.LanesInvalid);

        var titles = new Dictionary<Guid, string>();
        foreach (var lane in input)
        {
            var title = BoardLimits.CleanTitle(lane.Title);
            if (title is null) return BoardResult<Guid>.Fail(BoardError.LanesInvalid);
            titles[lane.SurveyId] = title;
        }

        var entryPoints = await EntryPointsAsync(titles.Where(t => t.Value.Length == 0).Select(t => t.Key).ToList(), ct);
        var kept = input.OrderBy(l => l.Order).ToList();
        for (var i = 0; i < kept.Count; i++)
        {
            var title = titles[kept[i].SurveyId];
            byId[kept[i].SurveyId].Set(i, title.Length == 0 ? Clip(entryPoints.GetValueOrDefault(kept[i].SurveyId, string.Empty)) : title);
        }

        var removed = stored.Where(l => input.All(i => i.SurveyId != l.SurveyId)).ToList();
        db.BoardLanes.RemoveRange(removed);
        board.Update(cleanName, cleanGlossary, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        foreach (var lane in removed) await layouts.ResetAsync(LayoutScope.Board, id, ct, lane.SurveyId);
        logger.LogInformation("Quadro {BoardId} atualizado ({Lanes} faixas, {Removed} tiradas).", id, kept.Count, removed.Count);
        return new BoardResult<Guid>(BoardError.None, id);
    }

    public async Task<BoardResult<Guid>> AddLaneAsync(Guid id, Guid surveyId, string? title, CancellationToken ct)
    {
        var board = await db.Boards.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return BoardResult<Guid>.Fail(BoardError.NotFound);

        var cleanTitle = BoardLimits.CleanTitle(title);
        if (cleanTitle is null) return BoardResult<Guid>.Fail(BoardError.LanesInvalid);

        var lanes = await db.BoardLanes.AsNoTracking().Where(l => l.BoardId == id).ToListAsync(ct);
        if (lanes.Any(l => l.SurveyId == surveyId)) return BoardResult<Guid>.Fail(BoardError.LaneExists);
        if (lanes.Count >= BoardLimits.MaxLanes) return BoardResult<Guid>.Fail(BoardError.LanesInvalid);

        var surveys = await ReadySurveysAsync([surveyId], ct);
        if (surveys.Error != BoardError.None) return BoardResult<Guid>.Fail(surveys.Error);

        var order = lanes.Count == 0 ? 0 : lanes.Max(l => l.Order) + 1;
        db.BoardLanes.Add(BoardLane.Create(id, surveyId, order, TitleOrEntry(cleanTitle, surveys.Value![surveyId])));
        board.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Faixa do levantamento {SurveyId} adicionada ao quadro {BoardId}.", surveyId, id);
        return new BoardResult<Guid>(BoardError.None, id);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var board = await db.Boards.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return false;

        db.Boards.Remove(board);
        await db.SaveChangesAsync(ct);
        // As faixas saem por cascata (FK); as posições, que não têm FK, saem aqui.
        await layouts.ResetAsync(LayoutScope.Board, id, ct);
        logger.LogInformation("Quadro {BoardId} excluído.", id);
        return true;
    }

    // Faixa e glossário do "Sugerir textos". Nulo quando o quadro não existe.
    public async Task<BoardSuggestTarget?> SuggestTargetAsync(Guid id, CancellationToken ct)
    {
        var board = await db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return null;
        var surveyIds = await db.BoardLanes.AsNoTracking().Where(l => l.BoardId == id).OrderBy(l => l.Order).Select(l => l.SurveyId).ToListAsync(ct);
        return new BoardSuggestTarget(board.Glossary, surveyIds);
    }

    // ---------- posição ----------

    // Quem chama já conferiu os limites (BusinessLayoutLimits). Aqui só se confere que cada faixa é do quadro.
    public async Task<BoardError> SaveLayoutAsync(Guid id, BoardLayoutDto layout, CancellationToken ct)
    {
        if (!await db.Boards.AnyAsync(b => b.Id == id, ct)) return BoardError.NotFound;

        var lanes = (await db.BoardLanes.AsNoTracking().Where(l => l.BoardId == id).Select(l => l.SurveyId).ToListAsync(ct)).ToHashSet();
        if ((layout.Steps ?? []).Any(s => !lanes.Contains(s.LaneSurveyId)) || (layout.Edges ?? []).Any(e => !lanes.Contains(e.LaneSurveyId)))
            return BoardError.LayoutInvalid;

        await layouts.SaveBoardAsync(id, layout, ct);
        return BoardError.None;
    }

    // "Reorganizar": uma faixa (lane) ou, sem ela, o quadro inteiro.
    public async Task<BoardError> ResetLayoutAsync(Guid id, Guid? lane, CancellationToken ct)
    {
        if (!await db.Boards.AnyAsync(b => b.Id == id, ct)) return BoardError.NotFound;
        if (lane is { } laneId && !await db.BoardLanes.AnyAsync(l => l.BoardId == id && l.SurveyId == laneId, ct)) return BoardError.LaneNotFound;

        await layouts.ResetAsync(LayoutScope.Board, id, ct, lane);
        logger.LogInformation("Posições do quadro {BoardId} apagadas.", id);
        return BoardError.None;
    }

    // ---------- troca de levantamento ----------

    // "Levantar de novo": a faixa passa a apontar para o levantamento novo, mantendo a ordem e o título, e as posições
    // dos passos que continuam vão junto. O passo se casa pela chave do nó (o id do passo muda porque leva o
    // levantamento). Devolve os passos do antigo que não existem no novo.
    public async Task<BoardResult<LaneReplacement>> ReplaceLaneAsync(Guid id, Guid oldSurveyId, Guid newSurveyId, UiLanguage language, CancellationToken ct)
    {
        if (!await db.Boards.AnyAsync(b => b.Id == id, ct)) return BoardResult<LaneReplacement>.Fail(BoardError.NotFound);

        var lanes = await db.BoardLanes.Where(l => l.BoardId == id).ToListAsync(ct);
        var lane = lanes.FirstOrDefault(l => l.SurveyId == oldSurveyId);
        if (lane is null) return BoardResult<LaneReplacement>.Fail(BoardError.LaneNotFound);
        if (newSurveyId == oldSurveyId || lanes.Any(l => l.SurveyId == newSurveyId)) return BoardResult<LaneReplacement>.Fail(BoardError.LaneExists);

        var ready = await ReadySurveysAsync([newSurveyId], ct);
        if (ready.Error != BoardError.None) return BoardResult<LaneReplacement>.Fail(ready.Error);

        var oldFlow = await view.GetAsync(oldSurveyId, language, ct);
        var newFlow = await view.GetAsync(newSurveyId, language, ct);
        var newByKey = (newFlow?.Steps ?? []).ToDictionary(s => BusinessViewService.KeyOf(newSurveyId, s), s => s.Id, StringComparer.Ordinal);
        var oldKeyOf = (oldFlow?.Steps ?? []).ToDictionary(s => s.Id, s => BusinessViewService.KeyOf(oldSurveyId, s), StringComparer.Ordinal);
        var map = oldKeyOf.Where(p => newByKey.ContainsKey(p.Value)).ToDictionary(p => p.Key, p => newByKey[p.Value], StringComparer.Ordinal);
        var removed = (oldFlow?.Steps ?? [])
            .Where(s => !newByKey.ContainsKey(oldKeyOf[s.Id]))
            .Select(s => new RemovedStep(s.Id, s.NodeId, s.Label))
            .ToList();

        var now = clock.GetUtcNow();
        var oldSteps = await db.BusinessStepLayouts
            .Where(s => s.Scope == LayoutScope.Board && s.ScopeId == id && s.LaneSurveyId == oldSurveyId).ToListAsync(ct);
        foreach (var step in oldSteps.Where(s => map.ContainsKey(s.StepId)))
            db.BusinessStepLayouts.Add(BusinessStepLayout.Create(LayoutScope.Board, id, map[step.StepId], step.X, step.Y, now, newSurveyId));
        var oldEdges = await db.BusinessEdgeLayouts
            .Where(e => e.Scope == LayoutScope.Board && e.ScopeId == id && e.LaneSurveyId == oldSurveyId).ToListAsync(ct);
        foreach (var edge in oldEdges.Where(e => map.ContainsKey(e.FromStep) && map.ContainsKey(e.ToStep)))
            db.BusinessEdgeLayouts.Add(BusinessEdgeLayout.Create(LayoutScope.Board, id, map[edge.FromStep], map[edge.ToStep], edge.PointsJson, now, newSurveyId));

        db.BusinessStepLayouts.RemoveRange(oldSteps);
        db.BusinessEdgeLayouts.RemoveRange(oldEdges);
        db.BoardLanes.Remove(lane);
        db.BoardLanes.Add(BoardLane.Create(id, newSurveyId, lane.Order, lane.Title));
        (await db.Boards.FirstAsync(b => b.Id == id, ct)).Touch(now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Faixa do quadro {BoardId} trocada do levantamento {OldSurveyId} para {NewSurveyId}: {Kept} passos mantidos, {Removed} saíram.",
            id, oldSurveyId, newSurveyId, map.Count, removed.Count);
        return new BoardResult<LaneReplacement>(BoardError.None, new LaneReplacement(removed));
    }

    // ---------- apoio ----------

    // Todos existem e terminaram (Completed). Falta de um: SurveyNotFound; algum ainda em análise ou com falha: SurveyNotReady.
    private async Task<BoardResult<Dictionary<Guid, Survey>>> ReadySurveysAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var surveys = await db.Surveys.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        if (ids.Any(i => !surveys.ContainsKey(i))) return BoardResult<Dictionary<Guid, Survey>>.Fail(BoardError.SurveyNotFound);
        if (surveys.Values.Any(s => s.Status != SurveyStatus.Completed)) return BoardResult<Dictionary<Guid, Survey>>.Fail(BoardError.SurveyNotReady);
        return new BoardResult<Dictionary<Guid, Survey>>(BoardError.None, surveys);
    }

    private async Task<Dictionary<Guid, string>> EntryPointsAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await db.Surveys.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.EntryPoint, ct);

    private static string TitleOrEntry(string title, Survey survey) => title.Length == 0 ? Clip(survey.EntryPoint) : title;

    private static string Clip(string text) => text.Length <= BoardLimits.MaxTitleLength ? text : text[..BoardLimits.MaxTitleLength];
}
