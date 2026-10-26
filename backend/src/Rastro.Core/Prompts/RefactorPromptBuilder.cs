using System.Text;
using Rastro.Core.Graph;

namespace Rastro.Core.Prompts;

// Prompt de refatoração a partir de um achado de qualidade (Fase 10). Sem IA: o mesmo achado gera
// sempre o mesmo texto, com o local exato, o caminho do fluxo até ele, o que fazer, o que não
// mexer e os critérios que o Rastro confere ao levantar o fluxo de novo.
public static class RefactorPromptBuilder
{
    public static string Build(PromptContext context, FlowGraph graph, Finding finding)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == finding.NodeId);
        var label = node?.Label ?? finding.NodeId;
        var text = new StringBuilder();

        text.AppendLine($"# Refatoração no fluxo `{context.EntryPoint}`");
        text.AppendLine();
        text.AppendLine($"Repositório `{context.Repository}`, solution `{context.SolutionPath}`" +
                        (context.CommitHash is null ? "." : $", levantado no commit `{context.CommitHash[..Math.Min(7, context.CommitHash.Length)]}`."));
        text.AppendLine("O achado veio do levantamento do fluxo pelo compilador (Roslyn). Corrija só ele.");
        text.AppendLine();

        text.AppendLine("## O problema");
        text.AppendLine($"{SeverityText(finding.Severity)}: {finding.Message}");
        var location = finding.Location ?? node?.Location;
        if (location is not null) text.AppendLine($"- Local: `{location.File}:{location.Line}`");
        if (node?.Symbol is not null) text.AppendLine($"- Método: `{node.Symbol}`");
        if (node?.Metrics is { } m)
            text.AppendLine($"- Métricas do método: complexidade {m.Cyclomatic}, {Count(m.Lines, "linha", "linhas")}, " +
                            $"{Count(m.MaxNesting, "nível", "níveis")} de aninhamento e {Count(m.Dependencies, "dependência injetada", "dependências injetadas")}");
        var path = GraphQueries.PathsTo(graph, finding.NodeId).FirstOrDefault();
        if (path is { Count: > 1 })
            text.AppendLine($"- Como o fluxo chega aqui: {string.Join(" → ", path.Select(n => $"`{n.Label}`"))}");
        text.AppendLine();

        text.AppendLine("## O que fazer");
        foreach (var line in Guidance(finding.Kind)) text.AppendLine($"- {line}");
        text.AppendLine();

        text.AppendLine("## Não mexer");
        text.AppendLine($"- O comportamento observável do fluxo: o que `{label}` faz para quem chama continua igual.");
        text.AppendLine("- Assinaturas públicas, contratos de API e nomes de tabela, salvo se a correção exigir (e então diga por quê).");
        text.AppendLine("- Outros achados do mesmo arquivo: um assunto por mudança.");
        text.AppendLine();

        text.AppendLine("## Critérios de aceite");
        text.AppendLine($"- {Acceptance(finding.Kind)}");
        text.AppendLine("- Build e testes do repositório passando; teste novo cobrindo o comportamento que a refatoração tocou.");
        text.AppendLine("- Ao levantar o fluxo de novo no Rastro, este achado não aparece mais.");
        return text.ToString();
    }

    private static string[] Guidance(FindingKind kind) => kind switch
    {
        FindingKind.HighComplexity =>
        [
            "Quebre o método em métodos menores com nome de regra de negócio, cada um com uma decisão.",
            "Troque cadeias de if/switch por tabela, polimorfismo ou retorno antecipado quando couber.",
        ],
        FindingKind.LongMethod => ["Extraia os blocos que fazem coisas diferentes para métodos com nome do que fazem; o método original vira a sequência deles."],
        FindingKind.DeepNesting => ["Use retorno antecipado (guard clauses) e extraia o corpo dos laços para reduzir os níveis."],
        FindingKind.TooManyDependencies => ["Separe as responsabilidades da classe: agrupe as dependências que andam juntas num serviço próprio e injete esse serviço."],
        FindingKind.SilentCatch =>
        [
            "Registre a falha com log estruturado (identificador do que falhou, sem dado pessoal) ou relance.",
            "Se a falha é esperada, troque o catch genérico pelo tipo exato e diga no código por que é seguro seguir.",
        ],
        FindingKind.ServiceLocator => ["Injete a dependência pelo construtor. Em job ou serviço em segundo plano, abra o escopo e resolva só a raiz do trabalho."],
        FindingKind.EntityChangedOutsideDomain =>
        [
            "Mova a alteração para um método da entidade (ou do serviço de domínio dela) que valide a regra e mantenha o estado consistente.",
            "O código de fora passa a chamar esse método; a propriedade deixa de ter setter público.",
        ],
        FindingKind.PublicSetterOnEntity => ["Torne os setters privados (ou init) e exponha métodos de negócio que mudam o estado com validação."],
        _ =>
        [
            "Leve a gravação para o serviço de domínio da entidade, que valida antes de persistir.",
            "A camada de aplicação passa a chamar o serviço de domínio, não o repositório.",
        ]
    };

    private static string Acceptance(FindingKind kind) => kind switch
    {
        FindingKind.HighComplexity => $"Complexidade ciclomática do método em até {QualityThresholds.ComplexityWarning}.",
        FindingKind.LongMethod => $"Método com até {QualityThresholds.LinesWarning} linhas.",
        FindingKind.DeepNesting => $"No máximo {QualityThresholds.NestingWarning} níveis de blocos um dentro do outro.",
        FindingKind.TooManyDependencies => $"Construtor com até {QualityThresholds.DependenciesWarning} dependências injetadas.",
        FindingKind.SilentCatch => "Nenhum catch sem log, sem relançar e sem usar a exceção.",
        FindingKind.ServiceLocator => "Nenhum GetService ou GetRequiredService fora da raiz de composição.",
        FindingKind.EntityChangedOutsideDomain => "Nenhuma atribuição a propriedade da entidade fora dela e do domínio.",
        FindingKind.PublicSetterOnEntity => "A entidade sem setter público nas propriedades de estado.",
        _ => "Nenhuma gravação da entidade pelo repositório fora do domínio."
    };

    private static string Count(int value, string one, string many) => $"{value} {(value == 1 ? one : many)}";

    public static string SeverityText(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "Crítico",
        FindingSeverity.Warning => "Atenção",
        _ => "Informativo"
    };
}
