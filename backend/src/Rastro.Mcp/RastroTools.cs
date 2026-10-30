using System.ComponentModel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Rastro.Core.Architecture;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Core.Surveys;
using Rastro.Extractor;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Proposals;
using Rastro.Infrastructure.Repositories;
using Rastro.Infrastructure.Validation;
using Rastro.Core.Validation;

namespace Rastro.Mcp;

// Modo agente (ADR-001): o agente lê o grafo, olha o código e PROPÕE. Toda proposta passa pelo
// mesmo ProposalService do modo provedor (validador de prova incluso). Confirmar ou descartar é
// sempre do usuário, na tela; não há ferramenta para o agente confirmar a própria proposta.
// Nós são citados por apelido curto (n1, n2...), a posição no grafo, ou pelo id completo.
[McpServerToolType]
public class RastroTools(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    ISolutionSource solutions,
    ISolutionAnalyzer analyzer,
    ProposalService proposals,
    ChangeValidationService validations,
    IOptions<RastroOptions> options,
    TimeProvider clock,
    Rastro.Infrastructure.Architecture.DeploymentReader deployments)
{
    private const int MaxOutlineLines = 400;

    [McpServerTool(Name = "list_repositories"), Description("Lista os repositórios que o Rastro pode ler e as solutions de cada um.")]
    public string ListRepositories()
    {
        var text = new StringBuilder();
        foreach (var name in catalog.Names())
        {
            var found = catalog.Solutions(name) ?? [];
            text.AppendLine($"- {name}: {(found.Count == 0 ? "nenhuma solution encontrada" : string.Join(", ", found.Take(5)))}");
        }
        return text.Length == 0 ? "Nenhum repositório configurado (Rastro:Repositories)." : text.ToString();
    }

    [McpServerTool(Name = "analyze_flow"), Description("Levanta o fluxo de um método (Tipo.Metodo) numa solution e salva o levantamento. Demora: carrega a solution com o compilador.")]
    public async Task<string> AnalyzeFlow(
        [Description("Nome do repositório (list_repositories).")] string repository,
        [Description("Caminho da solution relativo ao repositório, ex.: backend/MeuSistema.slnx.")] string solutionPath,
        [Description("Ponto de entrada: Tipo.Metodo ou Namespace.Tipo.Metodo.")] string entryPoint,
        [Description("Níveis de chamada a seguir (1 a 40).")] int maxDepth = 15,
        CancellationToken ct = default)
    {
        var limit = options.Value.Analysis.MaxDepthLimit;
        if (maxDepth < 1 || maxDepth > limit) return $"maxDepth precisa ficar entre 1 e {limit}.";

        var resolved = catalog.Resolve(repository, solutionPath);
        if (resolved.Target is not { } target) return $"Não consegui usar essa solution: {resolved.Error}.";

        var head = await git.HeadAsync(target.RepositoryRoot, ct);
        var survey = Survey.Create(target.RepositoryName, target.SolutionRelativePath, entryPoint.Trim(), maxDepth, head?.Commit, head?.Branch, clock.GetUtcNow());
        db.Surveys.Add(survey);
        survey.Start(clock.GetUtcNow());

        try
        {
            survey.Complete(await analyzer.AnalyzeAsync(target, survey.EntryPoint, maxDepth, ct), clock.GetUtcNow());
        }
        catch (AnalysisException ex)
        {
            survey.Fail(ex.Message, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        if (survey.Status == SurveyStatus.Failed) return $"A análise falhou: {survey.Error}";

        return $"Levantamento {survey.Id} pronto: {survey.NodeCount} nós, {survey.UnresolvedCount} não resolvidos.\n"
               + "Use list_unresolved para ver os buracos e get_flow para o fluxo inteiro.";
    }

    [McpServerTool(Name = "list_surveys"), Description("Lista os levantamentos mais recentes.")]
    public async Task<string> ListSurveys([Description("Quantos listar (até 50).")] int limit = 10, CancellationToken ct = default)
    {
        var surveys = await db.Surveys.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(limit, 1, 50)).ToListAsync(ct);
        if (surveys.Count == 0) return "Nenhum levantamento ainda. Use analyze_flow.";

        return string.Join('\n', surveys.Select(s =>
            $"- {s.Id} | {s.EntryPoint} | {s.Repository}/{s.SolutionPath} | {s.Status} | {s.NodeCount} nós, {s.UnresolvedCount} não resolvidos"));
    }

    [McpServerTool(Name = "get_flow"), Description("Mostra o fluxo como um sumário indentado a partir da entrada, com o apelido de cada nó (n1, n2...).")]
    public async Task<string> GetFlow([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;

        var handles = Handles(graph);
        var byId = graph.Nodes.ToDictionary(n => n.Id);
        var outgoing = graph.Edges.ToLookup(e => e.From);
        var printed = new HashSet<string>();
        var lines = new List<string> { $"Fluxo de {graph.EntryPoint} ({graph.Nodes.Count} nós). Legenda: [inferida] = proposta aguardando o usuário; [confirmada]." };

        void Walk(string id, int depth, string? via)
        {
            if (lines.Count >= MaxOutlineLines) return;
            var node = byId[id];
            var indent = new string(' ', depth * 2);
            var prefix = via is null ? string.Empty : $"({via}) ";
            if (!printed.Add(id))
            {
                lines.Add($"{indent}{prefix}→ {handles[id]} {node.Label} (já listado acima)");
                return;
            }
            lines.Add($"{indent}{prefix}{handles[id]} {Describe(node)}");
            foreach (var edge in outgoing[id].OrderBy(e => e.Order))
                Walk(edge.To, depth + 1, EdgeTag(edge));
        }

        Walk(graph.EntryNodeId, 0, null);
        if (lines.Count >= MaxOutlineLines) lines.Add($"… sumário cortado em {MaxOutlineLines} linhas.");
        return string.Join('\n', lines);
    }

    [McpServerTool(Name = "list_unresolved"), Description("Lista as chamadas que o compilador não resolveu (os buracos a completar), com o motivo e onde estão.")]
    public async Task<string> ListUnresolved([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;

        var handles = Handles(graph);
        var holes = graph.Nodes.Where(n => n.Kind == NodeKind.Unresolved).ToList();
        if (holes.Count == 0) return "Nenhum buraco: o compilador resolveu todas as chamadas deste fluxo.";

        var text = new StringBuilder();
        foreach (var hole in holes)
        {
            text.AppendLine($"- {handles[hole.Id]} chamada `{hole.Label}` em {Where(hole)}");
            text.AppendLine($"  motivo: {ReasonText(hole.Reason)}{(hole.Detail is null ? string.Empty : $" ({hole.Detail})")}");
            var proposed = graph.Edges.Where(e => e.From == hole.Id).Select(e => graph.Nodes.First(n => n.Id == e.To).Label).ToList();
            if (proposed.Count > 0)
                text.AppendLine($"  destinos já propostos: {string.Join(", ", proposed)}. Proponha outros só se também podem rodar aqui.");
        }
        text.AppendLine("Para completar: veja o código (get_node_code), ache o destino (search_methods) e proponha com prova (propose_edge).");
        return text.ToString();
    }

    [McpServerTool(Name = "list_cross_edges"), Description("Lista as ligações HTTP entre sistemas do levantamento (de, para, verbo, rota, sistema de origem e de destino, confiança) e as chamadas HTTP que ainda estão sem destino (nó, motivo, verbo, rota). Somente leitura. Para fechar uma chamada sem destino: search_methods com o system do destino e propose_edge com o mesmo system.")]
    public async Task<string> ListCrossEdges([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound;

        var handles = Handles(graph);
        var byId = graph.Nodes.ToDictionary(n => n.Id);
        var outgoing = graph.Edges.ToLookup(e => e.From);
        string SystemOf(FlowNode node) => node.System ?? survey.Repository;

        var links = graph.Edges
            .Where(e => e.Http is not null && byId.TryGetValue(e.To, out var to) && (to.Kind != NodeKind.Unresolved || byId[e.From].Kind == NodeKind.Unresolved))
            .ToList();
        // O nó "h:" sem destino: a chamada HTTP que chega nele tem verbo e rota, e nada sai dele.
        var open = graph.Edges
            .Where(e => e.Http is not null && byId.TryGetValue(e.To, out var to) && to.Kind == NodeKind.Unresolved && !outgoing[to.Id].Any())
            .ToList();

        var text = new StringBuilder();
        text.AppendLine($"Ligações HTTP entre sistemas: {links.Count}.");
        foreach (var edge in links)
        {
            var from = byId[edge.From];
            var to = byId[edge.To];
            text.AppendLine($"- {handles[from.Id]} {from.Label} → {handles[to.Id]} {to.Label}: {HttpText(edge.Http!)} ({SystemOf(from)} → {SystemOf(to)}), {ConfidenceText(edge.Confidence)}");
        }

        text.AppendLine($"Chamadas HTTP sem destino: {open.Count}.");
        foreach (var edge in open)
        {
            var hole = byId[edge.To];
            var place = hole.Location ?? edge.Location;
            text.AppendLine($"- {handles[hole.Id]} em {SystemOf(byId[edge.From])}, {(place is null ? "posição desconhecida" : $"{place.File}:{place.Line}")}: {HttpText(edge.Http!)}");
            text.AppendLine($"  motivo: {ReasonText(hole.Reason)}{(hole.Detail is null ? string.Empty : $" ({hole.Detail})")}");
        }

        if (survey.ReadCrossRepositories().Count == 0)
            text.AppendLine("Este levantamento não segue chamadas HTTP para outros repositórios (nenhum repositório na costura).");
        return text.ToString();
    }

    [McpServerTool(Name = "get_node_code"), Description("Mostra o código em volta de um nó (arquivo do repositório, numerado).")]
    public async Task<string> GetNodeCode(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Apelido (n12) ou id do nó.")] string node,
        [Description("Linhas antes e depois (até 40).")] int context = 15,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound;
        if (FindNode(graph, node) is not { } found) return $"Nó {node} não encontrado.";
        if (found.Location is null) return $"{found.Label} não tem posição no código (externo ou estrutural).";

        var path = catalog.SourceFile(found.System ?? survey.Repository, found.Location.File);
        if (path is null || !File.Exists(path)) return "Arquivo não encontrado no repositório.";

        var all = await File.ReadAllLinesAsync(path, ct);
        context = Math.Clamp(context, 0, 40);
        var start = Math.Max(1, found.Location.Line - context);
        var end = Math.Min(all.Length, found.Location.Line + context);
        var text = new StringBuilder($"{found.Location.File}:{found.Location.Line}\n");
        for (var i = start; i <= end; i++)
            text.AppendLine($"{(i == found.Location.Line ? ">" : " ")}{i,5}  {all[i - 1]}");
        return text.ToString();
    }

    [McpServerTool(Name = "search_methods"), Description("Procura métodos com corpo na solution do levantamento, por nome ou Tipo.Metodo. Devolve arquivo:linha, que serve de prova no propose_edge.")]
    public async Task<string> SearchMethods(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Parte do nome, ex.: Notify ou EmailNotifier.Notify.")] string query,
        [Description("Opcional: repositório da costura em que procurar (para o destino de uma chamada HTTP). Sem ele, procura no repositório do levantamento.")] string? system = null,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey is null) return NotFound;

        CatalogResult resolved;
        if (string.IsNullOrWhiteSpace(system) || string.Equals(system.Trim(), survey.Repository, StringComparison.OrdinalIgnoreCase))
        {
            resolved = catalog.Resolve(survey.Repository, survey.SolutionPath);
        }
        else
        {
            var cross = survey.ReadCrossRepositories().FirstOrDefault(r => string.Equals(r, system.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cross is null) return $"{system} não está na costura deste levantamento. Repositórios da costura: {(survey.ReadCrossRepositories().Count == 0 ? "nenhum" : string.Join(", ", survey.ReadCrossRepositories()))}.";
            var solution = catalog.Solutions(cross)?.FirstOrDefault();
            resolved = solution is null ? CatalogResult.Fail(CatalogError.SolutionNotFound) : catalog.Resolve(cross, solution);
        }
        if (resolved.Target is not { } target) return "O repositório ou a solution não estão mais disponíveis.";

        var handle = await solutions.OpenAsync(target, fresh: false, ct);
        var found = await ProofValidator.SearchMethodsAsync(handle.Solution, handle.BaseDirectory, query, 30, ct);
        return found.Count == 0
            ? $"Nenhum método com corpo contém \"{query}\"."
            : string.Join('\n', found.Select(f => $"- {f.Name} ({f.File}:{f.Line})"));
    }

    [McpServerTool(Name = "propose_edge"), Description("Propõe o destino de um nó não resolvido, com prova: o método de destino e o arquivo:linha onde ele está declarado. O compilador confere; se valer, entra como aresta inferida e o usuário confirma na tela. Chamada HTTP sem destino (list_cross_edges) também entra aqui, com o parâmetro system.")]
    public async Task<string> ProposeEdge(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Apelido (n12) ou id do nó não resolvido.")] string node,
        [Description("Método de destino: Tipo.Metodo ou Namespace.Tipo.Metodo.")] string target,
        [Description("Arquivo da declaração do destino, relativo ao repositório (como aparece em search_methods).")] string evidenceFile,
        [Description("Linha dentro da declaração do método de destino.")] int evidenceLine,
        [Description("Por que este é o destino (o que no código indica isso).")] string reason,
        [Description("Opcional: quando este destino roda, se o buraco tem vários (ex.: \"evento CheckoutCompleted\"). Vira o rótulo da aresta.")] string? when = null,
        [Description("Repositório de destino, obrigatório quando o nó é uma chamada HTTP sem destino. Precisa estar entre os repositórios da costura do levantamento. O arquivo da prova é relativo a ele, e o verbo e a rota da chamada precisam casar com os do método.")] string? system = null,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;
        if (FindNode(graph, node) is not { } found) return $"Nó {node} não encontrado.";

        var outcome = await proposals.ProposeEdgeAsync(surveyId, found.Id, target, evidenceFile, evidenceLine, reason, ProposalOrigin.Agent, ct, when, system);
        return (outcome.Accepted ? "Aceita. " : "Recusada. ") + outcome.Message;
    }

    [McpServerTool(Name = "describe_node"), Description("Escreve a regra de um nó em português, para quem lê o fluxo: o que ele faz e, nas bifurcações, quando cada ramo acontece. Aparece na tela marcada como escrita por um agente.")]
    public async Task<string> DescribeNode(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Apelido (n12) ou id do nó.")] string node,
        [Description("A regra, em linguagem de negócio, de 10 a 2000 caracteres. Sem inventar o que o código não mostra.")] string rule,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;
        if (FindNode(graph, node) is not { } found) return $"Nó {node} não encontrado.";

        var outcome = await proposals.ProposeRuleAsync(surveyId, found.Id, rule, ProposalOrigin.Agent, ct);
        return (outcome.Accepted ? "Gravada. " : "Recusada. ") + outcome.Message;
    }

    [McpServerTool(Name = "impact_of"), Description("Antes de alterar um método do fluxo: mostra de onde se chega até ele (caminhos desde a entrada) e o que roda depois dele (o que pode quebrar).")]
    public async Task<string> ImpactOf(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Apelido (n12) ou id do nó.")] string node,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;
        if (FindNode(graph, node) is not { } found) return $"Nó {node} não encontrado.";

        var handles = Handles(graph);
        var text = new StringBuilder();
        text.AppendLine($"Impacto de {found.Label}{(found.Location is null ? "" : $" [{found.Location.File}:{found.Location.Line}]")}");
        var paths = GraphQueries.PathsTo(graph, found.Id);
        text.AppendLine(paths.Count == 0 ? "Não é alcançável a partir da entrada." : $"Chega-se a ele por {paths.Count} caminho(s):");
        foreach (var path in paths)
            text.AppendLine("- " + string.Join(" → ", path.Select(n => $"{handles[n.Id]} {n.Label}")));
        var downstream = GraphQueries.Downstream(graph, found.Id);
        text.AppendLine(downstream.Count == 0 ? "Nada roda depois dele neste fluxo." : $"Depois dele rodam {downstream.Count} passo(s), que podem ser afetados:");
        foreach (var next in downstream.Take(40))
            text.AppendLine($"- {handles[next.Id]} {Describe(next)}");
        return text.ToString();
    }

    [McpServerTool(Name = "get_change_prompt"), Description("Prompt da mudança que o usuário desenhou no rascunho do levantamento: o que mudar, com arquivo:linha, o que não mexer e os critérios de aceite. Use-o como especificação.")]
    public async Task<string> GetChangePrompt([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == surveyId, ct);
        if (survey?.ReadGraph() is not { } original) return NotFound;
        if (draft is null) return "Este levantamento não tem rascunho de mudança. O usuário desenha a mudança no modo \"Editar fluxo\" da tela.";

        return ChangePromptBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), original, draft.ReadGraph());
    }

    [McpServerTool(Name = "check_change"), Description("Depois de aplicar a mudança do get_change_prompt: levanta o fluxo de novo no código atual, confere os critérios do rascunho, procura mudanças fora do pedido e roda o build e os testes configurados do repositório. Devolve a nota. Demora: carrega a solution e compila.")]
    public async Task<string> CheckChange([Description("Id do levantamento ORIGINAL (o que tem o rascunho).")] Guid surveyId, CancellationToken ct = default)
    {
        var request = await validations.RequestAsync(surveyId, ct);
        if (request.Validation is not { } validation)
            return request.Error switch
            {
                ValidationRequestError.DraftRequired => "Este levantamento não tem rascunho com mudanças. O usuário desenha a mudança no modo \"Editar fluxo\" da tela.",
                ValidationRequestError.AlreadyRunning => "Já existe uma validação em andamento para este levantamento. Aguarde e consulte de novo.",
                _ => NotFound
            };

        await validations.RunAsync(validation.Id, ct);
        var done = await db.SurveyValidations.AsNoTracking().FirstAsync(v => v.Id == validation.Id, ct);
        if (done.ReadResult() is not { } result) return $"A validação falhou: {done.Error}";

        return FlowDocumentBuilder.Summary(result) +
               $"\nLevantamento novo: {done.NewSurveyId}. A aprovação (que gera a documentação) é do usuário, na tela.";
    }

    [McpServerTool(Name = "list_findings"), Description("Achados de qualidade do fluxo: métricas acima do limiar (complexidade, tamanho, aninhamento, dependências), catch que engole a falha, service locator e violações de DDD (entidade alterada ou gravada fora do domínio, setter público). Cada um com o índice para o get_refactor_prompt.")]
    public async Task<string> ListFindings([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return NotFound;
        if (graph.Findings is null) return "Este levantamento é anterior à inspeção de qualidade. Levante o fluxo de novo (analyze_flow).";
        if (graph.Findings.Count == 0) return "Nenhum achado de qualidade neste fluxo.";

        var handles = Handles(graph);
        var text = new StringBuilder($"{graph.Findings.Count} achado(s):\n");
        foreach (var (finding, index) in graph.Findings.Select((f, i) => (f, i)))
        {
            var where = finding.Location is null ? "" : $"  [{finding.Location.File}:{finding.Location.Line}]";
            text.AppendLine($"{index}. {RefactorPromptBuilder.SeverityText(finding.Severity)} ({handles.GetValueOrDefault(finding.NodeId, finding.NodeId)}): {finding.Message}{where}");
        }
        return text.ToString();
    }

    [McpServerTool(Name = "get_refactor_prompt"), Description("Prompt de refatoração de um achado do list_findings: o problema com arquivo:linha, o caminho do fluxo até ele, o que fazer, o que não mexer e os critérios de aceite.")]
    public async Task<string> GetRefactorPrompt(
        [Description("Id do levantamento.")] Guid surveyId,
        [Description("Índice do achado no list_findings.")] int index,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound;
        if (graph.Findings is not { } findings || index < 0 || index >= findings.Count) return "Índice fora da lista. Use list_findings.";

        return RefactorPromptBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), graph, findings[index]);
    }

    [McpServerTool(Name = "get_c4"), Description("Diagramas C4 do fluxo em Structurizr DSL (C1 contexto, C2 containers, C3 componentes, C4 visão dinâmica do fluxo). As pessoas do C1 são as que o usuário cadastrou na tela.")]
    public async Task<string> GetC4([Description("Id do levantamento.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound;

        var people = (await db.SurveyArchitectures.AsNoTracking().FirstOrDefaultAsync(a => a.SurveyId == surveyId, ct))?.ReadPeople() ?? [];
        // C2 pelos serviços do docker-compose do repositório, quando ele existe (Fase 13).
        var root = catalog.Resolve(survey.Repository, survey.SolutionPath).Target?.RepositoryRoot;
        var deployment = root is null ? null : deployments.Read(root);
        return C4WorkspaceBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), graph, people, deployment);
    }

    private const string NotFound = "Levantamento não encontrado ou ainda sem grafo. Use list_surveys.";

    private async Task<FlowGraph?> GraphAsync(Guid surveyId, CancellationToken ct) =>
        (await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct))?.ReadGraph();

    private static Dictionary<string, string> Handles(FlowGraph graph) =>
        graph.Nodes.Select((n, i) => (n.Id, Handle: $"n{i + 1}")).ToDictionary(x => x.Id, x => x.Handle);

    internal static FlowNode? FindNode(FlowGraph graph, string reference)
    {
        var text = reference.Trim();
        if (text.Length > 1 && text[0] is 'n' or 'N' && int.TryParse(text[1..], out var index) && index >= 1 && index <= graph.Nodes.Count)
            return graph.Nodes[index - 1];
        return graph.Nodes.FirstOrDefault(n => n.Id == text);
    }

    private static string Describe(FlowNode node)
    {
        var text = $"{KindText(node.Kind)}: {node.Label}";
        if (node.Location is not null) text += $"  [{node.Location.File}:{node.Location.Line}]";
        if (node.System is not null) text += $"  (sistema {node.System})";
        if (node.Kind == NodeKind.Unresolved) text += $"  ← não resolvido: {ReasonText(node.Reason)}";
        if (node.Rule is not null) text += $"\n    regra: {node.Rule.Text}";
        return text;
    }

    private static string Where(FlowNode node) =>
        node.Location is null ? "posição desconhecida" : $"{node.Location.File}:{node.Location.Line}";

    private static string? EdgeTag(FlowEdge edge)
    {
        var parts = new List<string>();
        if (edge.Label is not null) parts.Add(edge.Label);
        if (edge.Confidence == EdgeConfidence.Inferred) parts.Add("inferida");
        if (edge.Confidence == EdgeConfidence.Confirmed) parts.Add("confirmada");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string HttpText(HttpCall call) => $"{call.Method} {call.Route ?? "?"}";

    private static string ConfidenceText(EdgeConfidence confidence) => confidence switch
    {
        EdgeConfidence.Extracted => "extraída",
        EdgeConfidence.Inferred => "inferida",
        _ => "confirmada"
    };

    private static string KindText(NodeKind kind) => kind switch
    {
        NodeKind.Entry => "entrada",
        NodeKind.Method => "método",
        NodeKind.Decision => "decisão",
        NodeKind.Loop => "laço",
        NodeKind.Try => "try",
        NodeKind.Return => "retorno",
        NodeKind.Throw => "exceção",
        NodeKind.External => "externo",
        NodeKind.Unresolved => "não resolvido",
        NodeKind.Jump => "desvio",
        _ => kind.ToString()
    };

    private static string ReasonText(UnresolvedReason? reason) => reason switch
    {
        UnresolvedReason.MultipleImplementations => "mais de uma implementação e nenhum registro de DI decide",
        UnresolvedReason.NoImplementation => "nenhuma implementação na solution",
        UnresolvedReason.DynamicDispatch => "chamada dinâmica ou que não compilou",
        UnresolvedReason.Delegate => "delegate ou evento",
        UnresolvedReason.MessageDispatch => "mensagem para mediator ou barramento (o detalhe é o tipo da mensagem)",
        UnresolvedReason.Reflection => "reflection",
        UnresolvedReason.DepthLimit => "limite de profundidade",
        UnresolvedReason.RouteNotResolved => "chamada HTTP com rota montada em tempo de execução",
        UnresolvedReason.NoMatchingEndpoint => "chamada HTTP sem destino em nenhum repositório da costura",
        UnresolvedReason.MultipleEndpoints => "chamada HTTP que casa com mais de um destino",
        _ => "desconhecido"
    };
}
