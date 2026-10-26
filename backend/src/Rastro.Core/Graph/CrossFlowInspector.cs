using System.Text.RegularExpressions;

namespace Rastro.Core.Graph;

// Fluxo de um levantamento, para comparar com os outros do mesmo repositório.
public record FlowSample(Guid SurveyId, string EntryPoint, FlowGraph Graph);

// "Este fluxo grava Pedido sem passar por PedidoValidator.Validar, que outros fluxos que gravam
// Pedido usam antes." NodeId é o passo que grava; Guard é a checagem que falta; Flows, os
// pontos de entrada que passam por ela.
public record CrossFlowFinding(string NodeId, string Entity, string Guard, IReadOnlyList<string> Flows);

// Regra cruzada entre fluxos (Fase 13). Heurística conservadora, sem IA:
// - passo que grava: método de tipo *Repository, *Store, *Dao ou *Repositorio cujo nome é de
//   escrita (Add, Update, Save, Insert, Remove, Delete, Gravar, Salvar, Atualizar, Inserir, Excluir);
// - entidade: o tipo do 1º parâmetro que não é primitivo nem CancellationToken;
// - guarda: método antes da gravação (no caminho da entrada até ela) com nome de checagem;
// - achado só quando ao menos MinOtherFlows outros fluxos que gravam a mesma entidade usam a
//   mesma guarda e este não: um fluxo isolado não vira regra.
public static partial class CrossFlowInspector
{
    public const int MinOtherFlows = 2;

    [GeneratedRegex(@"(Repository|Store|Dao|Repositorio)$")]
    private static partial Regex StoreType();

    [GeneratedRegex(@"^(Add|Update|Save|Insert|Remove|Delete|Upsert|Gravar|Salvar|Atualizar|Inserir|Excluir|Remover|Adicionar)")]
    private static partial Regex WriteMethod();

    [GeneratedRegex(@"^(Valid|Ensure|Check|Assert|Verify|Guard|Validar|Verificar|Garantir|Checar|Conferir|Pode|Deve)")]
    private static partial Regex GuardMethod();

    private static readonly HashSet<string> NotEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "long", "short", "bool", "decimal", "double", "float", "object", "byte", "char",
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeSpan", "CancellationToken",
        "String", "Int32", "Int64", "Boolean", "Decimal", "Double", "Object",
    };

    public static IReadOnlyList<CrossFlowFinding> Inspect(FlowSample target, IReadOnlyList<FlowSample> others)
    {
        var writes = Writes(target.Graph);
        if (writes.Count == 0) return [];

        // Guardas usadas por cada outro fluxo antes de gravar cada entidade.
        var guardsByEntity = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
        foreach (var other in others.Where(o => o.SurveyId != target.SurveyId && o.EntryPoint != target.EntryPoint))
        {
            foreach (var (node, entity) in Writes(other.Graph))
            {
                var perGuard = guardsByEntity.TryGetValue(entity, out var found) ? found : guardsByEntity[entity] = new(StringComparer.Ordinal);
                foreach (var guard in GuardsBefore(other.Graph, node.Id))
                {
                    if (!perGuard.TryGetValue(guard, out var flows)) perGuard[guard] = flows = new HashSet<string>(StringComparer.Ordinal);
                    flows.Add(other.EntryPoint);
                }
            }
        }

        var findings = new List<CrossFlowFinding>();
        foreach (var (node, entity) in writes)
        {
            if (!guardsByEntity.TryGetValue(entity, out var perGuard)) continue;
            var mine = GuardsBefore(target.Graph, node.Id);
            foreach (var (guard, flows) in perGuard.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (flows.Count >= MinOtherFlows && !mine.Contains(guard))
                    findings.Add(new CrossFlowFinding(node.Id, entity, guard, flows.OrderBy(f => f, StringComparer.Ordinal).ToList()));
            }
        }
        return findings;
    }

    private static List<(FlowNode Node, string Entity)> Writes(FlowGraph graph)
    {
        var result = new List<(FlowNode, string)>();
        foreach (var node in graph.Nodes.Where(n => n.Kind is NodeKind.Method or NodeKind.External && n.Symbol is not null))
        {
            var (type, method) = TypeAndMethod(node.Label);
            if (type is null || !StoreType().IsMatch(type) || !WriteMethod().IsMatch(method)) continue;
            if (EntityOf(node.Symbol!) is { } entity) result.Add((node, entity));
        }
        return result;
    }

    // Guardas no caminho da entrada até o passo (os que chegam a ele).
    private static HashSet<string> GuardsBefore(FlowGraph graph, string nodeId)
    {
        var incoming = graph.Edges.Where(e => e.From != e.To).ToLookup(e => e.To, e => e.From);
        var seen = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<string>([nodeId]);
        while (queue.Count > 0)
            foreach (var from in incoming[queue.Dequeue()])
                if (seen.Add(from)) queue.Enqueue(from);

        // Guardas são os passos do caminho com nome de checagem, mais os chamados pelos mesmos
        // passos do caminho: Validar() e Gravar() costumam ser irmãos, chamados pelo mesmo método.
        var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label, StringComparer.Ordinal);
        var candidates = new HashSet<string>(seen, StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(e => seen.Contains(e.From)))
            candidates.Add(edge.To);
        return candidates
            .Where(id => id != nodeId && labels.TryGetValue(id, out var label) && GuardMethod().IsMatch(TypeAndMethod(label).Method))
            .Select(id => labels[id])
            .ToHashSet(StringComparer.Ordinal);
    }

    private static (string? Type, string Method) TypeAndMethod(string label)
    {
        var dot = label.LastIndexOf('.');
        return dot <= 0 ? (null, label) : (label[..dot].Split('.').Last(), label[(dot + 1)..]);
    }

    // "Ns.IPedidoRepository.Update(Ns.Pedido, System.Threading.CancellationToken)" -> "Pedido".
    private static string? EntityOf(string symbol)
    {
        var open = symbol.IndexOf('(');
        if (open < 0 || !symbol.EndsWith(')')) return null;
        var parameters = symbol[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var parameter in parameters)
        {
            var type = parameter.Split('<')[0].Split('.').Last().TrimEnd('?', '[', ']');
            if (type.Length > 0 && !NotEntities.Contains(type)) return type;
        }
        return null;
    }
}
