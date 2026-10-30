using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Http;

namespace Rastro.Extractor;

// Índice de rotas de um repositório (Fase 25): qual método atende GET api/pedidos/{id}, POST api/estornos...
// Só sintaxe (nada de MSBuild nem compilação): ler centenas de .cs é rápido e não precisa de restore.
// Cobre controllers (atributos de rota) e Minimal API (MapGet e companhia com grupo de método).
// Handler em lambda fica fora nesta fase: não há método para ser o destino da aresta.
public static class RouteIndexBuilder
{
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".git", ".vs", "dist", "TestResults"];

    private static readonly Dictionary<string, string> AttributeVerbs = new(StringComparer.Ordinal)
    {
        ["HttpGet"] = "GET", ["HttpPost"] = "POST", ["HttpPut"] = "PUT", ["HttpPatch"] = "PATCH", ["HttpDelete"] = "DELETE"
    };

    private static readonly Dictionary<string, string> MapVerbs = new(StringComparer.Ordinal)
    {
        ["MapGet"] = "GET", ["MapPost"] = "POST", ["MapPut"] = "PUT", ["MapPatch"] = "PATCH", ["MapDelete"] = "DELETE"
    };

    private const int MaxGroupDepth = 8;

    private sealed record MethodInfo(string Target, string TypeName, string MethodName);

    public static IReadOnlyList<RouteEntry> Build(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var trees = new List<(string File, SyntaxTree Tree)>();
        foreach (var path in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            trees.Add((relative, CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)));
        }

        var methods = new List<MethodInfo>();
        foreach (var (_, tree) in trees)
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                methods.Add(new MethodInfo(TargetOf(method), TypeNameOf(method), method.Identifier.ValueText));

        var entries = new List<RouteEntry>();
        foreach (var (file, tree) in trees)
        {
            var rootNode = tree.GetRoot();
            foreach (var type in rootNode.DescendantNodes().OfType<TypeDeclarationSyntax>())
                AddControllerRoutes(type, file, entries);
            foreach (var invocation in rootNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                AddMinimalApiRoute(invocation, file, methods, entries);
        }

        return entries
            .OrderBy(e => e.File, StringComparer.Ordinal)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Method, StringComparer.Ordinal)
            .ThenBy(e => e.Template, StringComparer.Ordinal)
            .ToList();
    }

    // Arquivos .cs fora de build, dependências e testes. Percorre à mão para nem entrar nas pastas
    // ignoradas (um node_modules tem milhares de arquivos).
    private static IEnumerable<string> SourceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var found = new List<string>();
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(folder, "*.cs"));
            foreach (var child in Directory.EnumerateDirectories(folder))
                if (!IsSkipped(Path.GetFileName(child))) pending.Push(child);
        }
        return found.OrderBy(f => f, StringComparer.Ordinal);
    }

    private static bool IsSkipped(string folderName) =>
        SkippedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase)
        || folderName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);

    // ---- Controllers ----

    private static void AddControllerRoutes(TypeDeclarationSyntax type, string file, List<RouteEntry> entries)
    {
        var classRoute = AttributesNamed(type.AttributeLists, "Route").Select(FirstString).FirstOrDefault(r => r is not null);
        var controllerName = type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal)
            ? type.Identifier.ValueText[..^"Controller".Length]
            : type.Identifier.ValueText;

        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;

            var methodRoute = AttributesNamed(method.AttributeLists, "Route").Select(FirstString).FirstOrDefault(r => r is not null);
            foreach (var (verbAttribute, verb) in AttributeVerbs.SelectMany(v => AttributesNamed(method.AttributeLists, v.Key).Select(a => (a, v.Value))))
            {
                // [HttpPost("x")] manda; [HttpPost] com [Route("x")] no método usa o do Route.
                var template = FirstString(verbAttribute) ?? methodRoute;
                var full = Combine(classRoute, template, controllerName, method.Identifier.ValueText);
                entries.Add(new RouteEntry(verb, full, TargetOf(method), file, LineOf(method.Identifier)));
            }
        }
    }

    private static string Combine(string? classRoute, string? methodTemplate, string controller, string action)
    {
        string Fill(string text) => text.Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", action, StringComparison.OrdinalIgnoreCase);

        if (methodTemplate is not null && (methodTemplate.StartsWith('/') || methodTemplate.StartsWith("~/", StringComparison.Ordinal)))
            return RouteTemplate.Normalize(Fill(methodTemplate));

        return RouteTemplate.Normalize(Join(classRoute is null ? null : Fill(classRoute), methodTemplate is null ? null : Fill(methodTemplate)));
    }

    // Atributo pelo nome, com ou sem o sufixo "Attribute" (e com nome qualificado).
    private static IEnumerable<AttributeSyntax> AttributesNamed(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(l => l.Attributes).Where(a =>
        {
            var text = a.Name switch
            {
                QualifiedNameSyntax q => q.Right.Identifier.ValueText,
                SimpleNameSyntax s => s.Identifier.ValueText,
                _ => a.Name.ToString()
            };
            return text == name || text == name + "Attribute";
        });

    // Primeiro argumento posicional que é texto (Name = "..." e outros nomeados são ignorados).
    private static string? FirstString(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals is null && a.NameColon is null);
        return argument?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal
            ? literal.Token.ValueText
            : null;
    }

    // ---- Minimal API ----

    private static void AddMinimalApiRoute(InvocationExpressionSyntax invocation, string file, List<MethodInfo> methods, List<RouteEntry> entries)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access
            || !MapVerbs.TryGetValue(access.Name.Identifier.ValueText, out var verb)) return;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2 || arguments[0].Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } route)
            return;

        var target = HandlerTarget(arguments[1].Expression, invocation, methods);
        if (target is null) return; // lambda ou método que não está neste repositório

        var prefix = GroupPrefix(access.Expression, invocation, 0);
        var template = RouteTemplate.Normalize(Join(prefix, route.Token.ValueText));
        entries.Add(new RouteEntry(verb, template, target, file, LineOf(invocation.GetFirstToken())));
    }

    // Grupo de método (Handler ou Tipo.Handler) -> Namespace.Tipo.Metodo, quando a declaração é única.
    private static string? HandlerTarget(ExpressionSyntax handler, SyntaxNode call, List<MethodInfo> methods)
    {
        string? typeName = null;
        string name;
        switch (handler)
        {
            case IdentifierNameSyntax identifier:
                name = identifier.Identifier.ValueText;
                break;
            case MemberAccessExpressionSyntax { Expression: var owner } member:
                name = member.Name.Identifier.ValueText;
                typeName = owner switch
                {
                    IdentifierNameSyntax i => i.Identifier.ValueText,
                    MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                    _ => null
                };
                break;
            default:
                return null;
        }

        var candidates = methods.Where(m => m.MethodName == name).ToList();
        if (typeName is not null) candidates = candidates.Where(m => m.TypeName == typeName).ToList();
        if (candidates.Count > 1)
        {
            // Mesmo nome em vários tipos: vale o do tipo onde o Map foi escrito.
            var enclosing = call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            candidates = candidates.Where(m => m.TypeName == enclosing).ToList();
        }
        return candidates.Count == 1 ? candidates[0].Target : null;
    }

    // Prefixo de MapGroup("...") na cadeia da expressão ou numa variável local do mesmo método.
    private static string GroupPrefix(ExpressionSyntax receiver, SyntaxNode scopeFrom, int depth)
    {
        if (depth > MaxGroupDepth) return string.Empty;

        switch (receiver)
        {
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call:
            {
                var inner = GroupPrefix(access.Expression, scopeFrom, depth + 1);
                if (access.Name.Identifier.ValueText == "MapGroup"
                    && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
                    return Join(inner, literal.Token.ValueText);
                return inner; // WithTags, RequireAuthorization e afins não mudam a rota
            }

            case IdentifierNameSyntax variable:
            {
                var scope = scopeFrom.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or CompilationUnitSyntax);
                var declarator = scope?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(d => d.Identifier.ValueText == variable.Identifier.ValueText && d.Initializer is not null);
                return declarator is null ? string.Empty : GroupPrefix(declarator.Initializer!.Value, scopeFrom, depth + 1);
            }

            default:
                return string.Empty;
        }
    }

    // ---- Comum ----

    private static string Join(string? left, string? right) =>
        $"{left?.Trim('/')}/{right?.Trim('/')}".Trim('/');

    private static int LineOf(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string TargetOf(MethodDeclarationSyntax method) => $"{QualifiedTypeName(method)}.{method.Identifier.ValueText}";

    // Tipo mais interno do método (para casar com Tipo.Handler).
    private static string TypeNameOf(MethodDeclarationSyntax method) =>
        method.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? string.Empty;

    // Namespace (de bloco ou file-scoped) + tipos aninhados separados por ".".
    private static string QualifiedTypeName(SyntaxNode node)
    {
        var parts = new List<string>();
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case BaseTypeDeclarationSyntax type: parts.Add(type.Identifier.ValueText); break;
                case BaseNamespaceDeclarationSyntax ns: parts.Add(ns.Name.ToString()); break;
            }
        }
        parts.Reverse();
        return string.Join('.', parts);
    }
}
