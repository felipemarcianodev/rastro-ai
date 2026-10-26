namespace Rastro.Core.Graph;

// Métricas de um método do fluxo (Fase 10), calculadas pelo compilador. Dependencies é o maior
// número de parâmetros entre os construtores do tipo dono do método (dependências injetadas).
public record MethodMetrics(int Cyclomatic, int Lines, int MaxNesting, int Dependencies);

public enum FindingKind
{
    HighComplexity = 0,
    LongMethod = 1,
    DeepNesting = 2,
    TooManyDependencies = 3,
    SilentCatch = 4,
    ServiceLocator = 5,
    EntityChangedOutsideDomain = 6,
    PublicSetterOnEntity = 7,
    PersistenceOutsideDomain = 8
}

public enum FindingCategory
{
    Metric = 0,
    CleanCode = 1,
    Ddd = 2
}

public enum FindingSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

// Achado de qualidade num passo do fluxo. NodeId é a prova: o caminho da entrada até ele mostra
// por onde o fluxo chega ao problema. Location aponta a linha exata quando ela é mais precisa
// que o método (um catch, uma atribuição).
public record Finding(FindingKind Kind, FindingSeverity Severity, string NodeId, string Message, SourceLocation? Location = null);

// Limiares das métricas. Ficam no núcleo para a tela, o MCP e a documentação dizerem o mesmo.
public static class QualityThresholds
{
    public const int ComplexityWarning = 10;
    public const int ComplexityCritical = 20;
    public const int LinesWarning = 60;
    public const int LinesCritical = 120;
    public const int NestingWarning = 3;
    public const int DependenciesWarning = 7;

    public static FindingCategory CategoryOf(FindingKind kind) => kind switch
    {
        FindingKind.HighComplexity or FindingKind.LongMethod or FindingKind.DeepNesting or FindingKind.TooManyDependencies => FindingCategory.Metric,
        FindingKind.SilentCatch or FindingKind.ServiceLocator => FindingCategory.CleanCode,
        _ => FindingCategory.Ddd
    };
}
