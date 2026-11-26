using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Rastro.Core.Graph;
using Rastro.Core.Localization;

namespace Rastro.Core.Business;

// Deriva a visão de negócio (passos-chave) de um grafo de fluxo (Fase 55). Função pura e determinística:
// o mesmo grafo, fonte, opções e idioma dão a mesma visão. Sem IA e sem I/O (ADR-001, ADR-010).
//
// Em três etapas:
// 1. Expansão: o grafo vira uma árvore de execução estruturada (sequência, bifurcação, escopo de método,
//    saída), já com os passos-chave dos tipos R3 a R7 marcados.
// 2. Resolução (R2): de trás para frente, cada decisão olha o próximo passo-chave de cada ramo. Só entra
//    quando os ramos levam a lugares diferentes. Decisão que não muda o resultado se funde ao passo de cima.
// 3. Ligação: percurso para frente que cria os passos e as ligações, agrupa os nós de código no passo-chave
//    anterior e marca o fim (R7). Se passar do limite, as decisões mais profundas e sem aviso nem recusa
//    são fundidas primeiro, até caber.
public static class BusinessFlowBuilder
{
    public const int MaxLabelLength = 120;

    // Freio contra grafo gigante: depois disso a expansão para de abrir caminhos (resultado ainda determinístico).
    private const int MaxExpansions = 200_000;

    private const string Ellipsis = "…";
    private const string EndToken = "\0end";
    private const string IfPrefix = "if";
    private const string ElseLabel = "else";
    private const string TryLabel = "try";
    private const string CatchPrefix = "catch";
    private const string FinallyLabel = "finally";
    private const string ReturnPrefix = "return ";
    private const string ThrowPrefix = "throw ";
    private const string BareReturn = "return";
    private const string StatusCodeName = "StatusCode";
    private const string HtmlExtension = ".html";
    private const string TypeScriptExtension = ".ts";
    private const string HttpClientName = "HttpClient";
    private const int HashLength = 16;

    private const string KeyDecision = "business.label.decision";
    private const string KeyScreenBlocked = "business.label.screenBlocked";
    private const string KeyRejection = "business.label.rejection";
    private const string KeyEmail = "business.label.email";
    private const string KeyInApp = "business.label.inApp";
    private const string KeyEnd = "business.label.end";
    private const string EndKeyPrefix = "\0end|";

    private static readonly string[] EmailMarkers = ["Email", "Mail", "Smtp"];
    private static readonly string[] InAppMarkers = ["Notification", "Hub"];
    private static readonly string[] WritePrefixes = ["Post", "Put", "Patch", "Delete"];
    private static readonly Regex StatusFamily = new(@"(^|[^\d])4\d\d([^\d]|$)|Status4\d\d", RegexOptions.CultureInvariant);
    private static readonly Regex StatusConstant = new(@"Status4\d\d\w*", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingIf = new(@"^(if|se|si)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Pura e determinística. Grafo sem o nó de entrada devolve a visão vazia.
    public static BusinessFlow Derive(FlowGraph graph, BusinessSource source, BusinessViewOptions options, UiLanguage language) =>
        new Deriver(graph, source, options, language).Run();

    // ---------- árvore de execução ----------

    private enum ExitKind { Method, Flow }

    private abstract class Item
    {
        // A aresta que chega a este item é inferida.
        public bool Inferred { get; init; }
    }

    private sealed record StepRef(SourceLocation? Location, string? System);

    private sealed class VisitItem(FlowNode node) : Item
    {
        public FlowNode Node { get; } = node;
    }

    private sealed class KeyItem(FlowNode node, BusinessKind kind, string label, StepRef reference) : Item
    {
        public FlowNode Node { get; } = node;
        public BusinessKind Kind { get; } = kind;
        public string Label { get; } = label;
        public StepRef Reference { get; } = reference;
    }

    private sealed class ScopeItem(List<Item> items) : Item
    {
        public List<Item> Items { get; } = items;
    }

    private sealed class ExitItem(ExitKind kind) : Item
    {
        public ExitKind Kind { get; } = kind;
    }

    private sealed class BranchItem(string? label, List<Item> items, bool counts)
    {
        public string? Label { get; } = label;
        public List<Item> Items { get; } = items;

        // Se o ramo conta para decidir se a bifurcação muda o resultado (o catch não conta).
        public bool Counts { get; } = counts;
    }

    private sealed class ForkItem(FlowNode node, bool candidate, bool visitSelf, string? implicitLabel, bool hasImplicit) : Item
    {
        public FlowNode Node { get; } = node;

        // Pode virar passo-chave de decisão (R2). Laço, try e a guarda do template nunca viram.
        public bool Candidate { get; } = candidate;
        public bool VisitSelf { get; } = visitSelf;
        public string? ImplicitLabel { get; } = implicitLabel;
        public bool HasImplicit { get; } = hasImplicit;
        public List<BranchItem> Branches { get; } = [];
        public bool IsKey { get; set; }
        public bool Forced { get; set; }
        public bool? HasAlertCache { get; set; }
    }

    // ---------- derivação ----------

    private sealed class Deriver
    {
        private readonly FlowGraph _graph;
        private readonly BusinessSource _source;
        private readonly BusinessViewOptions _options;
        private readonly UiLanguage _language;
        private readonly Dictionary<string, FlowNode> _nodes;
        private readonly Dictionary<string, List<FlowEdge>> _out;
        private readonly List<Regex> _patterns;
        private readonly HashSet<string> _onPath = new(StringComparer.Ordinal);
        private int _expansions;

        public Deriver(FlowGraph graph, BusinessSource source, BusinessViewOptions options, UiLanguage language)
        {
            _graph = graph;
            _source = source;
            _options = options;
            _language = language;
            _nodes = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes) _nodes.TryAdd(node.Id, node);

            _out = graph.Edges
                .Select((edge, index) => (edge, index))
                .Where(x => x.edge.Change != ChangeKind.Removed && _nodes.ContainsKey(x.edge.From) && _nodes.ContainsKey(x.edge.To))
                .GroupBy(x => x.edge.From, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.edge.Order).ThenBy(x => x.index).Select(x => x.edge).ToList(),
                    StringComparer.Ordinal);

            _patterns = options.NotificationPatterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Glob)
                .ToList();
        }

        public BusinessFlow Run()
        {
            if (!_nodes.TryGetValue(_graph.EntryNodeId, out var entry))
                return new BusinessFlow(_source.SurveyId, _graph.SchemaVersion, [], [], 0);

            var start = new KeyItem(entry, BusinessKind.Start, Clip(entry.Label), new StepRef(entry.Location, entry.System));
            _onPath.Add(entry.Id);
            var body = Children(entry.Id, new Ctx(0, false));
            _onPath.Remove(entry.Id);

            var depths = Depths(entry.Id);
            var forks = new List<ForkItem>();
            CollectForks(body, forks);
            var forced = 0;

            while (true)
            {
                foreach (var fork in forks) fork.IsKey = false;
                Eval(body, [EndToken], [EndToken]);
                var linker = new Linker(this);
                linker.Run(start, body);

                if (linker.StepCount <= _options.MaxStepsPerFlow) return linker.ToFlow(forced);

                var next = forks
                    .Where(f => f.IsKey && !HasAlert(f))
                    .OrderByDescending(f => depths.GetValueOrDefault(f.Node.Id, 0))
                    .ThenBy(f => f.Node.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (next is null) return linker.ToFlow(forced);

                next.Forced = true;
                forced++;
            }
        }

        private sealed record Ctx(int CatchDepth, bool Quiet);

        // ---------- expansão ----------

        private IReadOnlyList<FlowEdge> Out(string id) => _out.TryGetValue(id, out var edges) ? edges : [];

        private List<Item> Children(string nodeId, Ctx ctx)
        {
            var items = new List<Item>();
            foreach (var edge in Out(nodeId)) Expand(edge, ctx, items);
            return items;
        }

        private void Expand(FlowEdge edge, Ctx ctx, List<Item> items)
        {
            if (++_expansions > MaxExpansions) return;
            var child = _nodes[edge.To];
            var inferred = edge.Confidence == EdgeConfidence.Inferred;
            var from = _nodes[edge.From];

            // Corte de ciclo (recursão, laço de chamadas): o nó aparece, mas não se abre de novo.
            if (!_onPath.Add(child.Id))
            {
                items.Add(new VisitItem(child) { Inferred = inferred });
                return;
            }

            if (ctx.Quiet)
            {
                items.Add(new VisitItem(child) { Inferred = inferred });
                items.AddRange(Children(child.Id, ctx));
            }
            else
            {
                ExpandNode(edge, from, child, ctx, inferred, items);
            }

            _onPath.Remove(child.Id);
        }

        private void ExpandNode(FlowEdge edge, FlowNode from, FlowNode child, Ctx ctx, bool inferred, List<Item> items)
        {
            // R5: a chamada entre sistemas é o nó de origem da aresta (quem chama), com "VERBO rota".
            if (edge.Http is not null)
            {
                var reference = edge.Location is not null
                    ? new StepRef(edge.Location, from.System)
                    : new StepRef(from.Location, from.System);
                var label = Clip($"{edge.Http.Method} {edge.Http.Route ?? "?"}");
                items.Add(new KeyItem(from, BusinessKind.CrossSystem, label, reference) { Inferred = inferred });
                items.Add(new VisitItem(child) { Inferred = inferred });
                items.Add(new ScopeItem(Children(child.Id, ctx)));
                return;
            }

            switch (child.Kind)
            {
                case NodeKind.Method:
                case NodeKind.Entry:
                    ExpandMethod(edge, from, child, ctx, inferred, items);
                    return;

                case NodeKind.Decision:
                    ExpandDecision(child, from, ctx, inferred, items);
                    return;

                case NodeKind.Loop:
                {
                    var fork = new ForkItem(child, false, true, null, true) { Inferred = inferred };
                    fork.Branches.Add(new BranchItem(null, Children(child.Id, ctx), true));
                    items.Add(fork);
                    return;
                }

                case NodeKind.Try:
                    ExpandTry(child, ctx, inferred, items);
                    return;

                case NodeKind.Return:
                    if (IsErrorReturn(child) || IsAngularGuardReturn(child))
                    {
                        items.Add(RejectionFor(child, IsAngularGuardReturn(child) && !IsErrorReturn(child), inferred));
                        items.Add(new ExitItem(ExitKind.Flow));
                    }
                    else
                    {
                        items.Add(new VisitItem(child) { Inferred = inferred });
                        items.Add(new ExitItem(ExitKind.Method));
                    }
                    return;

                case NodeKind.Throw:
                    if (ctx.CatchDepth > 0)
                    {
                        // Capturado por um catch do próprio fluxo: o caminho segue pelo catch, sem recusa e sem Fim.
                        items.Add(new VisitItem(child) { Inferred = inferred });
                        items.Add(new ExitItem(ExitKind.Flow));
                    }
                    else
                    {
                        items.Add(RejectionFor(child, false, inferred));
                        items.Add(new ExitItem(ExitKind.Flow));
                    }
                    return;

                case NodeKind.External:
                case NodeKind.Unresolved:
                    items.Add(ExternalItem(edge, from, child, inferred));
                    items.AddRange(Children(child.Id, ctx));
                    return;

                default:
                    items.Add(new VisitItem(child) { Inferred = inferred });
                    items.AddRange(Children(child.Id, ctx));
                    return;
            }
        }

        private void ExpandMethod(FlowEdge edge, FlowNode from, FlowNode child, Ctx ctx, bool inferred, List<Item> items)
        {
            if (child.Kind == NodeKind.Method && Matches(child))
            {
                // R3: o que a notificação faz por dentro fica agrupado nela.
                items.Add(new KeyItem(child, BusinessKind.Notification, NotificationLabel(child),
                    new StepRef(edge.Location ?? child.Location, edge.Location is not null ? from.System : child.System)) { Inferred = inferred });
                items.Add(new ScopeItem(Children(child.Id, ctx with { Quiet = true })));
                return;
            }

            if (IsDbWrite(child))
            {
                // R6: escrita no banco.
                items.Add(new KeyItem(child, BusinessKind.Step, Clip(child.Label), new StepRef(child.Location, child.System)) { Inferred = inferred });
            }
            else
            {
                items.Add(new VisitItem(child) { Inferred = inferred });
            }
            items.Add(new ScopeItem(Children(child.Id, ctx)));
        }

        private Item ExternalItem(FlowEdge edge, FlowNode from, FlowNode child, bool inferred)
        {
            var reference = new StepRef(edge.Location ?? child.Location, edge.Location is not null ? from.System : child.System);
            if (Matches(child))
                return new KeyItem(child, BusinessKind.Notification, NotificationLabel(child), reference) { Inferred = inferred };

            var symbol = Strip(child.Symbol);
            if (child.Kind == NodeKind.External && symbol is not null && symbol.Contains(HttpClientName, StringComparison.Ordinal))
                return new KeyItem(child, BusinessKind.CrossSystem, Clip(child.Label), reference) { Inferred = inferred };

            if (child.Kind == NodeKind.External && symbol is not null
                && WritePrefixes.Any(p => MethodName(symbol).StartsWith(p, StringComparison.Ordinal)))
                return new KeyItem(child, BusinessKind.Step, Clip(child.Label), reference) { Inferred = inferred };

            if (IsDbWrite(child))
                return new KeyItem(child, BusinessKind.Step, Clip(child.Label), reference) { Inferred = inferred };

            return new VisitItem(child) { Inferred = inferred };
        }

        private KeyItem RejectionFor(FlowNode node, bool screen, bool inferred)
        {
            var label = screen
                ? LocalizedText.Get(KeyScreenBlocked, _language)
                : LocalizedText.Get(KeyRejection, _language, RejectionText(node));
            return new KeyItem(node, BusinessKind.Rejection, Clip(label), new StepRef(node.Location, node.System)) { Inferred = inferred };
        }

        private List<(string? Label, List<FlowEdge> Edges)> Groups(string nodeId)
        {
            var groups = new List<(string? Label, List<FlowEdge> Edges)>();
            foreach (var edge in Out(nodeId))
            {
                var label = string.IsNullOrEmpty(edge.Label) ? null : edge.Label;
                var index = groups.FindIndex(g => string.Equals(g.Label, label, StringComparison.Ordinal));
                if (index < 0) groups.Add((label, [edge]));
                else groups[index].Edges.Add(edge);
            }
            return groups;
        }

        private List<Item> Branch(List<FlowEdge> edges, Ctx ctx)
        {
            var items = new List<Item>();
            foreach (var edge in edges) Expand(edge, ctx, items);
            return items;
        }

        private void ExpandDecision(FlowNode node, FlowNode from, Ctx ctx, bool inferred, List<Item> items)
        {
            var groups = Groups(node.Id);
            var inTemplate = node.Location?.File.EndsWith(HtmlExtension, StringComparison.OrdinalIgnoreCase) == true;

            if (inTemplate)
            {
                // Guarda do template: o que ela bloqueia vira recusa "Bloqueado na tela"; o resto segue.
                var guard = new ForkItem(node, false, false, null, false) { Inferred = inferred };
                foreach (var (label, edges) in groups)
                    guard.Branches.Add(new BranchItem(label, Branch(edges, ctx), true));
                guard.Branches.Add(new BranchItem(null,
                    [RejectionFor(node, true, inferred), new ExitItem(ExitKind.Flow)], true));
                items.Add(guard);
                return;
            }

            var needsElse = node.Label.StartsWith(IfPrefix, StringComparison.Ordinal)
                            && groups.All(g => !string.Equals(g.Label, ElseLabel, StringComparison.Ordinal));
            var fork = new ForkItem(node, true, true, needsElse ? ElseLabel : null, needsElse) { Inferred = inferred };
            foreach (var (label, edges) in groups)
                fork.Branches.Add(new BranchItem(label, Branch(edges, ctx), true));
            items.Add(fork);
        }

        private void ExpandTry(FlowNode node, Ctx ctx, bool inferred, List<Item> items)
        {
            var groups = Groups(node.Id);
            var hasCatch = groups.Any(g => IsCatch(g.Label));
            var inner = ctx with { CatchDepth = ctx.CatchDepth + (hasCatch ? 1 : 0) };

            List<Item> Finally()
            {
                var result = new List<Item>();
                foreach (var (label, edges) in groups)
                    if (string.Equals(label, FinallyLabel, StringComparison.Ordinal))
                        result.AddRange(Branch(edges, ctx));
                return result;
            }

            var fork = new ForkItem(node, false, true, null, false) { Inferred = inferred };

            var main = new List<Item>();
            foreach (var (label, edges) in groups)
                if (!IsCatch(label) && !string.Equals(label, FinallyLabel, StringComparison.Ordinal))
                    main.AddRange(Branch(edges, inner));
            main.AddRange(Finally());
            fork.Branches.Add(new BranchItem(null, main, true));

            foreach (var (label, edges) in groups)
            {
                if (!IsCatch(label)) continue;
                var body = Branch(edges, ctx);
                body.AddRange(Finally());
                fork.Branches.Add(new BranchItem(label, body, false));
            }
            items.Add(fork);
        }

        private static bool IsCatch(string? label) =>
            label is not null && label.StartsWith(CatchPrefix, StringComparison.Ordinal);

        // ---------- critérios dos tipos ----------

        private bool Matches(FlowNode node)
        {
            var key = TypeMethod(node.Symbol);
            return key is not null && _patterns.Any(p => p.IsMatch(key));
        }

        private static bool IsDbWrite(FlowNode node) =>
            node.Db is not null && node.Db.Any(a => a.Operation != DbOperation.Read);

        // Texto da recusa, sem "return" nem "throw": o literal de texto do primeiro argumento
        // (BadRequest("Fora do prazo.")), senão o nome do tipo de retorno ou da exceção.
        private static string RejectionText(FlowNode node)
        {
            if (node.Kind == NodeKind.Throw)
            {
                var thrown = TrimPrefix(TrimPrefix(node.Label.Trim(), ThrowPrefix), "new ");
                var stop = thrown.IndexOfAny(['(', '{', ' ', ';']);
                return stop < 0 ? thrown : thrown[..stop];
            }

            var expression = ReturnExpression(node);
            if (expression is null) return node.Label;

            // O literal de texto em qualquer argumento (o primeiro que houver).
            var quote = expression.IndexOf('"');
            if (quote >= 0)
            {
                var literal = FirstStringLiteral(expression[quote..]);
                if (!string.IsNullOrWhiteSpace(literal)) return literal;
            }
            var constant = StatusConstant.Match(expression);
            if (constant.Success) return constant.Value;
            var name = TypeName(expression);
            return name.Length == 0 ? node.Label : name;
        }

        private static string TypeName(string expression)
        {
            var end = expression.IndexOfAny(['(', '{', ' ', ';', '<']);
            var head = end < 0 ? expression : expression[..end];
            return head[(head.LastIndexOf('.') + 1)..];
        }

        private static string? FirstStringLiteral(string arguments)
        {
            var text = arguments.TrimStart();
            while (text.StartsWith('$') || text.StartsWith('@')) text = text[1..];
            if (!text.StartsWith('"')) return null;

            var builder = new StringBuilder();
            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                if (c == (char)92 && i + 1 < text.Length)
                {
                    builder.Append(text[++i]);
                    continue;
                }
                if (c == '"') return builder.ToString().Trim();
                builder.Append(c);
            }
            // Texto cortado pelo extrator: aproveita o que há, sem as reticências.
            return builder.ToString().TrimEnd('…', ')', ' ');
        }

        private static string? ReturnExpression(FlowNode node)
        {
            if (!node.Label.StartsWith(ReturnPrefix, StringComparison.Ordinal)) return null;
            var expression = node.Label[ReturnPrefix.Length..].Trim();
            expression = TrimPrefix(expression, "await ");
            expression = TrimPrefix(expression, "new ");
            expression = TrimPrefix(expression, "Task.FromResult(");
            expression = TrimPrefix(expression, "ValueTask.FromResult(");
            return expression;
        }

        private bool IsErrorReturn(FlowNode node)
        {
            var expression = ReturnExpression(node);
            if (expression is null) return false;

            // Constante de status 4xx em qualquer argumento (Error(StatusCodes.Status400BadRequest, ...)).
            if (StatusConstant.IsMatch(expression)) return true;

            var name = TypeName(expression);
            if (name.Length == 0) return false;

            if (string.Equals(name, StatusCodeName, StringComparison.Ordinal))
            {
                var open = expression.IndexOf('(');
                if (open < 0) return false;
                var close = expression.IndexOfAny([',', ')'], open);
                var argument = close < 0 ? expression[(open + 1)..] : expression[(open + 1)..close];
                return StatusFamily.IsMatch(argument);
            }

            return _options.RejectionReturnTypes.Any(t => !string.IsNullOrWhiteSpace(t)
                && name.StartsWith(t, StringComparison.OrdinalIgnoreCase));
        }

        // Angular: o `if (...) return;` antes da chamada HTTP bloqueia na tela.
        private static bool IsAngularGuardReturn(FlowNode node) =>
            string.Equals(node.Label, BareReturn, StringComparison.Ordinal)
            && node.Location?.File.EndsWith(TypeScriptExtension, StringComparison.OrdinalIgnoreCase) == true;

        private static string TrimPrefix(string text, string prefix) =>
            text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].TrimStart() : text;

        // ---------- texto ----------

        private string NotificationLabel(FlowNode node)
        {
            var symbol = TypeMethod(node.Symbol) ?? node.Label;
            if (EmailMarkers.Any(m => symbol.Contains(m, StringComparison.OrdinalIgnoreCase)))
                return LocalizedText.Get(KeyEmail, _language);
            if (InAppMarkers.Any(m => symbol.Contains(m, StringComparison.OrdinalIgnoreCase)))
                return LocalizedText.Get(KeyInApp, _language);
            return Clip(node.Label);
        }

        public string DecisionLabel(FlowNode node)
        {
            var text = (node.Condition ?? node.Label).Trim();
            if (text.StartsWith(IfPrefix, StringComparison.Ordinal))
            {
                var rest = text[IfPrefix.Length..].TrimStart();
                if (rest.StartsWith('(') && rest.EndsWith(')')) text = rest[1..^1].Trim();
                else text = LeadingIf.Replace(text, string.Empty);
            }
            else
            {
                text = LeadingIf.Replace(text, string.Empty);
            }
            text = text.TrimEnd('?', ' ');
            return Clip(LocalizedText.Get(KeyDecision, _language, text));
        }

        private static string Clip(string text) =>
            text.Length <= MaxLabelLength ? text : text[..(MaxLabelLength - Ellipsis.Length)] + Ellipsis;

        // ---------- símbolo e glob ----------

        // Tira os parâmetros: "A.B.Tipo.Metodo(string)" vira "A.B.Tipo.Metodo".
        private static string? Strip(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            var open = symbol.IndexOf('(');
            return open < 0 ? symbol : symbol[..open];
        }

        // Tipo.Método sem namespace e sem parâmetros; ponto dentro de <...> não separa.
        internal static string? TypeMethod(string? symbol)
        {
            var text = Strip(symbol);
            if (text is null) return null;

            var dots = new List<int>();
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '<': depth++; break;
                    case '>': depth = Math.Max(0, depth - 1); break;
                    case '.' when depth == 0: dots.Add(i); break;
                }
            }
            return dots.Count < 2 ? text : text[(dots[^2] + 1)..];
        }

        private static string MethodName(string stripped)
        {
            var typeMethod = TypeMethod(stripped) ?? stripped;
            var index = typeMethod.LastIndexOf('.');
            return index < 0 ? typeMethod : typeMethod[(index + 1)..];
        }

        private static Regex Glob(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---------- profundidade e aviso ----------

        private Dictionary<string, int> Depths(string entryId)
        {
            var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [entryId] = 0 };
            var queue = new Queue<string>([entryId]);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var edge in Out(id))
                    if (depths.TryAdd(edge.To, depths[id] + 1))
                        queue.Enqueue(edge.To);
            }
            return depths;
        }

        private static void CollectForks(List<Item> items, List<ForkItem> forks)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case ScopeItem scope:
                        CollectForks(scope.Items, forks);
                        break;
                    case ForkItem fork:
                        if (fork.Candidate) forks.Add(fork);
                        foreach (var branch in fork.Branches) CollectForks(branch.Items, forks);
                        break;
                }
            }
        }

        // A decisão leva a aviso ou recusa dentro dos próprios ramos?
        private static bool HasAlert(ForkItem fork) =>
            fork.HasAlertCache ??= fork.Branches.Any(b => ContainsAlert(b.Items));

        private static bool ContainsAlert(List<Item> items) =>
            items.Any(i => i switch
            {
                KeyItem k => k.Kind is BusinessKind.Notification or BusinessKind.Rejection,
                ScopeItem s => ContainsAlert(s.Items),
                ForkItem f => f.Branches.Any(b => ContainsAlert(b.Items)),
                _ => false
            });

        // ---------- resolução (R2) ----------

        // Devolve os próximos passos-chave possíveis da sequência, dado o que vem depois dela (`cont`) e o que
        // vem depois do método em que ela está (`scopeCont`). Marca em cada decisão se ela entra.
        private IReadOnlyList<string> Eval(List<Item> items, IReadOnlyList<string> cont, IReadOnlyList<string> scopeCont)
        {
            var current = cont;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                switch (items[i])
                {
                    case KeyItem key:
                        current = [key.Node.Id];
                        break;
                    case ExitItem exit:
                        current = exit.Kind == ExitKind.Flow ? [EndToken] : scopeCont;
                        break;
                    case ScopeItem scope:
                        current = Eval(scope.Items, current, current);
                        break;
                    case ForkItem fork:
                        current = EvalFork(fork, current, scopeCont);
                        break;
                }
            }
            return current;
        }

        private IReadOnlyList<string> EvalFork(ForkItem fork, IReadOnlyList<string> cont, IReadOnlyList<string> scopeCont)
        {
            var sets = new List<IReadOnlyList<string>>();
            foreach (var branch in fork.Branches)
            {
                var set = Eval(branch.Items, cont, scopeCont);
                if (branch.Counts) sets.Add(Normalize(set));
            }
            if (fork.HasImplicit) sets.Add(Normalize(cont));

            if (fork.Candidate)
            {
                var different = sets.Count >= 2 && sets.Skip(1).Any(s => !s.SequenceEqual(sets[0], StringComparer.Ordinal));
                fork.IsKey = !fork.Forced && different;
            }

            return fork.IsKey
                ? [fork.Node.Id]
                : Normalize(sets.SelectMany(s => s));
        }

        private static IReadOnlyList<string> Normalize(IEnumerable<string> set) =>
            set.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

        // ---------- ligação ----------

        private sealed record Entry(string From, string? Label, bool Inferred);

        private sealed class StepDraft(FlowNode node, BusinessKind kind, string label, StepRef reference, bool inferred)
        {
            public FlowNode Node { get; } = node;
            public BusinessKind Kind { get; } = kind;
            public string Label { get; } = label;
            public StepRef Reference { get; } = reference;
            public bool Inferred { get; } = inferred;
            public List<string> Sources { get; } = [node.Id];
            public HashSet<string> SourceSet { get; } = new(StringComparer.Ordinal) { node.Id };
        }

        private sealed class EdgeDraft(string from, string to, string? label, bool inferred)
        {
            public string From { get; } = from;
            public string To { get; } = to;
            public string? Label { get; } = label;
            public bool Inferred { get; set; } = inferred;
        }

        private sealed class Linker(Deriver owner)
        {
            private readonly Dictionary<string, StepDraft> _steps = new(StringComparer.Ordinal);
            private readonly List<StepDraft> _order = [];
            private readonly List<EdgeDraft> _edges = [];
            private readonly Dictionary<(string, string, string?), EdgeDraft> _edgeIndex = [];
            private readonly Stack<List<Entry>> _exits = new();

            public int StepCount => _order.Count;

            public void Run(KeyItem start, List<Item> body)
            {
                _exits.Push([]);
                var frontier = Emit(start, []);
                var rest = Walk(body, frontier);
                var finals = Dedup(rest.Concat(_exits.Pop()));

                // R7: no máximo um Fim no fluxo, no nó de entrada (o fluxo termina ao sair do método de entrada).
                // Todo caminho que termina sem erro liga a ele. Sem nenhum passo além do início, não há o que fechar.
                if (finals.Count == 0 || _order.Count <= 1) return;
                var entryNode = owner._nodes[owner._graph.EntryNodeId];
                var end = new FlowNode(EndKeyPrefix + entryNode.Id, NodeKind.Entry, LocalizedText.Get(KeyEnd, owner._language),
                    Location: entryNode.Location, System: entryNode.System);
                Emit(new KeyItem(end, BusinessKind.End, end.Label, new StepRef(entryNode.Location, entryNode.System)), finals);
            }

            private List<Entry> Walk(List<Item> items, List<Entry> frontier)
            {
                foreach (var item in items)
                {
                    if (frontier.Count == 0) break;
                    switch (item)
                    {
                        case KeyItem key:
                            frontier = Emit(key, frontier);
                            break;
                        case VisitItem visit:
                            frontier = Touch(visit.Node, visit.Inferred, frontier);
                            break;
                        case ScopeItem scope:
                        {
                            _exits.Push([]);
                            var outs = Walk(scope.Items, frontier);
                            frontier = Dedup(outs.Concat(_exits.Pop()));
                            break;
                        }
                        case ExitItem exit:
                            if (exit.Kind == ExitKind.Method) _exits.Peek().AddRange(frontier);
                            frontier = [];
                            break;
                        case ForkItem fork:
                            frontier = WalkFork(fork, frontier);
                            break;
                    }
                }
                return frontier;
            }

            private List<Entry> WalkFork(ForkItem fork, List<Entry> frontier)
            {
                List<Entry> bases;
                if (fork.IsKey)
                {
                    var key = new KeyItem(fork.Node, BusinessKind.Decision, owner.DecisionLabel(fork.Node),
                        new StepRef(fork.Node.Location, fork.Node.System)) { Inferred = fork.Inferred };
                    bases = Emit(key, frontier);
                }
                else
                {
                    bases = fork.VisitSelf ? Touch(fork.Node, fork.Inferred, frontier) : frontier;
                }

                var outs = new List<Entry>();
                foreach (var branch in fork.Branches)
                {
                    var start = fork.IsKey
                        ? bases.Select(e => e with { Label = branch.Label }).ToList()
                        : bases.Select(e => IsCatch(branch.Label) ? e with { Label = e.Label ?? branch.Label } : e).ToList();
                    outs.AddRange(Walk(branch.Items, start));
                }

                if (fork.HasImplicit)
                    outs.AddRange(fork.IsKey ? bases.Select(e => e with { Label = fork.ImplicitLabel }) : bases);

                return Dedup(outs);
            }

            private List<Entry> Emit(KeyItem key, List<Entry> frontier)
            {
                if (!_steps.TryGetValue(key.Node.Id, out var step))
                {
                    step = new StepDraft(key.Node, key.Kind, key.Label, key.Reference, key.Inferred);
                    _steps[key.Node.Id] = step;
                    _order.Add(step);
                }

                foreach (var entry in frontier)
                    AddEdge(entry.From, step.Node.Id, entry.Label, entry.Inferred || key.Inferred);

                return [new Entry(step.Node.Id, null, false)];
            }

            // Nó que não vira passo: entra na lista de código agrupada no passo-chave anterior.
            private List<Entry> Touch(FlowNode node, bool inferred, List<Entry> frontier)
            {
                foreach (var from in frontier.Select(e => e.From).Distinct(StringComparer.Ordinal))
                    if (_steps.TryGetValue(from, out var step) && step.SourceSet.Add(node.Id))
                        step.Sources.Add(node.Id);

                return frontier.Select(e => e with { Inferred = e.Inferred || inferred }).ToList();
            }

            private void AddEdge(string from, string to, string? label, bool inferred)
            {
                if (string.Equals(from, to, StringComparison.Ordinal)) return;
                if (_edgeIndex.TryGetValue((from, to, label), out var existing))
                {
                    existing.Inferred |= inferred;
                    return;
                }
                var edge = new EdgeDraft(from, to, label, inferred);
                _edgeIndex[(from, to, label)] = edge;
                _edges.Add(edge);
            }

            private static List<Entry> Dedup(IEnumerable<Entry> entries) => entries.Distinct().ToList();

            private static string RealNodeId(string key) =>
                key.StartsWith(EndKeyPrefix, StringComparison.Ordinal) ? key[EndKeyPrefix.Length..] : key;

            public BusinessFlow ToFlow(int collapsed)
            {
                var steps = _order.Select(s => new BusinessStep(
                    owner.StepId(s.Node.Id), RealNodeId(s.Node.Id), s.Kind, s.Label,
                    LabelOrigin.Rule, LabelState.Proposed,
                    owner.RefOf(s.Reference), s.Kind == BusinessKind.End ? [RealNodeId(s.Node.Id)] : s.Sources, s.Inferred)).ToList();
                var edges = _edges.Select(e => new BusinessEdge(owner.StepId(e.From), owner.StepId(e.To), e.Label, e.Inferred)).ToList();
                return new BusinessFlow(owner._source.SurveyId, owner._graph.SchemaVersion, steps, edges, collapsed);
            }
        }

        // ---------- identificação e referência ----------

        public string StepId(string nodeId)
        {
            // O Fim leva "end|" na frente do nó de entrada: o Id não pode colidir com o do Início.
            var seed = nodeId.StartsWith(EndKeyPrefix, StringComparison.Ordinal)
                ? $"end|{nodeId[EndKeyPrefix.Length..]}"
                : nodeId;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_source.SurveyId:N}|{seed}"));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..HashLength];
        }

        public CodeRef? RefOf(StepRef reference)
        {
            if (reference.Location is null) return null;

            var repository = reference.System ?? _source.Repository;
            string? commit;
            string? branch;
            if (reference.System is null)
            {
                commit = _source.CommitHash;
                branch = _source.Branch;
            }
            else
            {
                var segment = _source.Segments.FirstOrDefault(s =>
                    string.Equals(s.Repository, reference.System, StringComparison.OrdinalIgnoreCase));
                commit = segment?.CommitHash;
                branch = segment?.Branch;
            }

            return new CodeRef(repository, reference.Location.File.Replace('\\', '/'), reference.Location.Line, commit, branch);
        }
    }
}
