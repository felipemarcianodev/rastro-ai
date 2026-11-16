using System.Text.Json;
using Rastro.Core.Failures;
using Rastro.Core.Graph;

namespace Rastro.Core.Validation;

public enum ValidationStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

// Validação de uma mudança: o levantamento original, o levantamento novo (código depois da
// mudança), o resultado e, depois da aprovação do usuário, a documentação gerada.
public class SurveyValidation
{
    public Guid Id { get; private set; }
    public Guid SurveyId { get; private set; }
    public Guid? NewSurveyId { get; private set; }
    public ValidationStatus Status { get; private set; }
    public string? ResultJson { get; private set; }
    public string? Error { get; private set; }

    // Código e parâmetros da falha (a tela traduz por eles); nulos nas validações antigas.
    public ValidationFailure? Failure { get; private set; }
    public string? FailureArgsJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? DocumentMarkdown { get; private set; }

    private SurveyValidation() { }

    public static SurveyValidation Create(Guid surveyId, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), SurveyId = surveyId, Status = ValidationStatus.Pending, CreatedAt = now };

    public void Start() => Status = ValidationStatus.Running;

    public void Complete(Guid newSurveyId, ChangeValidationResult result, DateTimeOffset now)
    {
        NewSurveyId = newSurveyId;
        ResultJson = JsonSerializer.Serialize(result, FlowGraphJson.Options);
        Status = ValidationStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(ValidationFailure failure, IReadOnlyList<string>? args, DateTimeOffset now)
    {
        Failure = failure;
        FailureArgsJson = FailureText.SerializeArgs(args);
        Error = FailureText.Of(failure, args);
        Status = ValidationStatus.Failed;
        CompletedAt = now;
    }

    public void Approve(string markdown, DateTimeOffset now)
    {
        if (Status != ValidationStatus.Completed) throw new InvalidOperationException("Só validação concluída pode ser aprovada.");
        DocumentMarkdown = markdown;
        ApprovedAt = now;
    }

    public IReadOnlyList<string> ReadFailureArgs() => FailureText.ParseArgs(FailureArgsJson);

    public ChangeValidationResult? ReadResult() =>
        ResultJson is null ? null : JsonSerializer.Deserialize<ChangeValidationResult>(ResultJson, FlowGraphJson.Options);
}
