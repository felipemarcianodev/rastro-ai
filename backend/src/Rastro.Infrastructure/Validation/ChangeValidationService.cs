using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Core.Prompts;
using Rastro.Core.Surveys;
using Rastro.Core.Validation;
using Rastro.Extractor;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Validation;

public enum ValidationRequestError
{
    SurveyNotFound = 0,
    DraftRequired = 1,
    AlreadyRunning = 2
}

// Falha da validação que o usuário pode ler: o código e os parâmetros vão para a tela.
public class ValidationFailureException(ValidationFailure failure, IReadOnlyList<string>? args = null, Exception? inner = null)
    : Exception(FailureText.Of(failure, args), inner)
{
    public ValidationFailure Failure { get; } = failure;
    public IReadOnlyList<string> Args { get; } = args ?? [];
}

public record ValidationRequestOutcome(SurveyValidation? Validation, ValidationRequestError? Error);

// Validação da mudança (Fase 9), comum à API (fila) e ao MCP (check_change): levanta o fluxo de novo
// no código atual, compara com o rascunho, roda build e testes configurados do repositório e dá a nota.
// Sem IA: o mesmo código e o mesmo rascunho dão sempre o mesmo resultado.
public class ChangeValidationService(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    ISolutionSource solutions,
    IOptionsMonitor<RastroOptions> options,
    TimeProvider clock,
    IValidationCommandRunner commands,
    ILogger<ChangeValidationService> logger)
{

    public async Task<ValidationRequestOutcome> RequestAsync(Guid surveyId, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } original) return new(null, ValidationRequestError.SurveyNotFound);

        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == surveyId, ct);
        if (draft is null || DraftDiff.Compute(original, draft.ReadGraph()).Count == 0)
            return new(null, ValidationRequestError.DraftRequired);

        if (await db.SurveyValidations.AnyAsync(v => v.SurveyId == surveyId && (v.Status == ValidationStatus.Pending || v.Status == ValidationStatus.Running), ct))
            return new(null, ValidationRequestError.AlreadyRunning);

        var validation = SurveyValidation.Create(surveyId, clock.GetUtcNow());
        db.SurveyValidations.Add(validation);
        await db.SaveChangesAsync(ct);
        return new(validation, null);
    }

    // O texto do relatório (descrição dos critérios, build e testes) é gravado junto com o resultado e roda fora
    // do pedido (fila ou MCP), então usa o idioma que vale sem cabeçalho: o do config.json ou o do sistema (ADR-009).
    public async Task RunAsync(Guid validationId, CancellationToken ct) =>
        await RunAsync(validationId, EffectiveLanguage.Of(options.CurrentValue), ct);

    public async Task RunAsync(Guid validationId, UiLanguage language, CancellationToken ct)
    {
        var validation = await db.SurveyValidations.FirstOrDefaultAsync(v => v.Id == validationId, ct);
        if (validation is null || validation.Status != ValidationStatus.Pending)
        {
            logger.LogWarning("Validação {ValidationId} fora do estado esperado (ausente ou já processada).", validationId);
            return;
        }

        validation.Start();
        await db.SaveChangesAsync(ct);

        try
        {
            var survey = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == validation.SurveyId, ct);
            var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == survey.Id, ct)
                        ?? throw new ValidationFailureException(ValidationFailure.DraftDeleted);
            var (newSurvey, result) = await ValidateAsync(survey, survey.ReadGraph()!, draft.ReadGraph(), language, ct);

            validation.Complete(newSurvey.Id, result, clock.GetUtcNow());
            logger.LogInformation("Validação {ValidationId} concluída: {Verdict}, nota {Score}.", validationId, result.Verdict, result.Score);
        }
        catch (ValidationFailureException ex)
        {
            validation.Fail(ex.Failure, ex.Args, clock.GetUtcNow());
            logger.LogWarning(ex, "Validação {ValidationId} falhou.", validationId);
        }
        catch (AnalysisException ex)
        {
            validation.Fail(ValidationFailure.Analysis, FailureText.Nest(ex.Failure, ex.Args), clock.GetUtcNow());
            logger.LogWarning(ex, "Validação {ValidationId} falhou.", validationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Fail(ValidationFailure.Unexpected, null, clock.GetUtcNow());
            logger.LogError(ex, "Validação {ValidationId} falhou com erro inesperado.", validationId);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(Survey NewSurvey, ChangeValidationResult Result)> ValidateAsync(Survey survey, FlowGraph original, FlowGraph draft, UiLanguage language, CancellationToken ct)
    {
        var target = catalog.Resolve(survey.Repository, survey.SolutionPath).Target
                     ?? throw new ValidationFailureException(ValidationFailure.TargetUnavailable);
        if (target.Language != Rastro.Core.Graph.SourceLanguage.DotNet)
            throw new ValidationFailureException(ValidationFailure.AngularUnsupported);

        var head = await git.HeadAsync(target.RepositoryRoot, ct);
        var handle = await solutions.OpenAsync(target, fresh: true, ct);
        FlowGraph after;
        try
        {
            after = await FlowExtractor.ExtractAsync(handle.Solution, survey.EntryPoint,
                new ExtractionOptions { MaxDepth = survey.MaxDepth, BaseDirectory = handle.BaseDirectory }, ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            throw new ValidationFailureException(ValidationFailure.EntryGone, [survey.EntryPoint], ex);
        }
        after = ChangeValidator.CarryRules(original, draft, after);

        // O fluxo novo vira um levantamento comum (aparece na lista e abre no grafo).
        var newSurvey = Survey.Create(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.MaxDepth, head?.Commit, head?.Branch, clock.GetUtcNow());
        newSurvey.Start(clock.GetUtcNow());
        newSurvey.Complete(after, clock.GetUtcNow());
        db.Surveys.Add(newSurvey);

        var repository = options.CurrentValue.Repositories.First(r => string.Equals(r.Name, survey.Repository, StringComparison.OrdinalIgnoreCase));
        var build = await commands.RunAsync(LocalizedText.Get("Validation.Report.BuildName", language), repository, ValidationCommand.Build, handle.BaseDirectory, language, ct);
        // Sem build não há o que testar.
        var tests = build.Status == CheckStatus.Failed
            ? new ValidationCheck(LocalizedText.Get("Validation.Report.TestsName", language), CheckStatus.Skipped, LocalizedText.Get("Validation.Report.TestsSkipped", language))
            : await commands.RunAsync(LocalizedText.Get("Validation.Report.TestsName", language), repository, ValidationCommand.Test, handle.BaseDirectory, language, ct);

        var (expectations, unexpected) = ChangeValidator.Compare(original, draft, after, language);
        var (verdict, score) = ChangeValidator.Judge(expectations, unexpected, build, tests);
        var result = new ChangeValidationResult(expectations, unexpected,
            ChangeValidator.DecisionPoints(original), ChangeValidator.DecisionPoints(after), build, tests, verdict, score);
        return (newSurvey, result);
    }

    // Aprovação do usuário: gera a documentação do fluxo novo. Validação reprovada não vira documentação.
    public async Task<SurveyValidation?> ApproveAsync(Guid validationId, UiLanguage language, CancellationToken ct)
    {
        var validation = await db.SurveyValidations.FirstOrDefaultAsync(v => v.Id == validationId, ct);
        if (validation?.ReadResult() is not { } result || validation.NewSurveyId is not { } newSurveyId || result.Verdict == ValidationVerdict.Rejected)
            return null;

        var survey = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == validation.SurveyId, ct);
        var newSurvey = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == newSurveyId, ct);
        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == survey.Id, ct);
        if (draft is null) return null;

        var now = clock.GetUtcNow();
        var context = new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash);
        var notes = await db.NodeNotes.AsNoTracking().Where(n => n.Repository == survey.Repository).ToDictionaryAsync(n => n.NodeId, n => n.Text, ct);
        validation.Approve(FlowDocumentBuilder.Build(context, language, newSurvey.ReadGraph()!, survey.ReadGraph()!, draft.ReadGraph(), result, newSurvey.CommitHash, now, notes), now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Validação {ValidationId} aprovada; documentação gerada.", validationId);
        return validation;
    }
}
