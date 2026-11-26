using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rastro.Core.Links;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Links;

// Modelos de link de cada repositório de um levantamento (Fase 56, ADR-010): o do levantamento e os dos trechos,
// cada um com o commit em que foi lido. O remote vem do git da pasta cadastrada e sai sem credencial; o valor lido
// nunca vai para o log. Repositório que saiu do cadastro fica sem link (nem para o host, nem para o editor).
public class CodeLinkService(RastroDbContext db, RepositoryCatalog catalog, IGitInfo git, IOptionsMonitor<RastroOptions> options)
{
    // Nulo quando o levantamento não existe.
    public async Task<IReadOnlyList<RepositoryLinks>?> ForSurveyAsync(Guid surveyId, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey is null) return null;

        var segments = await db.SurveySegments.AsNoTracking()
            .Where(g => g.SurveyId == surveyId)
            .OrderBy(g => g.Order)
            .ToListAsync(ct);
        var versions = new List<(string Repository, string? Commit, string? Branch)> { (survey.Repository, survey.CommitHash, survey.Branch) };
        versions.AddRange(segments.Select(g => (g.Repository, g.CommitHash, g.Branch)));

        var links = new List<RepositoryLinks>();
        foreach (var (repository, commit, branch) in versions.DistinctBy(v => v.Repository, StringComparer.OrdinalIgnoreCase))
            links.Add(await ForRepositoryAsync(repository, commit, branch, ct));
        return links;
    }

    private async Task<RepositoryLinks> ForRepositoryAsync(string repository, string? commit, string? branch, CancellationToken ct)
    {
        if (catalog.RootOf(repository) is not { } root)
            return new RepositoryLinks(repository, RepositoryHostKind.Unknown, null, null, null);

        var remote = CodeLinks.ParseRemote(await git.OriginUrlAsync(root, ct));
        return new RepositoryLinks(
            repository,
            remote?.Host ?? RepositoryHostKind.Unknown,
            CodeLinks.HostTemplate(remote, commit, null),
            CodeLinks.HostTemplate(remote, null, branch),
            CodeLinks.EditorTemplate(root, options.CurrentValue.Editor.UrlTemplate));
    }
}
