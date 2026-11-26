using Microsoft.EntityFrameworkCore;
using Rastro.Core.Surveys;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Surveys;

public sealed record SegmentFreshness(string Repository, bool Stale, string? SurveyCommit, string? CurrentCommit);

// Stale geral = o primeiro trecho ou qualquer outro mudou. Segments tem um item por trecho além do primeiro (Fase 25).
public sealed record SurveyFreshness(bool Stale, string? SurveyCommit, string? CurrentCommit, IReadOnlyList<SegmentFreshness> Segments);

// "O código mudou desde o levantamento?": compara o commit salvo com o HEAD atual de cada repositório (o do
// levantamento e o de cada trecho). Comum à rota /stale e ao quadro de fluxos (Fase 60). O HEAD de cada repositório é
// lido uma vez por instância do serviço (um pedido), mesmo que várias faixas citem o mesmo repositório.
public class SurveyStalenessService(RastroDbContext db, RepositoryCatalog catalog, IGitInfo git)
{
    private readonly Dictionary<string, GitHead?> _heads = new(StringComparer.OrdinalIgnoreCase);

    // Nulo quando o levantamento não existe.
    public async Task<SurveyFreshness?> CheckAsync(Guid surveyId, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey is null) return null;
        var segments = await db.SurveySegments.AsNoTracking().Where(g => g.SurveyId == surveyId).OrderBy(g => g.Order).ToListAsync(ct);
        return await CheckAsync(survey, segments, ct);
    }

    public async Task<SurveyFreshness> CheckAsync(Survey survey, IReadOnlyList<SurveySegment> segments, CancellationToken ct)
    {
        var head = await HeadOfAsync(survey.Repository, ct);
        var stale = survey.CommitHash is not null && head is not null && head.Commit != survey.CommitHash;

        // Um item por trecho além do primeiro (Fase 25): diz qual sistema mudou.
        var items = new List<SegmentFreshness>();
        foreach (var segment in segments.OrderBy(g => g.Order))
        {
            var segmentHead = await HeadOfAsync(segment.Repository, ct);
            var segmentStale = segment.CommitHash is not null && segmentHead is not null && segmentHead.Commit != segment.CommitHash;
            items.Add(new SegmentFreshness(segment.Repository, segmentStale, segment.CommitHash, segmentHead?.Commit));
        }

        return new SurveyFreshness(stale || items.Any(g => g.Stale), survey.CommitHash, head?.Commit, items);
    }

    private async Task<GitHead?> HeadOfAsync(string repository, CancellationToken ct)
    {
        if (_heads.TryGetValue(repository, out var cached)) return cached;
        var root = catalog.RootOf(repository);
        return _heads[repository] = root is null ? null : await git.HeadAsync(root, ct);
    }
}
