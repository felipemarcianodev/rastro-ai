using System.Text;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;

namespace Rastro.Core.Architecture;

// Pessoa do C1 (contexto): o compilador não sabe quem usa o sistema, então quem diz é o usuário.
// FromAi: veio de uma sugestão da IA e ninguém editou depois (sai no C1 com a tag "Inferida").
public record C4Person(string Name, string? Description, bool? FromAi = null);

// Diagramas C4 do fluxo em Structurizr DSL (Fase 11). Sem IA: o mesmo grafo gera sempre o mesmo texto.
//   C1 contexto     o sistema, as pessoas informadas e os sistemas externos chamados pelo fluxo
//   C2 containers   os serviços do docker-compose (com o mapa de deploy) ou os projetos do fluxo,
//                   e o banco (EF Core, DbContext ou repositório)
//   C3 componentes  os tipos de cada projeto e quem chama quem
//   C4 código       visão dinâmica com a ordem das chamadas a partir da entrada
public static class C4WorkspaceBuilder
{
    public const int MaxDynamicSteps = 40;
    public const string InferredTag = "Inferida";

    private static readonly string[] FrameworkAssemblies = ["System", "Microsoft", "netstandard", "mscorlib"];

    private sealed record Component(string Key, string Id, string Name, string Project, string Description);

    public static string Build(PromptContext context, FlowGraph graph, IReadOnlyList<C4Person> people, DeploymentMap? deployment = null)
    {
        // Container de cada projeto: o serviço do compose que o publica ou, sem mapa, o próprio projeto.
        string Box(string project) => deployment?.ServiceOf(project)?.Name ?? project;

        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        // Os passos de outros sistemas (Fase 25) não são componentes do sistema do levantamento: cada sistema
        // vira um softwareSystem próprio, ligado por "chama por HTTP".
        var methods = graph.Nodes.Where(n => n.Kind is NodeKind.Entry or NodeKind.Method && n.Change is null && n.System is null).ToList();
        var httpOf = graph.Edges.Where(e => e.Http is not null).GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.First().Http!);
        var crossSystems = graph.Nodes.Where(n => n.System is not null && n.Change is null).Select(n => n.System!)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Componentes: tipos donos dos métodos, por projeto.
        var components = new Dictionary<string, Component>(StringComparer.Ordinal);
        var componentOf = new Dictionary<string, Component>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var project = method.Project ?? "Código";
            var type = TypeOf(method.Label);
            var key = $"{project}/{type}";
            if (!components.TryGetValue(key, out var component))
            {
                component = new Component(key, $"comp{components.Count + 1}", type, project, Role(type));
                components[key] = component;
            }
            componentOf[method.Id] = component;
        }

        var projects = components.Values.Select(c => Box(c.Project)).Distinct().ToList();
        var containerIds = projects.Select((p, i) => (p, Id: $"cont{i + 1}")).ToDictionary(x => x.p, x => x.Id);
        var entryProject = componentOf.TryGetValue(graph.EntryNodeId, out var entryComponent) ? Box(entryComponent.Project) : projects.FirstOrDefault();

        // Chamadas entre passos do fluxo, atravessando os nós de estrutura (if, laço, try...).
        var outgoing = graph.Edges.Where(e => e.Change != ChangeKind.Removed).ToLookup(e => e.From, e => e.To);
        var componentLinks = new List<(Component From, Component To, string Method)>();
        var databaseUsers = new List<Component>();
        var externalLinks = new List<(Component From, string System, string What)>();
        var crossLinks = new List<(Component From, string System, string What)>();
        var flowSteps = new List<(string From, string To, string What)>();

        foreach (var method in methods)
        {
            var from = componentOf[method.Id];
            foreach (var target in Reached(method.Id, outgoing, nodes))
            {
                var node = nodes[target];
                if (node.System is not null)
                {
                    var what = httpOf.TryGetValue(node.Id, out var http) ? $"{http.Method} {http.Route ?? "?"}" : MethodOf(node.Label);
                    crossLinks.Add((from, node.System, what));
                    flowSteps.Add((method.Id, "ext:" + node.System, what));
                }
                else if (componentOf.TryGetValue(node.Id, out var to))
                {
                    var what = MethodOf(node.Label);
                    if (to.Key != from.Key) componentLinks.Add((from, to, what));
                    flowSteps.Add((method.Id, node.Id, what));
                }
                else if (node.Kind == NodeKind.External && IsDatabase(node))
                {
                    databaseUsers.Add(from);
                    flowSteps.Add((method.Id, "db", "lê e grava"));
                }
                else if (node.Kind == NodeKind.External && ExternalSystem(node) is { } system)
                {
                    externalLinks.Add((from, system, MethodOf(node.Label)));
                    flowSteps.Add((method.Id, "ext:" + system, MethodOf(node.Label)));
                }
            }
        }
        databaseUsers.AddRange(components.Values.Where(c => IsStoreType(c.Name)));
        var usesDatabase = databaseUsers.Count > 0;
        var systems = externalLinks.Select(l => l.System).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var systemIds = systems.Select((s, i) => (s, Id: $"ext{i + 1}")).ToDictionary(x => x.s, x => x.Id);
        var crossIds = crossSystems.Select((s, i) => (s, Id: $"sis{i + 1}")).ToDictionary(x => x.s, x => x.Id);
        // Destino de uma chamada de fora do sistema do levantamento: sistema externo ou sistema do fluxo.
        var targetIds = systemIds.Concat(crossIds).ToDictionary(x => x.Key, x => x.Value);

        var text = new StringBuilder();
        var title = Quote($"{context.Repository}: {context.EntryPoint}");
        text.AppendLine($"workspace {title} {Quote($"Gerado pelo Rastro AI a partir do fluxo levantado pelo compilador (solution {context.SolutionPath}{(context.CommitHash is null ? "" : $", commit {context.CommitHash[..Math.Min(7, context.CommitHash.Length)]}")}).")} {{");
        text.AppendLine();
        text.AppendLine("    !identifiers hierarchical");
        text.AppendLine();
        text.AppendLine("    model {");

        var peopleIds = people.Select((p, i) => (Person: p, Id: $"pessoa{i + 1}")).ToList();
        foreach (var (person, id) in peopleIds)
            text.AppendLine(person.FromAi == true
                ? $"        {id} = person {Quote(person.Name)} {Quote(person.Description ?? "")} {Quote(InferredTag)}"
                : $"        {id} = person {Quote(person.Name)} {Quote(person.Description ?? "")}");

        text.AppendLine($"        sistema = softwareSystem {Quote(context.Repository)} {Quote($"Fluxo {context.EntryPoint}.")} {{");
        foreach (var project in projects)
        {
            var service = deployment?.Applications.FirstOrDefault(a => a.Name == project);
            var about = service is null ? "" : $"Serviço do docker-compose com {string.Join(", ", components.Values.Where(c => Box(c.Project) == project).Select(c => c.Project).Distinct().Order(StringComparer.Ordinal))}.";
            text.AppendLine($"            {containerIds[project]} = container {Quote(project)} {Quote(about)} \".NET\" {{");
            foreach (var component in components.Values.Where(c => Box(c.Project) == project))
                text.AppendLine($"                {component.Id} = component {Quote(component.Name)} {Quote(component.Description)} \"C#\"");
            text.AppendLine("            }");
        }
        if (usesDatabase)
            text.AppendLine($"            db = container {Quote(deployment?.DatabaseService ?? "Banco de dados")} \"Lido e gravado pelo fluxo.\" \"EF Core\" \"Database\"");
        text.AppendLine("        }");

        foreach (var system in systems)
            text.AppendLine($"        {systemIds[system]} = softwareSystem {Quote(system)} \"Sistema externo chamado pelo fluxo.\" \"Externo\"");
        foreach (var system in crossSystems)
        {
            text.AppendLine($"        {crossIds[system]} = softwareSystem {Quote(system)} {Quote("Sistema do fluxo, alcançado por chamada HTTP.")} {{");
            var crossProjects = graph.Nodes.Where(n => n.System == system && n.Kind is NodeKind.Entry or NodeKind.Method && n.Change is null)
                .Select(n => n.Project ?? "Código").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            foreach (var (project, i) in crossProjects.Select((p, i) => (p, i)))
                text.AppendLine($"            c{i + 1} = container {Quote(project)} \"\" \".NET\"");
            text.AppendLine("        }");
        }
        text.AppendLine();

        var entryContainer = entryProject is null ? null : $"sistema.{containerIds[entryProject]}";
        foreach (var (_, id) in peopleIds)
            text.AppendLine(entryContainer is null ? $"        {id} -> sistema \"Usa\"" : $"        {id} -> {entryContainer} \"Usa\"");

        string Ref(Component c) => $"sistema.{containerIds[Box(c.Project)]}.{c.Id}";
        foreach (var group in componentLinks.GroupBy(l => (l.From.Key, l.To.Key)))
        {
            var first = group.First();
            text.AppendLine($"        {Ref(first.From)} -> {Ref(first.To)} {Quote(Describe(group.Select(l => l.Method)))}");
        }
        foreach (var user in databaseUsers.DistinctBy(c => c.Key))
            text.AppendLine($"        {Ref(user)} -> sistema.db \"Lê e grava\"");
        foreach (var group in externalLinks.GroupBy(l => (l.From.Key, l.System)))
        {
            var first = group.First();
            text.AppendLine($"        {Ref(first.From)} -> {systemIds[first.System]} {Quote(Describe(group.Select(l => l.What)))}");
        }
        foreach (var group in crossLinks.GroupBy(l => (l.From.Key, l.System)))
        {
            var first = group.First();
            text.AppendLine($"        {Ref(first.From)} -> {crossIds[first.System]} {Quote($"chama por HTTP: {Describe(group.Select(l => l.What))}")} \"HTTP\"");
        }
        text.AppendLine("    }");
        text.AppendLine();

        text.AppendLine("    views {");
        text.AppendLine("        systemContext sistema \"C1-contexto\" \"C1: contexto do fluxo.\" {");
        text.AppendLine("            include *");
        text.AppendLine("            autolayout lr");
        text.AppendLine("        }");
        text.AppendLine("        container sistema \"C2-containers\" \"C2: projetos e banco usados pelo fluxo.\" {");
        text.AppendLine("            include *");
        text.AppendLine("            autolayout lr");
        text.AppendLine("        }");
        foreach (var project in projects)
        {
            text.AppendLine($"        component sistema.{containerIds[project]} {Quote($"C3-{containerIds[project]}")} {Quote($"C3: componentes de {project}.")} {{");
            text.AppendLine("            include *");
            text.AppendLine("            autolayout lr");
            text.AppendLine("        }");
        }

        foreach (var system in crossSystems)
        {
            text.AppendLine($"        container {crossIds[system]} {Quote($"C2-{crossIds[system]}")} {Quote($"C2: projetos de {system}.")} {{");
            text.AppendLine("            include *");
            text.AppendLine("            autolayout lr");
            text.AppendLine("        }");
        }

        if (entryProject is not null)
        {
            text.AppendLine($"        dynamic sistema.{containerIds[entryProject]} \"C4-fluxo\" \"C4: ordem das chamadas a partir de {Escape(context.EntryPoint)}.\" {{");
            var written = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var (fromId, toId, what) in OrderSteps(flowSteps, graph))
            {
                if (count >= MaxDynamicSteps) break;
                var from = componentOf[fromId];
                if (Box(from.Project) != entryProject) continue;
                string? target = toId switch
                {
                    "db" => "sistema.db",
                    _ when toId.StartsWith("ext:", StringComparison.Ordinal) => targetIds[toId[4..]],
                    _ when componentOf[toId] is var to && to.Key == from.Key => null,
                    _ when componentOf[toId] is var to && Box(to.Project) == entryProject => Ref(to),
                    _ => $"sistema.{containerIds[Box(componentOf[toId].Project)]}"
                };
                if (target is null || !written.Add($"{Ref(from)}>{target}")) continue;
                text.AppendLine($"            {Ref(from)} -> {target} {Quote(what)}");
                count++;
            }
            text.AppendLine("            autolayout lr");
            text.AppendLine("        }");
        }

        text.AppendLine();
        text.AppendLine("        styles {");
        text.AppendLine("            element \"Person\" {");
        text.AppendLine("                shape person");
        text.AppendLine("            }");
        text.AppendLine("            element \"Database\" {");
        text.AppendLine("                shape cylinder");
        text.AppendLine("            }");
        if (people.Any(p => p.FromAi == true))
        {
            text.AppendLine($"            element {Quote(InferredTag)} {{");
            text.AppendLine("                border dashed");
            text.AppendLine("            }");
        }
        text.AppendLine("            element \"Externo\" {");
        text.AppendLine("                background #8a8f98");
        text.AppendLine("                color #ffffff");
        text.AppendLine("            }");
        text.AppendLine("        }");
        text.AppendLine("    }");
        text.AppendLine("}");
        return text.ToString();
    }

    // Passos do fluxo na ordem em que a entrada chega a cada método de origem.
    private static IEnumerable<(string From, string To, string What)> OrderSteps(List<(string From, string To, string What)> steps, FlowGraph graph)
    {
        var order = DraftDiff.FlowOrder(graph);
        return steps.Select((s, i) => (Step: s, i)).OrderBy(x => order.GetValueOrDefault(x.Step.From, int.MaxValue)).ThenBy(x => x.i).Select(x => x.Step);
    }

    // Próximos métodos e chamadas externas alcançados a partir do método, atravessando estrutura.
    private static IEnumerable<string> Reached(string start, ILookup<string, string> outgoing, Dictionary<string, FlowNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var queue = new Queue<string>(outgoing[start]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !nodes.TryGetValue(id, out var node)) continue;
            if (node.Kind is NodeKind.Method or NodeKind.Entry or NodeKind.External)
            {
                yield return id;
                continue;
            }
            foreach (var next in outgoing[id]) queue.Enqueue(next);
        }
    }

    private static bool IsDatabase(FlowNode node) =>
        (node.Symbol ?? "").StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || (node.Project ?? "").StartsWith("Npgsql", StringComparison.Ordinal)
        || (node.Project ?? "").StartsWith("Dapper", StringComparison.Ordinal);

    private static bool IsStoreType(string type) =>
        type.EndsWith("DbContext", StringComparison.Ordinal) || type.Contains("Repositor", StringComparison.OrdinalIgnoreCase);

    // Sistema externo: chamada HTTP ou SDK de terceiro. Chamadas do próprio .NET ficam de fora.
    private static string? ExternalSystem(FlowNode node)
    {
        var symbol = node.Symbol ?? "";
        if (symbol.StartsWith("System.Net.Http.", StringComparison.Ordinal)) return "Serviço HTTP externo";
        var assembly = node.Project;
        if (string.IsNullOrEmpty(assembly) || FrameworkAssemblies.Any(f => assembly == f || assembly.StartsWith(f + ".", StringComparison.Ordinal))) return null;
        return assembly;
    }

    // "Tipo.Metodo" ou "Tipo.Metodo.Local" -> "Tipo".
    private static string TypeOf(string label) => label.Split('.')[0];

    private static string MethodOf(string label)
    {
        var dot = label.IndexOf('.');
        return dot < 0 ? label : label[(dot + 1)..];
    }

    private static string Role(string type) =>
        type.EndsWith("Controller", StringComparison.Ordinal) ? "Controller da API."
        : type.EndsWith("Job", StringComparison.Ordinal) ? "Job agendado."
        : type.EndsWith("Handler", StringComparison.Ordinal) || type.EndsWith("Consumer", StringComparison.Ordinal) ? "Tratador de mensagem."
        : IsStoreType(type) ? "Acesso a dados."
        : type.EndsWith("Service", StringComparison.Ordinal) || type.EndsWith("AppService", StringComparison.Ordinal) ? "Serviço."
        : "";

    private const int MaxMethodsInDescription = 3;

    private static string Describe(IEnumerable<string> methods)
    {
        var distinct = methods.Distinct(StringComparer.Ordinal).ToList();
        var shown = string.Join(", ", distinct.Take(MaxMethodsInDescription));
        return distinct.Count > MaxMethodsInDescription ? $"{shown} e mais {distinct.Count - MaxMethodsInDescription}" : shown;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "'").ReplaceLineEndings(" ");

    private static string Quote(string text) => $"\"{Escape(text)}\"";
}
