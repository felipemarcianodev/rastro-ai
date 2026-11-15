using System.Text;
using Rastro.Core.Graph;
using Rastro.Core.Localization;

namespace Rastro.Core.Prompts;

// Prompt de refatoração a partir de um achado de qualidade (Fase 10). Sem IA: o mesmo achado gera
// sempre o mesmo texto, com o local exato, o caminho do fluxo até ele, o que fazer, o que não
// mexer e os critérios que o Rastro confere ao levantar o fluxo de novo.
public static class RefactorPromptBuilder
{
    public static string Build(PromptContext context, UiLanguage language, FlowGraph graph, Finding finding)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var node = graph.Nodes.FirstOrDefault(n => n.Id == finding.NodeId);
        var label = node?.Label ?? finding.NodeId;
        var text = new StringBuilder();

        text.AppendLine(T("Prompt.Refactor.Title", context.EntryPoint));
        text.AppendLine();
        text.AppendLine(PromptText.RepositoryLine(context, language));
        text.AppendLine(T("Prompt.Refactor.Intro"));
        text.AppendLine();

        text.AppendLine(T("Prompt.Refactor.Problem.Heading"));
        text.AppendLine($"{SeverityText(finding.Severity, language)}: {finding.Message}");
        var location = finding.Location ?? node?.Location;
        if (location is not null) text.AppendLine(T("Prompt.Refactor.Problem.Location", location.File, location.Line));
        if (node?.Symbol is not null) text.AppendLine(T("Prompt.Refactor.Problem.Method", node.Symbol));
        if (node?.Metrics is { } m)
            text.AppendLine(T("Prompt.Refactor.Problem.Metrics", m.Cyclomatic,
                Count(m.Lines, "Prompt.Refactor.Unit.Line", language),
                Count(m.MaxNesting, "Prompt.Refactor.Unit.Level", language),
                Count(m.Dependencies, "Prompt.Refactor.Unit.Dependency", language)));
        var path = GraphQueries.PathsTo(graph, finding.NodeId).FirstOrDefault();
        if (path is { Count: > 1 })
            text.AppendLine(T("Prompt.Refactor.Problem.Path", string.Join(" → ", path.Select(n => $"`{n.Label}`"))));
        text.AppendLine();

        text.AppendLine(T("Prompt.Refactor.Do.Heading"));
        foreach (var key in Guidance(finding.Kind)) text.AppendLine($"- {T(key)}");
        text.AppendLine();

        text.AppendLine(T("Prompt.Refactor.Keep.Heading"));
        text.AppendLine(T("Prompt.Refactor.Keep.Behavior", label));
        text.AppendLine(T("Prompt.Refactor.Keep.Contracts"));
        text.AppendLine(T("Prompt.Refactor.Keep.OtherFindings"));
        text.AppendLine();

        text.AppendLine(T("Prompt.Refactor.Accept.Heading"));
        text.AppendLine($"- {Acceptance(finding.Kind, language)}");
        text.AppendLine(T("Prompt.Refactor.Accept.Build"));
        text.AppendLine(T("Prompt.Refactor.Accept.Resurvey"));
        return text.ToString();
    }

    // Chaves das linhas de orientação de cada tipo de achado.
    private static string[] Guidance(FindingKind kind) => kind switch
    {
        FindingKind.HighComplexity => ["Prompt.Refactor.Do.HighComplexity.1", "Prompt.Refactor.Do.HighComplexity.2"],
        FindingKind.LongMethod => ["Prompt.Refactor.Do.LongMethod.1"],
        FindingKind.DeepNesting => ["Prompt.Refactor.Do.DeepNesting.1"],
        FindingKind.TooManyDependencies => ["Prompt.Refactor.Do.TooManyDependencies.1"],
        FindingKind.SilentCatch => ["Prompt.Refactor.Do.SilentCatch.1", "Prompt.Refactor.Do.SilentCatch.2"],
        FindingKind.ServiceLocator => ["Prompt.Refactor.Do.ServiceLocator.1"],
        FindingKind.EntityChangedOutsideDomain => ["Prompt.Refactor.Do.EntityChangedOutsideDomain.1", "Prompt.Refactor.Do.EntityChangedOutsideDomain.2"],
        FindingKind.PublicSetterOnEntity => ["Prompt.Refactor.Do.PublicSetterOnEntity.1"],
        FindingKind.QueryInLoop => ["Prompt.Refactor.Do.QueryInLoop.1", "Prompt.Refactor.Do.QueryInLoop.2"],
        FindingKind.MaterializeBeforeFilter => ["Prompt.Refactor.Do.MaterializeBeforeFilter.1"],
        FindingKind.ReadWithoutNoTracking => ["Prompt.Refactor.Do.ReadWithoutNoTracking.1"],
        FindingKind.ProcedureByRawSql => ["Prompt.Refactor.Do.ProcedureByRawSql.1", "Prompt.Refactor.Do.ProcedureByRawSql.2"],
        FindingKind.LoadToDelete => ["Prompt.Refactor.Do.LoadToDelete.1", "Prompt.Refactor.Do.LoadToDelete.2"],
        FindingKind.LeadingWildcardLike => ["Prompt.Refactor.Do.LeadingWildcardLike.1", "Prompt.Refactor.Do.LeadingWildcardLike.2"],
        _ => ["Prompt.Refactor.Do.PersistenceOutsideDomain.1", "Prompt.Refactor.Do.PersistenceOutsideDomain.2"]
    };

    private static string Acceptance(FindingKind kind, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);
        return kind switch
        {
            FindingKind.HighComplexity => T("Prompt.Refactor.Accept.HighComplexity", QualityThresholds.ComplexityWarning),
            FindingKind.LongMethod => T("Prompt.Refactor.Accept.LongMethod", QualityThresholds.LinesWarning),
            FindingKind.DeepNesting => T("Prompt.Refactor.Accept.DeepNesting", QualityThresholds.NestingWarning),
            FindingKind.TooManyDependencies => T("Prompt.Refactor.Accept.TooManyDependencies", QualityThresholds.DependenciesWarning),
            FindingKind.SilentCatch => T("Prompt.Refactor.Accept.SilentCatch"),
            FindingKind.ServiceLocator => T("Prompt.Refactor.Accept.ServiceLocator"),
            FindingKind.EntityChangedOutsideDomain => T("Prompt.Refactor.Accept.EntityChangedOutsideDomain"),
            FindingKind.PublicSetterOnEntity => T("Prompt.Refactor.Accept.PublicSetterOnEntity"),
            FindingKind.QueryInLoop => T("Prompt.Refactor.Accept.QueryInLoop"),
            FindingKind.MaterializeBeforeFilter => T("Prompt.Refactor.Accept.MaterializeBeforeFilter"),
            FindingKind.ReadWithoutNoTracking => T("Prompt.Refactor.Accept.ReadWithoutNoTracking"),
            FindingKind.ProcedureByRawSql => T("Prompt.Refactor.Accept.ProcedureByRawSql"),
            FindingKind.LoadToDelete => T("Prompt.Refactor.Accept.LoadToDelete"),
            FindingKind.LeadingWildcardLike => T("Prompt.Refactor.Accept.LeadingWildcardLike"),
            _ => T("Prompt.Refactor.Accept.PersistenceOutsideDomain")
        };
    }

    private static string Count(int value, string unitKey, UiLanguage language) =>
        $"{value} {LocalizedText.Get(value == 1 ? unitKey + ".One" : unitKey + ".Many", language)}";

    public static string SeverityText(FindingSeverity severity, UiLanguage language) => severity switch
    {
        FindingSeverity.Critical => LocalizedText.Get("Prompt.Severity.Critical", language),
        FindingSeverity.Warning => LocalizedText.Get("Prompt.Severity.Warning", language),
        _ => LocalizedText.Get("Prompt.Severity.Info", language)
    };
}
