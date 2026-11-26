namespace Rastro.Core.Business;

// Onde a posição vale: na visão de negócio de um levantamento sozinho ou num quadro (Fase 60).
public enum LayoutScope
{
    Survey = 0,
    Board = 1
}

// Posição de um passo-chave na tela livre da visão de negócio (Fase 58). Chave (escopo, id do escopo, passo).
// O tamanho do balão não se guarda: sai do layout da tela.
public class BusinessStepLayout
{
    public LayoutScope Scope { get; private set; }
    public Guid ScopeId { get; private set; }
    public string StepId { get; private set; } = string.Empty;

    // Faixa do quadro (levantamento) a que o passo pertence: o id do passo é por levantamento. No escopo Survey vale
    // Guid.Empty (uma chave primária não aceita nulo).
    public Guid LaneSurveyId { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessStepLayout() { }

    public static BusinessStepLayout Create(LayoutScope scope, Guid scopeId, string stepId, double x, double y, DateTimeOffset now, Guid laneSurveyId = default) =>
        new() { Scope = scope, ScopeId = scopeId, LaneSurveyId = laneSurveyId, StepId = stepId, X = x, Y = y, UpdatedAt = now };

    public void Move(double x, double y, DateTimeOffset now)
    {
        X = x;
        Y = y;
        UpdatedAt = now;
    }
}

// Pontos de dobra de uma ligação entre dois passos. Chave (escopo, id do escopo, origem, destino).
public class BusinessEdgeLayout
{
    public LayoutScope Scope { get; private set; }
    public Guid ScopeId { get; private set; }
    public Guid LaneSurveyId { get; private set; }
    public string FromStep { get; private set; } = string.Empty;
    public string ToStep { get; private set; } = string.Empty;
    public string PointsJson { get; private set; } = "[]";
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessEdgeLayout() { }

    public static BusinessEdgeLayout Create(LayoutScope scope, Guid scopeId, string fromStep, string toStep, string pointsJson, DateTimeOffset now, Guid laneSurveyId = default) =>
        new() { Scope = scope, ScopeId = scopeId, LaneSurveyId = laneSurveyId, FromStep = fromStep, ToStep = toStep, PointsJson = pointsJson, UpdatedAt = now };

    public void SetPoints(string pointsJson, DateTimeOffset now)
    {
        PointsJson = pointsJson;
        UpdatedAt = now;
    }
}

public record LayoutPoint(double X, double Y);

public record StepPositionDto(string StepId, double X, double Y);

public record EdgePointsDto(string From, string To, IReadOnlyList<LayoutPoint> Points);

// Formato da rota de layout (GET e PUT), em camelCase no JSON.
public record BusinessLayoutDto(IReadOnlyList<StepPositionDto> Steps, IReadOnlyList<EdgePointsDto> Edges);

// Limites da gravação em lote.
public static class BusinessLayoutLimits
{
    public const int MaxSteps = 500;
    public const int MaxEdges = 500;
    public const int MaxPointsPerEdge = 20;
    public const int MaxStepIdLength = 64;
    public const double MaxCoordinate = 100000;

    public static bool IsValid(BusinessLayoutDto? layout)
    {
        if (layout is null) return false;
        var steps = layout.Steps ?? [];
        var edges = layout.Edges ?? [];
        if (steps.Count > MaxSteps || edges.Count > MaxEdges) return false;
        foreach (var step in steps)
        {
            if (step is null || !ValidId(step.StepId) || !ValidNumber(step.X) || !ValidNumber(step.Y)) return false;
        }
        foreach (var edge in edges)
        {
            if (edge is null || !ValidId(edge.From) || !ValidId(edge.To)) return false;
            var points = edge.Points ?? [];
            if (points.Count > MaxPointsPerEdge) return false;
            if (points.Any(p => p is null || !ValidNumber(p.X) || !ValidNumber(p.Y))) return false;
        }
        return true;
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxStepIdLength;

    private static bool ValidNumber(double value) => double.IsFinite(value) && value is >= -MaxCoordinate and <= MaxCoordinate;
}
