using System.ComponentModel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Rastro.Core.Architecture;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
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
// As descrições das ferramentas ficam em inglês fixo (o agente as lê uma vez, ao conectar); o texto que elas
// devolvem sai no idioma do config.json (ADR-009), lido a cada chamada.
[McpServerToolType]
public class RastroTools(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    ISolutionSource solutions,
    ISolutionAnalyzer analyzer,
    ProposalService proposals,
    ChangeValidationService validations,
    IOptionsMonitor<RastroOptions> options,
    TimeProvider clock,
    Rastro.Infrastructure.Architecture.DeploymentReader deployments,
    Rastro.Infrastructure.Database.DbSchemaService schemas,
    Rastro.Infrastructure.Database.QueryProposalService queryProposals,
    Rastro.Infrastructure.Database.Live.LiveConnectionStore liveConnections,
    Rastro.Infrastructure.Database.Live.LiveDatabaseService liveDatabase,
    Rastro.Infrastructure.Business.BusinessViewService business,
    Rastro.Infrastructure.Business.BusinessAnnotationService businessAnnotations,
    Rastro.Infrastructure.Links.CodeLinkService codeLinks,
    ILogger<RastroTools> logger)
{
    private const int MaxOutlineLines = 400;

    // Idioma do que as ferramentas devolvem (texto próprio, prompts, resumo da validação, esquema): o do config.json
    // ou o do sistema. Lido ao vivo (IOptionsMonitor): o MCP é um processo longo e a pessoa pode trocar o idioma na
    // tela com ele aberto.
    private UiLanguage Language => EffectiveLanguage.Of(options.CurrentValue);

    private string T(string key, params object[] args) => McpText.Get(key, Language, args);

    [McpServerTool(Name = "list_repositories"), Description("Lists the repositories Rastro can read and the solutions of each one.")]
    public string ListRepositories()
    {
        var text = new StringBuilder();
        foreach (var name in catalog.Names())
        {
            var found = catalog.Solutions(name) ?? [];
            text.AppendLine($"- {name}: {(found.Count == 0 ? T("Repos.NoSolution") : string.Join(", ", found.Take(5)))}");
        }
        return text.Length == 0 ? T("Repos.None") : text.ToString();
    }

    [McpServerTool(Name = "analyze_flow"), Description("Traces the flow of a method (Type.Method) in a solution and saves the trace. It takes a while: it loads the solution with the compiler.")]
    public async Task<string> AnalyzeFlow(
        [Description("Repository name (list_repositories).")] string repository,
        [Description("Solution path relative to the repository, e.g. backend/MySystem.slnx.")] string solutionPath,
        [Description("Entry point: Type.Method or Namespace.Type.Method.")] string entryPoint,
        [Description("Call levels to follow (1 to 40).")] int maxDepth = 15,
        CancellationToken ct = default)
    {
        var limit = options.CurrentValue.Analysis.MaxDepthLimit;
        if (maxDepth < 1 || maxDepth > limit) return T("Analyze.DepthRange", limit);

        var resolved = catalog.Resolve(repository, solutionPath);
        if (resolved.Target is not { } target) return T("Analyze.SolutionError", resolved.Error!);
        if (target.Language != Rastro.Core.Graph.SourceLanguage.DotNet) return T("Analyze.AngularNotHere");

        var head = await git.HeadAsync(target.RepositoryRoot, ct);
        var survey = Survey.Create(target.RepositoryName, target.SolutionRelativePath, entryPoint.Trim(), maxDepth, head?.Commit, head?.Branch, clock.GetUtcNow());
        db.Surveys.Add(survey);
        survey.Start(clock.GetUtcNow());

        try
        {
            survey.Complete(await analyzer.AnalyzeAsync(target, survey.EntryPoint, maxDepth, Language, ct), clock.GetUtcNow());
        }
        catch (AnalysisException ex)
        {
            survey.Fail(ex.Failure, ex.Args, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        // A falha vem em código e parâmetros: o texto sai no idioma de quem usa (a coluna Error guarda o português).
        if (survey.Status == SurveyStatus.Failed)
            return T("Analyze.Failed", survey.Failure is { } failure ? FailureText.Of(failure, survey.ReadFailureArgs(), Language) : survey.Error ?? string.Empty);

        return T("Analyze.Done", survey.Id, survey.NodeCount, survey.UnresolvedCount);
    }

    [McpServerTool(Name = "list_surveys"), Description("Lists the most recent traces.")]
    public async Task<string> ListSurveys([Description("How many to list (up to 50).")] int limit = 10, CancellationToken ct = default)
    {
        var surveys = await db.Surveys.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(limit, 1, 50)).ToListAsync(ct);
        if (surveys.Count == 0) return T("Surveys.None");

        return string.Join('\n', surveys.Select(s =>
            T("Surveys.Row", s.Id, s.EntryPoint, s.Repository, s.SolutionPath, s.Status, s.NodeCount, s.UnresolvedCount)));
    }

    [McpServerTool(Name = "get_flow"), Description("Shows the flow as an indented outline from the entry, with the handle of each node (n1, n2...).")]
    public async Task<string> GetFlow([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");

        var handles = Handles(graph);
        var byId = graph.Nodes.ToDictionary(n => n.Id);
        var outgoing = graph.Edges.ToLookup(e => e.From);
        var printed = new HashSet<string>();
        var lines = new List<string> { T("Flow.Header", graph.EntryPoint, graph.Nodes.Count) };

        void Walk(string id, int depth, string? via)
        {
            if (lines.Count >= MaxOutlineLines) return;
            var node = byId[id];
            var indent = new string(' ', depth * 2);
            var prefix = via is null ? string.Empty : $"({via}) ";
            if (!printed.Add(id))
            {
                lines.Add($"{indent}{prefix}→ {handles[id]} {node.Label} ({T("Flow.AlreadyListed")})");
                return;
            }
            lines.Add($"{indent}{prefix}{handles[id]} {Describe(node)}");
            foreach (var edge in outgoing[id].OrderBy(e => e.Order))
                Walk(edge.To, depth + 1, EdgeTag(edge));
        }

        Walk(graph.EntryNodeId, 0, null);
        if (lines.Count >= MaxOutlineLines) lines.Add(T("Flow.Cut", MaxOutlineLines));
        return string.Join('\n', lines);
    }

    [McpServerTool(Name = "get_flow_map"), Description("Shows the flow map, at the level of a C2 diagram: lanes per system, blocks (entry, step, decision, outside the code) with the handle of the node that names each one, links (sequence, outcome, call with verb and route) and the end-to-end paths. Read-only. Use it to answer questions about the flow, citing the nodes.")]
    public async Task<string> GetFlowMap([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");

        var handles = Handles(graph);
        var map = FlowMapBuilder.Build(graph);
        var text = new StringBuilder();
        foreach (var lane in map.Lanes)
        {
            text.AppendLine(T("Map.Lane", lane.Id, lane.System is null ? string.Empty : $" ({lane.System})"));
            foreach (var block in map.Blocks.Where(b => b.Lane == lane.Id))
            {
                text.AppendLine(T("Map.Block", block.Id, block.Kind, handles.GetValueOrDefault(block.NodeId, block.NodeId), block.Title, block.NodeIds.Count));
                foreach (var outcome in block.Outcomes ?? [])
                    text.AppendLine(T("Map.Outcome", BranchText.Plain(outcome.Label, Language), outcome.To ?? T("Map.Continues"), outcome.Fails ? " " + T("Map.Fails") : outcome.Ends ? " " + T("Map.Ends") : string.Empty));
            }
        }
        text.AppendLine(T("Map.Links"));
        foreach (var link in map.Links)
            text.AppendLine($"  {link.From} -> {link.To} {link.Kind}{(link.Label is null ? string.Empty : $" ({BranchText.Plain(link.Label, Language)})")}{(link.Http is null ? string.Empty : $" {link.Http.Method} {link.Http.Route ?? "?"}")}");
        text.AppendLine(T("Map.Paths", map.PathsTruncated ? T("Map.PathsMore", map.Paths.Count) : string.Empty));
        foreach (var path in map.Paths)
            text.AppendLine($"  {path.Id}{(path.Fails ? " " + T("Map.Fails") : string.Empty)}: {string.Join(" > ", path.Blocks)}");
        return text.ToString();
    }

    [McpServerTool(Name = "get_flow_rules"), Description("Lists the flow rules taken from the decisions in the code, without AI: effect (Blocks ends the flow, SideEffect calls another system or outside, Allows only chooses the path), the rule sentence and the sources (node handle and file:line). A rule marked [duplicated] is implemented in more than one system. Read-only.")]
    public async Task<string> GetFlowRules([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");

        var handles = Handles(graph);
        var rules = FlowRuleExtractor.Extract(graph, FlowMapBuilder.Build(graph), Language);
        if (rules.Count == 0) return T("Rules.None");

        var text = new StringBuilder();
        foreach (var rule in rules)
        {
            text.AppendLine($"- {rule.Id} {rule.Effect}{(rule.Duplicated ? " " + T("Rules.Duplicated") : string.Empty)}{(rule.FromUser ? " " + T("Rules.FromUser") : string.Empty)}: {rule.Text}");
            foreach (var source in rule.Sources)
                text.AppendLine(T("Rules.Source", handles.GetValueOrDefault(source.NodeId, source.NodeId), source.Lane, source.Location is null ? string.Empty : $", {source.Location.File}:{source.Location.Line}"));
        }
        return text.ToString();
    }

    [McpServerTool(Name = "list_boards"), Description("Lists the flow boards (several traces side by side, one lane per trace) with the lanes of each one: order, title, trace id, entry point and repositories. Read-only. Use get_business_view with boardId to read the business view of every lane.")]
    public async Task<string> ListBoards(CancellationToken ct = default)
    {
        var all = await db.Boards.AsNoTracking().OrderByDescending(b => b.UpdatedAt).ToListAsync(ct);
        if (all.Count == 0) return T("Boards.None");

        var lanes = await db.BoardLanes.AsNoTracking().OrderBy(l => l.Order).ToListAsync(ct);
        var surveyIds = lanes.Select(l => l.SurveyId).Distinct().ToList();
        var surveys = await db.Surveys.AsNoTracking().Where(s => surveyIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var segments = (await db.SurveySegments.AsNoTracking().Where(g => surveyIds.Contains(g.SurveyId)).OrderBy(g => g.Order).ToListAsync(ct))
            .ToLookup(g => g.SurveyId);

        var text = new StringBuilder();
        foreach (var board in all)
        {
            var own = lanes.Where(l => l.BoardId == board.Id).ToList();
            text.AppendLine(T("Boards.Row", board.Id, board.Name, own.Count));
            foreach (var lane in own.Where(l => surveys.ContainsKey(l.SurveyId)))
            {
                var survey = surveys[lane.SurveyId];
                var repositories = new[] { survey.Repository }.Concat(segments[survey.Id].Select(g => g.Repository))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                text.AppendLine(T("Boards.Lane", lane.Order + 1, lane.Title, lane.SurveyId, survey.EntryPoint, string.Join(", ", repositories)));
            }
        }
        return text.ToString();
    }

    [McpServerTool(Name = "get_business_view"), Description("Shows the business view of a trace, without AI: only the key steps (Start, Decision, Notification, Rejection, CrossSystem, Step, End) with the business text proposed by rule, the node handle and the code reference (repository · file:line), and the links between them with the branch label. Read-only. Use it to explain the business rules of a flow; the texts are proposals until the user confirms them on screen. Give surveyId for one trace, or boardId (list_boards) for every lane of a flow board, with the board glossary: each lane is the business view of its trace and its steps are cited by the trace id of the lane.")]
    public async Task<string> GetBusinessView(
        [Description("Trace id. Use this or boardId.")] Guid? surveyId = null,
        [Description("Board id (list_boards). Use this or surveyId.")] Guid? boardId = null,
        CancellationToken ct = default)
    {
        if ((surveyId is null) == (boardId is null)) return T("Boards.NeedOne");

        if (boardId is { } board) return await BoardBusinessViewAsync(board, ct);

        if (await GraphAsync(surveyId!.Value, ct) is not { } graph || await business.GetAsync(surveyId.Value, Language, ct) is not { } flow)
            return T("Surveys.NotFound");

        return await RenderBusinessViewAsync(surveyId.Value, graph, flow, ct);
    }

    // Cada faixa é a visão de negócio do levantamento dela; as faixas que ainda não têm grafo dizem isso.
    private async Task<string> BoardBusinessViewAsync(Guid boardId, CancellationToken ct)
    {
        if (await db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boardId, ct) is not { } board) return T("Boards.NotFound");

        var lanes = await db.BoardLanes.AsNoTracking().Where(l => l.BoardId == boardId).OrderBy(l => l.Order).ToListAsync(ct);
        var text = new StringBuilder();
        text.AppendLine(T("Boards.Header", board.Name, lanes.Count));
        if (!string.IsNullOrWhiteSpace(board.Glossary))
        {
            text.AppendLine(T("Boards.Glossary"));
            foreach (var line in board.Glossary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                text.AppendLine($"  {line}");
        }

        foreach (var lane in lanes)
        {
            var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == lane.SurveyId, ct);
            text.AppendLine(T("Boards.LaneHeader", lane.Order + 1, lane.Title, lane.SurveyId, survey?.EntryPoint ?? string.Empty));
            if (await GraphAsync(lane.SurveyId, ct) is { } graph && await business.GetAsync(lane.SurveyId, Language, ct) is { } flow)
                text.Append(await RenderBusinessViewAsync(lane.SurveyId, graph, flow, ct));
            else
                text.AppendLine(T("Boards.LaneNoView"));
        }
        return text.ToString();
    }

    private async Task<string> RenderBusinessViewAsync(Guid surveyId, FlowGraph graph, Rastro.Core.Business.BusinessFlow flow, CancellationToken ct)
    {
        var handles = Handles(graph);
        // Apelido curto de cada passo (b1, b2...), na ordem da visão, para as ligações ficarem legíveis.
        var steps = flow.Steps.Select((step, index) => (step, handle: $"b{index + 1}")).ToList();
        var stepHandle = steps.ToDictionary(s => s.step.Id, s => s.handle);
        // Link para o host pelo commit do levantamento (Fase 56), quando o remote do repositório é reconhecido.
        var templates = (await codeLinks.ForSurveyAsync(surveyId, ct) ?? [])
            .Where(l => l.CommitTemplate is not null)
            .ToDictionary(l => l.Repository, l => l.CommitTemplate!, StringComparer.OrdinalIgnoreCase);
        var text = new StringBuilder();
        text.AppendLine(T("Business.Header", graph.EntryPoint, flow.Steps.Count));
        foreach (var (step, handle) in steps)
        {
            var where = step.Ref is { } r ? $" [{r.Repository} · {r.File}:{r.Line}]" : string.Empty;
            if (step.Ref is { } at && templates.TryGetValue(at.Repository, out var template))
                where += " " + Rastro.Core.Links.CodeLinks.Fill(template, at.File, at.Line);
            var proposed = step.LabelState == Rastro.Core.Business.LabelState.Proposed ? " " + T("Business.Proposed") : string.Empty;
            if (step.CodeChanged) proposed += " " + T("Business.CodeChanged");
            if (step.KindProposal is { } kindProposal) proposed += " " + T("Business.KindProposal", kindProposal.Kind, kindProposal.Reason);
            text.AppendLine(T("Business.Step", handle, step.Kind, step.Label, handles.GetValueOrDefault(step.NodeId, step.NodeId), where, proposed));
        }
        text.AppendLine(T("Business.Links"));
        foreach (var edge in flow.Edges)
        {
            var label = edge.Label is null ? string.Empty : $" ({BranchText.Plain(edge.Label, Language)})";
            var inferred = edge.Inferred ? " " + T("Business.Inferred") : string.Empty;
            text.AppendLine($"  {stepHandle.GetValueOrDefault(edge.From, edge.From)} -> {stepHandle.GetValueOrDefault(edge.To, edge.To)}{label}{inferred}");
        }
        if (flow.CollapsedDecisions > 0) text.AppendLine(T("Business.Collapsed", flow.Steps.Count, flow.CollapsedDecisions));
        return text.ToString();
    }

    [McpServerTool(Name = "propose_business_label"), Description("Proposes the business text of a key step of the business view (get_business_view): a single line of up to 120 characters, in the user's language; a decision is a question that ends with \"?\"; no method, class or variable names, and nothing the code does not show. It shows on the screen as proposed by an agent and only the user confirms it. A text the user already confirmed is never changed. Does not use the compiler: the text is not proof, it is a proposal.")]
    public async Task<string> ProposeBusinessLabel(
        [Description("Trace id.")] Guid surveyId,
        [Description("Step handle (b3, from get_business_view), node handle (n12), node id or step id. Use the step handle to point to the End step.")] string nodeId,
        [Description("The text of the step: one line, up to 120 characters.")] string text,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        var segment = await StepSegmentAsync(surveyId, graph, nodeId, ct);
        if (segment is null) return T("Business.StepNotFound");

        var outcome = await businessAnnotations.ProposeLabelAsync(surveyId, segment, text, Rastro.Core.Business.LabelOrigin.Agent, Language, ct);
        return outcome.Ok
            ? T("Business.LabelProposed", nodeId.Trim(), outcome.Step!.Label)
            : BusinessEditText(outcome.Error);
    }

    [McpServerTool(Name = "propose_business_kind"), Description("Proposes another business type for a key step of the business view (get_business_view): Start, Decision, Notification, Rejection, CrossSystem, Step or End (or the number 0 to 6), with a short reason (up to 300 characters, one line) that points to what the code shows. The drawing does not change: it shows on the screen as a proposal of an agent and only the user confirms or dismisses it. A type the user already confirmed is never changed.")]
    public async Task<string> ProposeBusinessKind(
        [Description("Trace id.")] Guid surveyId,
        [Description("Step handle (b3, from get_business_view), node handle (n12), node id or step id.")] string nodeId,
        [Description("The type: Start, Decision, Notification, Rejection, CrossSystem, Step or End.")] string kind,
        [Description("Why this step is of that type (what in the code shows it).")] string reason,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        var segment = await StepSegmentAsync(surveyId, graph, nodeId, ct);
        if (segment is null) return T("Business.StepNotFound");

        var text = kind.Trim();
        if (!Enum.TryParse<Rastro.Core.Business.BusinessKind>(text, true, out var parsed) || !Enum.IsDefined(parsed))
            return T("Business.KindInvalid");

        var outcome = await businessAnnotations.ProposeKindAsync(surveyId, segment, (int)parsed, reason, Language, ct);
        return outcome.Ok
            ? T("Business.KindProposed", nodeId.Trim(), parsed)
            : BusinessEditText(outcome.Error);
    }

    // b3 (passo da visão de negócio), n12 (nó), id do nó ou Id do passo. Nulo se não apontar para nada.
    private async Task<string?> StepSegmentAsync(Guid surveyId, FlowGraph graph, string reference, CancellationToken ct)
    {
        var text = reference.Trim();
        if (text.Length > 1 && text[0] is 'b' or 'B' && int.TryParse(text[1..], out var index) && index >= 1)
        {
            var flow = await business.GetAsync(surveyId, Language, ct);
            return flow is not null && index <= flow.Steps.Count ? flow.Steps[index - 1].Id : null;
        }
        return FindNode(graph, text)?.Id ?? (text.Length > 0 ? text : null);
    }

    private string BusinessEditText(Rastro.Infrastructure.Business.BusinessEditError error) => error switch
    {
        Rastro.Infrastructure.Business.BusinessEditError.LabelInvalid => T("Business.LabelInvalid"),
        Rastro.Infrastructure.Business.BusinessEditError.KindInvalid => T("Business.KindInvalid"),
        Rastro.Infrastructure.Business.BusinessEditError.ReasonInvalid => T("Business.ReasonInvalid"),
        Rastro.Infrastructure.Business.BusinessEditError.AlreadyConfirmed => T("Business.AlreadyConfirmed"),
        Rastro.Infrastructure.Business.BusinessEditError.SurveyNotFound => T("Surveys.NotFound"),
        _ => T("Business.StepNotFound")
    };

    [McpServerTool(Name = "list_unresolved"), Description("Lists the calls the compiler did not resolve (the gaps to fill), with the reason and where they are.")]
    public async Task<string> ListUnresolved([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");

        var handles = Handles(graph);
        var holes = graph.Nodes.Where(n => n.Kind == NodeKind.Unresolved).ToList();
        if (holes.Count == 0) return T("Unresolved.None");

        var text = new StringBuilder();
        foreach (var hole in holes)
        {
            text.AppendLine(T("Unresolved.Call", handles[hole.Id], hole.Label, Where(hole)));
            text.AppendLine(T("Unresolved.Reason", ReasonText(hole.Reason), hole.Detail is null ? string.Empty : $" ({hole.Detail})"));
            var proposed = graph.Edges.Where(e => e.From == hole.Id).Select(e => graph.Nodes.First(n => n.Id == e.To).Label).ToList();
            if (proposed.Count > 0)
                text.AppendLine(T("Unresolved.Proposed", string.Join(", ", proposed)));
        }
        text.AppendLine(T("Unresolved.Next"));
        return text.ToString();
    }

    [McpServerTool(Name = "list_cross_edges"), Description("Lists the HTTP links between systems of the trace (from, to, verb, route, source and target system, confidence) and the HTTP calls that still have no target (node, reason, verb, route). Read-only. To close a call with no target: search_methods with the target's system and propose_edge with the same system.")]
    public async Task<string> ListCrossEdges([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return T("Surveys.NotFound");

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
        text.AppendLine(T("Cross.Links", links.Count));
        foreach (var edge in links)
        {
            var from = byId[edge.From];
            var to = byId[edge.To];
            text.AppendLine($"- {handles[from.Id]} {from.Label} → {handles[to.Id]} {to.Label}: {HttpText(edge.Http!)} ({SystemOf(from)} → {SystemOf(to)}), {ConfidenceText(edge.Confidence)}");
        }

        text.AppendLine(T("Cross.Open", open.Count));
        foreach (var edge in open)
        {
            var hole = byId[edge.To];
            var place = hole.Location ?? edge.Location;
            text.AppendLine(T("Cross.OpenRow", handles[hole.Id], SystemOf(byId[edge.From]), place is null ? T("Where.Unknown") : $"{place.File}:{place.Line}", HttpText(edge.Http!)));
            text.AppendLine(T("Unresolved.Reason", ReasonText(hole.Reason), hole.Detail is null ? string.Empty : $" ({hole.Detail})"));
        }

        if (survey.ReadCrossRepositories().Count == 0)
            text.AppendLine(T("Cross.NoBridge"));
        return text.ToString();
    }

    [McpServerTool(Name = "get_node_code"), Description("Shows the code around a node (a repository file, with line numbers).")]
    public async Task<string> GetNodeCode(
        [Description("Trace id.")] Guid surveyId,
        [Description("Handle (n12) or node id.")] string node,
        [Description("Lines before and after (up to 40).")] int context = 15,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return T("Surveys.NotFound");
        if (FindNode(graph, node) is not { } found) return T("Node.NotFound", node);
        if (found.Location is null) return T("Node.NoLocation", found.Label);

        var path = catalog.SourceFile(found.System ?? survey.Repository, found.Location.File);
        if (path is null || !File.Exists(path)) return T("Node.FileMissing");

        var all = await File.ReadAllLinesAsync(path, ct);
        context = Math.Clamp(context, 0, 40);
        var start = Math.Max(1, found.Location.Line - context);
        var end = Math.Min(all.Length, found.Location.Line + context);
        var text = new StringBuilder($"{found.Location.File}:{found.Location.Line}\n");
        for (var i = start; i <= end; i++)
            text.AppendLine($"{(i == found.Location.Line ? ">" : " ")}{i,5}  {all[i - 1]}");
        return text.ToString();
    }

    [McpServerTool(Name = "search_methods"), Description("Searches methods with a body in the solution of the trace, by name or Type.Method. Returns file:line, which serves as the evidence in propose_edge.")]
    public async Task<string> SearchMethods(
        [Description("Trace id.")] Guid surveyId,
        [Description("Part of the name, e.g. Notify or EmailNotifier.Notify.")] string query,
        [Description("Optional: bridge repository to search in (for the target of an HTTP call). Without it, searches the repository of the trace.")] string? system = null,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey is null) return T("Surveys.NotFound");

        CatalogResult resolved;
        if (string.IsNullOrWhiteSpace(system) || string.Equals(system.Trim(), survey.Repository, StringComparison.OrdinalIgnoreCase))
        {
            resolved = catalog.Resolve(survey.Repository, survey.SolutionPath);
        }
        else
        {
            var cross = survey.ReadCrossRepositories().FirstOrDefault(r => string.Equals(r, system.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cross is null) return T("Search.NotInBridge", system, survey.ReadCrossRepositories().Count == 0 ? T("Common.None") : string.Join(", ", survey.ReadCrossRepositories()));
            var solution = catalog.DotNetSolutions(cross)?.FirstOrDefault();
            resolved = solution is null ? CatalogResult.Fail(CatalogError.SolutionNotFound) : catalog.Resolve(cross, solution);
        }
        if (resolved.Target is not { } target) return T("Search.Unavailable");
        if (target.Language != Rastro.Core.Graph.SourceLanguage.DotNet) return T("Search.Angular");

        var handle = await solutions.OpenAsync(target, fresh: false, ct);
        var found = await ProofValidator.SearchMethodsAsync(handle.Solution, handle.BaseDirectory, query, 30, ct);
        return found.Count == 0
            ? T("Search.NoMethod", query)
            : string.Join('\n', found.Select(f => $"- {f.Name} ({f.File}:{f.Line})"));
    }

    [McpServerTool(Name = "propose_edge"), Description("Proposes the target of an unresolved node, with evidence: the target method and the file:line where it is declared. The compiler checks it; if it holds, it enters as an inferred edge and the user confirms it on the screen. An HTTP call with no target (list_cross_edges) also goes through here, with the system parameter.")]
    public async Task<string> ProposeEdge(
        [Description("Trace id.")] Guid surveyId,
        [Description("Handle (n12) or id of the unresolved node.")] string node,
        [Description("Target method: Type.Method or Namespace.Type.Method.")] string target,
        [Description("File of the target declaration, relative to the repository (as it appears in search_methods).")] string evidenceFile,
        [Description("Line inside the declaration of the target method.")] int evidenceLine,
        [Description("Why this is the target (what in the code points to it).")] string reason,
        [Description("Optional: when this target runs, if the gap has several (e.g. \"CheckoutCompleted event\"). It becomes the edge label.")] string? when = null,
        [Description("Target repository, required when the node is an HTTP call with no target. It must be among the bridge repositories of the trace. The evidence file is relative to it, and the verb and the route of the call must match those of the method.")] string? system = null,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        if (FindNode(graph, node) is not { } found) return T("Node.NotFound", node);

        var outcome = await proposals.ProposeEdgeAsync(surveyId, found.Id, target, evidenceFile, evidenceLine, reason, ProposalOrigin.Agent, ct, when, system);
        return T(outcome.Accepted ? "Propose.Accepted" : "Propose.Refused") + " " + OutcomeText(outcome);
    }

    [McpServerTool(Name = "describe_node"), Description("Writes the rule of a node, in the user's language, for whoever reads the flow: what it does and, at branches, when each branch happens. It shows on the screen marked as written by an agent.")]
    public async Task<string> DescribeNode(
        [Description("Trace id.")] Guid surveyId,
        [Description("Handle (n12) or node id.")] string node,
        [Description("The rule, in business language, from 10 to 2000 characters. Do not invent what the code does not show.")] string rule,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        if (FindNode(graph, node) is not { } found) return T("Node.NotFound", node);

        var outcome = await proposals.ProposeRuleAsync(surveyId, found.Id, rule, ProposalOrigin.Agent, ct);
        return T(outcome.Accepted ? "Propose.Written" : "Propose.Refused") + " " + OutcomeText(outcome);
    }

    [McpServerTool(Name = "impact_of"), Description("Before changing a method of the flow: shows how it is reached (paths from the entry) and what runs after it (what may break).")]
    public async Task<string> ImpactOf(
        [Description("Trace id.")] Guid surveyId,
        [Description("Handle (n12) or node id.")] string node,
        CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        if (FindNode(graph, node) is not { } found) return T("Node.NotFound", node);

        var handles = Handles(graph);
        var text = new StringBuilder();
        text.AppendLine(T("Impact.Title", found.Label, found.Location is null ? "" : $" [{found.Location.File}:{found.Location.Line}]"));
        var paths = GraphQueries.PathsTo(graph, found.Id);
        text.AppendLine(paths.Count == 0 ? T("Impact.NoPath") : T("Impact.Paths", paths.Count));
        foreach (var path in paths)
            text.AppendLine("- " + string.Join(" → ", path.Select(n => $"{handles[n.Id]} {n.Label}")));
        var downstream = GraphQueries.Downstream(graph, found.Id);
        text.AppendLine(downstream.Count == 0 ? T("Impact.NoDown") : T("Impact.Down", downstream.Count));
        foreach (var next in downstream.Take(40))
            text.AppendLine($"- {handles[next.Id]} {Describe(next)}");
        return text.ToString();
    }

    [McpServerTool(Name = "get_change_prompt"), Description("The prompt of the change the user designed in the draft of the trace: what to change, with file:line, what not to touch and the acceptance criteria. Use it as the specification.")]
    public async Task<string> GetChangePrompt([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == surveyId, ct);
        if (survey?.ReadGraph() is not { } original) return T("Surveys.NotFound");
        if (draft is null) return T("Change.NoDraft");

        return ChangePromptBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), Language, original, draft.ReadGraph());
    }

    [McpServerTool(Name = "check_change"), Description("After applying the change from get_change_prompt: traces the flow again on the current code, checks the criteria of the draft, looks for changes outside the request and runs the configured build and tests of the repository. Returns the score. It takes a while: it loads the solution and compiles.")]
    public async Task<string> CheckChange([Description("Id of the ORIGINAL trace (the one that has the draft).")] Guid surveyId, CancellationToken ct = default)
    {
        var request = await validations.RequestAsync(surveyId, ct);
        if (request.Validation is not { } validation)
            return request.Error switch
            {
                ValidationRequestError.DraftRequired => T("Check.NoDraft"),
                ValidationRequestError.AlreadyRunning => T("Check.Running"),
                _ => T("Surveys.NotFound")
            };

        await validations.RunAsync(validation.Id, ct);
        var done = await db.SurveyValidations.AsNoTracking().FirstAsync(v => v.Id == validation.Id, ct);
        if (done.ReadResult() is not { } result)
            return T("Check.Failed", done.Failure is { } failure ? FailureText.Of(failure, done.ReadFailureArgs(), Language) : done.Error ?? string.Empty);

        return FlowDocumentBuilder.Summary(result, Language) + T("Check.Done", done.NewSurveyId!);
    }

    [McpServerTool(Name = "list_findings"), Description("Quality findings of the flow: metrics above the threshold (complexity, size, nesting, dependencies), a catch that swallows the failure, service locator and DDD violations (entity changed or saved outside the domain, public setter). Each one with the index for get_refactor_prompt.")]
    public async Task<string> ListFindings([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        if (await GraphAsync(surveyId, ct) is not { } graph) return T("Surveys.NotFound");
        if (graph.Findings is null) return T("Findings.OldSurvey");
        if (graph.Findings.Count == 0) return T("Findings.None");

        var handles = Handles(graph);
        var text = new StringBuilder(T("Findings.Count", graph.Findings.Count));
        foreach (var (finding, index) in graph.Findings.Select((f, i) => (f, i)))
        {
            var where = finding.Location is null ? "" : $"  [{finding.Location.File}:{finding.Location.Line}]";
            text.AppendLine($"{index}. {RefactorPromptBuilder.SeverityText(finding.Severity, Language)} ({handles.GetValueOrDefault(finding.NodeId, finding.NodeId)}): {finding.Message}{where}");
        }
        return text.ToString();
    }

    [McpServerTool(Name = "get_refactor_prompt"), Description("Refactoring prompt for a finding from list_findings: the problem with file:line, the flow path to it, what to do, what not to touch and the acceptance criteria.")]
    public async Task<string> GetRefactorPrompt(
        [Description("Trace id.")] Guid surveyId,
        [Description("Index of the finding in list_findings.")] int index,
        CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return T("Surveys.NotFound");
        if (graph.Findings is not { } findings || index < 0 || index >= findings.Count) return T("Findings.OutOfList");

        return RefactorPromptBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), Language, graph, findings[index]);
    }

    [McpServerTool(Name = "get_db_schema"), Description("Database schema of a repository, read from the code (EF Core model, migrations and .sql files; SQL Server or PostgreSQL, according to the repository): tables, columns, keys, indexes, views and procedures, with the file:line of each item and the divergences between sources. It only reads files: no database is queried and no table data is read.")]
    public async Task<string> GetDbSchema(
        [Description("Repository name (list_repositories).")] string repository,
        [Description("Optional: only one table (e.g. orders or dbo.orders). Without it, the whole schema.")] string? table = null,
        CancellationToken ct = default)
    {
        var language = Language;
        var schema = await schemas.ReadAsync(repository, language, ct);
        if (schema is null) return McpText.Get("Db.NotRegistered", language);
        if (schema.Tables.Count == 0 && schema.Views.Count == 0 && schema.Procedures.Count == 0)
            return McpText.Get("Db.NoTables", language);
        return DbSchemaText.Format(schema, string.IsNullOrWhiteSpace(table) ? null : table.Trim(), language);
    }

    [McpServerTool(Name = "analyze_query"), Description("Analyzes a SQL query (SQL Server or PostgreSQL, the dialect of the repository) without executing it and without AI: the bottleneck findings from the SQL reader of the dialect, crossed with the schema read from the repository code (SELECT *, function on the filter column, LIKE or ILIKE with a leading wildcard, :: cast in the filter, OR across columns, column without an index in the filter or the join (a composite index counts), unbounded read filtered only by equality, join without a condition, DISTINCT over a join, correlated subquery, ORDER BY without a limit, NOT IN with a nullable column, UPDATE or DELETE without WHERE), each with the severity (high, medium or low), the snippet and the reason, the tables the query uses and the schema of only those tables (columns, types and indexes), to propose the improvement with propose_query_rewrite. No database is queried.")]
    public async Task<string> AnalyzeQuery(
        [Description("Repository name (list_repositories), to cross with the schema.")] string repository,
        [Description("The SQL query (in the dialect of the repository), as the user wrote it.")] string sql,
        CancellationToken ct = default)
    {
        var language = Language;
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > Rastro.Core.Database.QueryLimits.MaxSqlLength)
            return McpText.Get("Query.TooLong", language);
        var context = await queryProposals.ContextAsync(repository, sql, language, ct);
        return context is null ? McpText.Get("Db.NotRegistered", language) : QueryAnalysisText.Format(context.Analysis, language, context.Schema);
    }

    [McpServerTool(Name = "propose_query_rewrite"), Description("Proposes the improvement of a SQL query (SQL Server or PostgreSQL) already seen in analyze_query: the rewritten query and the suggested indexes, with the reason for each change. Rastro checks it before accepting, without AI: the rewrite must be read by the SQL reader of the dialect, be a single statement of the same operation on the same table (no SET, EXEC, COPY, CALL or DDL), cite only tables and columns that exist and return the same columns, with the same names and in the same order, without adding TOP, LIMIT, OFFSET or FETCH; the index only uses columns that exist and does not repeat an index or a primary key that already starts with the same columns. What does not pass comes back refused with the reason: fix it and propose again. Nothing is applied or executed: the user copies and applies it.")]
    public async Task<string> ProposeQueryRewrite(
        [Description("Repository name (list_repositories).")] string repository,
        [Description("The original query, exactly as it was analyzed.")] string sql,
        [Description("The rewritten query, in the dialect of the repository. Empty when the proposal is only about indexes.")] string? rewrittenSql = null,
        [Description("Each change of the rewrite, with the reason (up to 8).")] QueryChangeInput[]? changes = null,
        [Description("Suggested indexes (up to 5).")] QueryIndexInput[]? indexes = null,
        CancellationToken ct = default)
    {
        var language = Language;
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > Rastro.Core.Database.QueryLimits.MaxSqlLength)
            return McpText.Get("Rewrite.TooLong", language);
        if (string.IsNullOrWhiteSpace(rewrittenSql) && (indexes is null || indexes.Length == 0))
            return McpText.Get("Rewrite.Nothing", language);

        var proposal = new Rastro.Core.Database.QueryProposal(rewrittenSql,
            (changes ?? []).Select(c => new Rastro.Core.Database.QueryChange(c.What ?? string.Empty, c.Why ?? string.Empty)).ToList(),
            (indexes ?? []).Select(i => new Rastro.Core.Database.IndexSuggestion(i.Table ?? string.Empty, i.Columns ?? [], i.Include ?? [], i.Unique, i.Reason ?? string.Empty)).ToList(),
            ProposalOrigin.Agent);
        var context = await queryProposals.ContextAsync(repository, sql, language, ct);
        if (context is null) return McpText.Get("Db.NotRegistered", language);
        if (context.Analysis.Error is not null) return QueryAnalysisText.Format(context.Analysis, language);
        var review = queryProposals.Review(repository, context, sql, proposal, language);
        // Com banco vivo, o custo estimado do plano da original e da reescrita aceita, como a tela mostra.
        if (review.Rewrite is { Accepted: true } rewrite && (await liveConnections.ViewAsync(repository, ct)).Configured)
        {
            try
            {
                review = review with { Plan = await liveDatabase.ComparePlansAsync(repository, sql, rewrite.Sql, language, ct) };
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "A conexão com o banco vivo do repositório {Repository} não pôde ser decifrada pelo servidor MCP.", repository);
                review = review with { Plan = new Rastro.Core.Database.PlanComparison(null, null, McpText.Get("Plan.Undecipherable", language)) };
            }
        }
        return QueryProposalText.Format(review, language);
    }

    [McpServerTool(Name = "get_query_plan"), Description("Estimated plan of a SQL query on the live database the user registered for the repository (SQL Server or PostgreSQL), without executing it: the estimated total cost, the most expensive operators (with the table, the weight and whether it is a scan), the indexes the database itself says are missing and the warnings. The query first goes through the Rastro SQL reader, which refuses anything that is not a single query or data-change statement, with no script or command. The database only returns the plan: no table data is read, and the connection string never appears. Without a registered live database, the answer says so: the registration is screen-only.")]
    public async Task<string> GetQueryPlan(
        [Description("Repository name (list_repositories).")] string repository,
        [Description("The SQL query (in the dialect of the live database), as the user wrote it.")] string sql,
        CancellationToken ct = default)
    {
        var language = Language;
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > Rastro.Core.Database.QueryLimits.MaxSqlLength)
            return McpText.Get("Query.TooLong", language);
        if (catalog.RootOf(repository) is null) return McpText.Get("Db.NotRegistered", language);

        var connection = await liveConnections.ViewAsync(repository, ct);
        if (!connection.Configured) return McpText.Get("Plan.NoLive", language);

        Rastro.Infrastructure.Database.Live.LivePlanResult result;
        try
        {
            result = await liveDatabase.PlanAsync(repository, sql, language, ct);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "A conexão com o banco vivo do repositório {Repository} não pôde ser decifrada pelo servidor MCP.", repository);
            return McpText.Get("Plan.CannotPlan", language, McpText.Get("Plan.Undecipherable", language));
        }

        return result.Outcome switch
        {
            Rastro.Infrastructure.Database.Live.LivePlanOutcome.Ok when result.Plan is { } plan => QueryPlanText.Format(plan, connection, language),
            Rastro.Infrastructure.Database.Live.LivePlanOutcome.NoConnection => McpText.Get("Plan.NoLive", language),
            Rastro.Infrastructure.Database.Live.LivePlanOutcome.NotAllowed => McpText.Get("Plan.NotAllowed", language, (result.Reason ?? McpText.Get("Plan.NotAllowedDefault", language)).TrimEnd('.')),
            _ => McpText.Get("Plan.NoAnswer", language, (result.Reason ?? McpText.Get("Plan.NoAnswerDefault", language)).TrimEnd('.'))
        };
    }

    [McpServerTool(Name = "get_c4"), Description("C4 diagrams of the flow in Structurizr DSL (C1 context, C2 containers, C3 components, C4 dynamic view of the flow). The people in C1 are the ones the user registered on the screen.")]
    public async Task<string> GetC4([Description("Trace id.")] Guid surveyId, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph) return T("Surveys.NotFound");

        var people = (await db.SurveyArchitectures.AsNoTracking().FirstOrDefaultAsync(a => a.SurveyId == surveyId, ct))?.ReadPeople() ?? [];
        // C2 pelos serviços do docker-compose do repositório, quando ele existe (Fase 13).
        var root = catalog.Resolve(survey.Repository, survey.SolutionPath).Target?.RepositoryRoot;
        var deployment = root is null ? null : deployments.Read(root);
        return C4WorkspaceBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), Language, graph, people, deployment);
    }

    private async Task<FlowGraph?> GraphAsync(Guid surveyId, CancellationToken ct) =>
        (await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == surveyId, ct))?.ReadGraph();

    // A recusa vem em código e parâmetros e sai no idioma de quem usa; a aceita, dos dados que a proposta trouxe.
    private string OutcomeText(ProposalOutcome outcome)
    {
        if (outcome.Refusal is { } refusal) return FailureText.Of(refusal, outcome.RefusalArgs, Language);
        if (outcome.Edge is { } edge)
            return edge.Destination is null
                ? T("Propose.EdgeAccepted", edge.Target, edge.File, edge.Line, edge.Nodes)
                : T("Propose.HttpEdgeAccepted", edge.Target, edge.File, edge.Line, edge.Nodes, edge.Destination);
        return outcome.Accepted ? T("Propose.RuleSaved") : outcome.Message;
    }

    private static Dictionary<string, string> Handles(FlowGraph graph) =>
        graph.Nodes.Select((n, i) => (n.Id, Handle: $"n{i + 1}")).ToDictionary(x => x.Id, x => x.Handle);

    internal static FlowNode? FindNode(FlowGraph graph, string reference)
    {
        var text = reference.Trim();
        if (text.Length > 1 && text[0] is 'n' or 'N' && int.TryParse(text[1..], out var index) && index >= 1 && index <= graph.Nodes.Count)
            return graph.Nodes[index - 1];
        return graph.Nodes.FirstOrDefault(n => n.Id == text);
    }

    private string Describe(FlowNode node)
    {
        var text = $"{KindText(node.Kind)}: {node.Label}";
        if (node.Location is not null) text += $"  [{node.Location.File}:{node.Location.Line}]";
        if (node.System is not null) text += T("Node.System", node.System);
        if (node.Kind == NodeKind.Unresolved) text += T("Node.UnresolvedTag", ReasonText(node.Reason));
        if (node.Rule is not null) text += T("Node.RuleLine", node.Rule.Text);
        return text;
    }

    private string Where(FlowNode node) =>
        node.Location is null ? T("Where.Unknown") : $"{node.Location.File}:{node.Location.Line}";

    private string? EdgeTag(FlowEdge edge)
    {
        var parts = new List<string>();
        if (edge.Label is not null) parts.Add(BranchText.Plain(edge.Label, Language));
        if (edge.Confidence == EdgeConfidence.Inferred) parts.Add(T("Edge.Inferred"));
        if (edge.Confidence == EdgeConfidence.Confirmed) parts.Add(T("Edge.Confirmed"));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string HttpText(HttpCall call) => $"{call.Method} {call.Route ?? "?"}";

    private string ConfidenceText(EdgeConfidence confidence) => confidence switch
    {
        EdgeConfidence.Extracted => T("Edge.Extracted"),
        EdgeConfidence.Inferred => T("Edge.Inferred"),
        _ => T("Edge.Confirmed")
    };

    private string KindText(NodeKind kind) => kind switch
    {
        NodeKind.Entry => T("Kind.Entry"),
        NodeKind.Method => T("Kind.Method"),
        NodeKind.Decision => T("Kind.Decision"),
        NodeKind.Loop => T("Kind.Loop"),
        NodeKind.Try => T("Kind.Try"),
        NodeKind.Return => T("Kind.Return"),
        NodeKind.Throw => T("Kind.Throw"),
        NodeKind.External => T("Kind.External"),
        NodeKind.Unresolved => T("Kind.Unresolved"),
        NodeKind.Jump => T("Kind.Jump"),
        _ => kind.ToString()
    };

    private string ReasonText(UnresolvedReason? reason) => reason switch
    {
        UnresolvedReason.MultipleImplementations => T("Reason.MultipleImplementations"),
        UnresolvedReason.NoImplementation => T("Reason.NoImplementation"),
        UnresolvedReason.DynamicDispatch => T("Reason.DynamicDispatch"),
        UnresolvedReason.Delegate => T("Reason.Delegate"),
        UnresolvedReason.MessageDispatch => T("Reason.MessageDispatch"),
        UnresolvedReason.Reflection => T("Reason.Reflection"),
        UnresolvedReason.DepthLimit => T("Reason.DepthLimit"),
        UnresolvedReason.RouteNotResolved => T("Reason.RouteNotResolved"),
        UnresolvedReason.NoMatchingEndpoint => T("Reason.NoMatchingEndpoint"),
        UnresolvedReason.MultipleEndpoints => T("Reason.MultipleEndpoints"),
        _ => T("Reason.Unknown")
    };
}
