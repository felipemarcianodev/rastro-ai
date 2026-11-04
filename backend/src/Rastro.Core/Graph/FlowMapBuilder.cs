namespace Rastro.Core.Graph;

// Monta o mapa do fluxo a partir do grafo (Fase 30). Regras:
// - faixa por sistema (FlowNode.System; nulo é o primeiro trecho), na ordem em que o fluxo entra
//   em cada um, e a faixa de fora do código por último;
// - bloco de entrada para o ponto de entrada e para cada destino de chamada HTTP resolvida. A chamada
//   repetida para a mesma entrada liga ao trecho já montado, sem abrir de novo (Fase 35);
// - bloco de passo para cada método chamado pelo trecho. Método grande (mais nós que o limite do
//   trecho) que só orquestra outros vira transparente: os passos dele sobem para o mapa. Assim o
//   controller que só repassa para um serviço não vira um bloco único;
// - decisão no mapa só quando muda o desfecho: um ramo retorna, lança exceção ou alcança outro
//   sistema. As outras ficam dentro do bloco;
// - chamada para fora do código (nó externo, HTTP sem destino) vira bloco na faixa de fora,
//   um por nó externo, com uma ligação de chamada para cada lugar que o chama;
// - caminhos: cada combinação de desfechos, da entrada até o fim, no máximo MaxPaths.
public static class FlowMapBuilder
{
    public const string MainLane = ":principal";
    public const string OutsideLane = ":fora";
    public const int MaxPaths = 8;

    // Trecho com poucos nós nunca tem método transparente: um bloco por método chamado.
    public const int MinExpandWeight = 12;

    public static FlowMap Build(FlowGraph graph) => new MapAssembler(graph).Build();

    private sealed class BlockDraft(string id, MapBlockKind kind, string lane, FlowNode node, string? segment)
    {
        public string Id { get; } = id;
        public MapBlockKind Kind { get; } = kind;
        public string Lane { get; } = lane;
        public FlowNode Node { get; } = node;

        // Bloco de entrada do trecho; a entrada aponta para si mesma e o bloco de fora fica sem trecho.
        public string? Segment { get; } = segment ?? (kind == MapBlockKind.Entry ? id : null);
        public List<string> NodeIds { get; } = [node.Id];
        public List<MapOutcome>? Outcomes { get; set; }

        public void Add(string nodeId)
        {
            if (!NodeIds.Contains(nodeId)) NodeIds.Add(nodeId);
        }
    }

    // Árvore de execução usada para montar os caminhos.
    private abstract record Item;
    private sealed record BlockItem(BlockDraft Block) : Item;
    private sealed record CallItem(List<Item> Remote) : Item;
    private sealed record DecisionItem(BlockDraft Block, List<Branch> Branches) : Item;
    private sealed record Branch(List<Item> Items, bool Ends, bool Fails);

    private sealed class Context(string lane, BlockDraft current, List<Item> items, int threshold)
    {
        public string Lane { get; } = lane;
        public BlockDraft Current { get; set; } = current;
        public List<Item> Items { get; } = items;
        public int Threshold { get; } = threshold;
        public string? PendingOutcome { get; set; }
        public bool Ends { get; set; }
        public bool Fails { get; set; }
    }

    private sealed class MapAssembler
    {
        private readonly FlowGraph _graph;
        private readonly Dictionary<string, FlowNode> _nodes;
        private readonly Dictionary<string, List<FlowEdge>> _out;
        private readonly List<BlockDraft> _blocks = [];
        private readonly List<MapLink> _links = [];
        private readonly List<string> _codeLanes = [];
        private readonly Dictionary<string, string?> _laneSystem = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BlockDraft> _outside = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BlockDraft> _segments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _weight = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _reachesOutside = new(StringComparer.Ordinal);
        private readonly HashSet<string> _active = new(StringComparer.Ordinal);

        public MapAssembler(FlowGraph graph)
        {
            _graph = graph;
            _nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            _out = graph.Edges
                .Where(e => e.Change != ChangeKind.Removed && _nodes.ContainsKey(e.From) && _nodes.ContainsKey(e.To))
                .GroupBy(e => e.From, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.Order).ThenBy(e => e.To, StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal);
        }

        public FlowMap Build()
        {
            if (!_nodes.ContainsKey(_graph.EntryNodeId))
                return new FlowMap([], [], [], [], false);

            var root = Segment(_graph.EntryNodeId);
            var (paths, truncated) = Paths(root);

            var lanes = _codeLanes.Select(id => new MapLane(id, MapLaneKind.Code, _laneSystem[id])).ToList();
            if (_outside.Count > 0) lanes.Add(new MapLane(OutsideLane, MapLaneKind.Outside, null));

            var depths = Depths(((BlockItem)root[0]).Block.Id);
            var blocks = _blocks.Select((b, i) => new MapBlock(
                b.Id, b.Kind, b.Lane, i + 1, Title(b), b.Node.Id, b.NodeIds,
                b.Node.Symbol, b.Node.Location,
                b.Kind == MapBlockKind.Decision ? b.Node.Condition : null,
                b.Outcomes, b.Segment, depths.GetValueOrDefault(b.Segment ?? b.Id))).ToList();

            return new FlowMap(lanes, blocks, _links, paths, truncated);
        }

        // Saltos entre sistemas até cada trecho, pelo caminho mais curto: um trecho chamado de dois
        // lugares fica com a menor profundidade. O bloco de fora conta um salto depois de quem o chama.
        private Dictionary<string, int> Depths(string rootSegment)
        {
            var byId = _blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
            var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [rootSegment] = 0 };
            var queue = new Queue<string>([rootSegment]);
            while (queue.Count > 0)
            {
                var segment = queue.Dequeue();
                foreach (var link in _links.Where(l => l.Kind == MapLinkKind.Call && byId[l.From].Segment == segment))
                {
                    var target = byId[link.To].Segment ?? link.To;
                    if (depths.ContainsKey(target)) continue;
                    depths[target] = depths[segment] + 1;
                    if (byId[link.To].Segment is not null) queue.Enqueue(target);
                }
            }
            return depths;
        }

        private static string Title(BlockDraft block) =>
            block.Kind == MapBlockKind.Decision && !string.IsNullOrWhiteSpace(block.Node.Condition)
                ? block.Node.Condition!
                : block.Node.Label;

        // ---------- montagem ----------

        private List<Item> Segment(string entryId)
        {
            var entryNode = _nodes[entryId];
            var lane = LaneOf(entryNode);
            var entry = NewBlock(MapBlockKind.Entry, lane, entryNode, null);
            _segments[entryId] = entry;
            var items = new List<Item> { new BlockItem(entry) };
            var context = new Context(lane, entry, items, Math.Max(MinExpandWeight, Weight(entryId) / 3));

            _active.Add(entryId);
            Walk(entryId, context);
            _active.Remove(entryId);
            return items;
        }

        private void Walk(string parentId, Context context)
        {
            foreach (var edge in Out(parentId))
                Handle(edge, context);
        }

        private void Handle(FlowEdge edge, Context context)
        {
            var child = _nodes[edge.To];

            if (edge.Http is not null || child.Kind == NodeKind.External)
            {
                Call(edge, child, context);
                return;
            }

            switch (child.Kind)
            {
                case NodeKind.Entry:
                case NodeKind.Method:
                    if (_active.Contains(child.Id))
                    {
                        context.Current.Add(child.Id);
                        return;
                    }
                    _active.Add(child.Id);
                    if (ShouldExpand(edge.From, child.Id, context))
                    {
                        context.Current.Add(child.Id);
                        Walk(child.Id, context);
                    }
                    else
                    {
                        var step = NewBlock(MapBlockKind.Step, context.Lane, child, context.Current.Segment);
                        Link(context, step.Id);
                        context.Items.Add(new BlockItem(step));
                        context.Current = step;
                        Collect(child.Id, step, context);
                    }
                    _active.Remove(child.Id);
                    return;

                case NodeKind.Decision when Promoted(child.Id):
                    Decision(child, context);
                    return;

                case NodeKind.Decision:
                case NodeKind.Loop:
                case NodeKind.Try:
                    context.Current.Add(child.Id);
                    Walk(child.Id, context);
                    return;

                case NodeKind.Return:
                case NodeKind.Throw:
                    context.Current.Add(child.Id);
                    context.Ends = true;
                    if (child.Kind == NodeKind.Throw) context.Fails = true;
                    return;

                default:
                    context.Current.Add(child.Id);
                    return;
            }
        }

        // Tudo o que o passo alcança no mesmo sistema entra no bloco; as chamadas para outro
        // sistema ou para fora saem do bloco como ligações de chamada.
        private void Collect(string parentId, BlockDraft step, Context context)
        {
            foreach (var edge in Out(parentId))
            {
                var child = _nodes[edge.To];
                if (edge.Http is not null || child.Kind == NodeKind.External)
                {
                    Call(edge, child, context);
                    continue;
                }

                step.Add(child.Id);
                if (child.Kind is NodeKind.Method or NodeKind.Entry)
                {
                    if (!_active.Add(child.Id)) continue;
                    Collect(child.Id, step, context);
                    _active.Remove(child.Id);
                }
                else
                {
                    Collect(child.Id, step, context);
                }
            }
        }

        private void Call(FlowEdge edge, FlowNode child, Context context)
        {
            var remoteEntry = edge.Http is not null && child.Kind is NodeKind.Entry or NodeKind.Method;
            if (remoteEntry && _segments.TryGetValue(child.Id, out var built) && !_active.Contains(child.Id))
            {
                // O caminho passa só pela entrada: abrir o trecho de novo multiplicaria as combinações.
                Link(context, built.Id, MapLinkKind.Call, edge.Http);
                context.Items.Add(new CallItem([new BlockItem(built)]));
                return;
            }

            if (remoteEntry && !_active.Contains(child.Id))
            {
                var remote = Segment(child.Id);
                var target = ((BlockItem)remote[0]).Block;
                Link(context, target.Id, MapLinkKind.Call, edge.Http);
                context.Items.Add(new CallItem(remote));
                return;
            }

            if (remoteEntry)
            {
                context.Current.Add(child.Id);
                return;
            }

            if (!_outside.TryGetValue(child.Id, out var outside))
            {
                outside = NewBlock(MapBlockKind.Outside, OutsideLane, child, null);
                _outside[child.Id] = outside;
            }
            Link(context, outside.Id, MapLinkKind.Call, edge.Http);
            context.Items.Add(new BlockItem(outside));
        }

        private void Decision(FlowNode node, Context context)
        {
            var decision = NewBlock(MapBlockKind.Decision, context.Lane, node, context.Current.Segment);
            decision.Outcomes = [];
            Link(context, decision.Id);
            var item = new DecisionItem(decision, []);
            context.Items.Add(item);

            var groups = Out(node.Id)
                .GroupBy(e => e.Label ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            foreach (var group in groups)
            {
                var branchItems = new List<Item>();
                var branch = new Context(context.Lane, decision, branchItems, context.Threshold) { PendingOutcome = Text(group.Key) };
                foreach (var edge in group)
                    Handle(edge, branch);

                decision.Outcomes.Add(new MapOutcome(Text(group.Key), FirstBlock(branchItems), branch.Ends, branch.Fails));
                item.Branches.Add(new Branch(branchItems, branch.Ends, branch.Fails));
            }

            // if sem else: o fluxo segue quando a condição é falsa.
            if (node.Label.StartsWith("if", StringComparison.Ordinal) && groups.All(g => g.Key != "else"))
            {
                decision.Outcomes.Add(new MapOutcome("else", null, false, false));
                item.Branches.Add(new Branch([], false, false));
            }

            context.Current = decision;
            context.PendingOutcome = null;
        }

        private static string Text(string label) => label.Length == 0 ? "then" : label;

        private static string? FirstBlock(List<Item> items) => items.Select(i => i switch
        {
            BlockItem b => b.Block.Id,
            DecisionItem d => d.Block.Id,
            CallItem c => ((BlockItem)c.Remote[0]).Block.Id,
            _ => null
        }).FirstOrDefault(id => id is not null);

        private void Link(Context context, string to, MapLinkKind kind = MapLinkKind.Sequence, HttpCall? http = null)
        {
            var label = context.PendingOutcome;
            if (label is not null)
            {
                if (kind == MapLinkKind.Sequence) kind = MapLinkKind.Outcome;
                context.PendingOutcome = null;
            }
            var link = new MapLink(context.Current.Id, to, kind, label, http);
            // O mesmo bloco chamando o mesmo destino pela mesma rota é uma ligação só no desenho.
            if (kind != MapLinkKind.Call || !_links.Contains(link)) _links.Add(link);
        }

        private BlockDraft NewBlock(MapBlockKind kind, string lane, FlowNode node, string? segment)
        {
            var block = new BlockDraft($"b{_blocks.Count + 1}", kind, lane, node, segment);
            _blocks.Add(block);
            return block;
        }

        private string LaneOf(FlowNode node)
        {
            var lane = node.System ?? MainLane;
            if (!_laneSystem.ContainsKey(lane))
            {
                _laneSystem[lane] = node.System;
                _codeLanes.Add(lane);
            }
            return lane;
        }

        private IReadOnlyList<FlowEdge> Out(string id) =>
            _out.TryGetValue(id, out var edges) ? edges : [];

        // ---------- critérios ----------

        // Transparente: o método orquestra outros passos (chama método ou decide) e é grande demais
        // para um bloco só, ou é o único método que o pai chama (passagem, como o controller que só
        // repassa para o serviço).
        private bool ShouldExpand(string parentId, string methodId, Context context)
        {
            var orchestrates = Out(methodId).Any(e => e.Http is null && _nodes[e.To].Kind is NodeKind.Method or NodeKind.Entry or NodeKind.Decision);
            if (!orchestrates) return false;
            if (Weight(methodId) > context.Threshold) return true;
            if (_nodes[parentId].Kind is not (NodeKind.Method or NodeKind.Entry)) return false;

            var methodChildren = Out(parentId)
                .Where(e => e.Http is null && _nodes[e.To].Kind is NodeKind.Method or NodeKind.Entry)
                .Select(e => e.To)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return methodChildren.Count == 1 && methodChildren[0] == methodId;
        }

        // Nós alcançados no mesmo sistema, sem atravessar chamada HTTP.
        private int Weight(string id)
        {
            if (_weight.TryGetValue(id, out var cached)) return cached;
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var queue = new Queue<string>([id]);
            while (queue.Count > 0)
                foreach (var edge in Out(queue.Dequeue()))
                    if (edge.Http is null && seen.Add(edge.To))
                        queue.Enqueue(edge.To);
            return _weight[id] = seen.Count;
        }

        // A decisão muda o desfecho: um ramo tem return ou throw no próprio nível (sem entrar em
        // método chamado) ou alcança outro sistema ou fora do código.
        private bool Promoted(string decisionId) =>
            EndsInside(decisionId, new HashSet<string>(StringComparer.Ordinal)) || ReachesOutside(decisionId);

        private bool EndsInside(string id, HashSet<string> seen)
        {
            foreach (var edge in Out(id))
            {
                var child = _nodes[edge.To];
                if (child.Kind is NodeKind.Return or NodeKind.Throw) return true;
                if (child.Kind is NodeKind.Decision or NodeKind.Loop or NodeKind.Try
                    && seen.Add(child.Id) && EndsInside(child.Id, seen))
                    return true;
            }
            return false;
        }

        private bool ReachesOutside(string id)
        {
            if (_reachesOutside.TryGetValue(id, out var cached)) return cached;
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var queue = new Queue<string>([id]);
            var found = false;
            while (queue.Count > 0 && !found)
                foreach (var edge in Out(queue.Dequeue()))
                {
                    if (edge.Http is not null || _nodes[edge.To].Kind == NodeKind.External)
                    {
                        found = true;
                        break;
                    }
                    if (seen.Add(edge.To)) queue.Enqueue(edge.To);
                }
            return _reachesOutside[id] = found;
        }

        // ---------- caminhos ----------

        // Enumera as combinações de desfechos como um odômetro: roda o fluxo com as escolhas feitas
        // até aqui (0 nas decisões novas) e depois avança a última decisão que ainda tem desfecho.
        private (List<MapPath> Paths, bool Truncated) Paths(List<Item> root)
        {
            var found = new List<(List<string> Blocks, List<MapChoice> Choices, bool Fails)>();
            var choices = new List<int>();
            var counts = new List<int>();
            var truncated = false;

            while (true)
            {
                var blocks = new List<string>();
                var taken = new List<MapChoice>();
                var fails = false;
                var position = 0;
                Run(root, blocks, taken, ref position, ref fails, choices, counts);
                found.Add((blocks, taken, fails));

                while (choices.Count > 0 && choices[^1] + 1 >= counts[^1])
                {
                    choices.RemoveAt(choices.Count - 1);
                    counts.RemoveAt(counts.Count - 1);
                }
                if (choices.Count == 0) break;
                if (found.Count >= MaxPaths)
                {
                    truncated = true;
                    break;
                }
                choices[^1]++;
            }

            var ordered = found
                .Select((p, i) => (p, i))
                .OrderBy(x => x.p.Fails)
                .ThenBy(x => x.i)
                .Select((x, i) => new MapPath($"p{i + 1}", x.p.Blocks, x.p.Choices, x.p.Fails))
                .ToList();
            return (ordered, truncated);
        }

        // Devolve verdadeiro quando o fluxo terminou (um ramo encerrou com return ou throw).
        private static bool Run(List<Item> items, List<string> blocks, List<MapChoice> taken, ref int position, ref bool fails,
            List<int> choices, List<int> counts)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case BlockItem b:
                        blocks.Add(b.Block.Id);
                        break;

                    case CallItem c:
                        // O fim do outro sistema volta para quem chamou; não encerra este fluxo.
                        var remoteFails = false;
                        Run(c.Remote, blocks, taken, ref position, ref remoteFails, choices, counts);
                        break;

                    case DecisionItem d when d.Branches.Count > 0:
                        blocks.Add(d.Block.Id);
                        if (position == choices.Count)
                        {
                            choices.Add(0);
                            counts.Add(d.Branches.Count);
                        }
                        var chosen = choices[position++];
                        taken.Add(new MapChoice(d.Block.Id, chosen));
                        var branch = d.Branches[chosen];
                        if (Run(branch.Items, blocks, taken, ref position, ref fails, choices, counts)) return true;
                        if (branch.Ends)
                        {
                            fails |= branch.Fails;
                            return true;
                        }
                        break;

                    case DecisionItem d:
                        blocks.Add(d.Block.Id);
                        break;
                }
            }
            return false;
        }
    }
}
