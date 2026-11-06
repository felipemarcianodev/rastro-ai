using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Um arquivo do repositório: caminho relativo (com "/") e o texto.
public record SchemaFile(string RelativePath, string Text);

// Os .cs do repositório lidos pelo Roslyn só pela árvore de sintaxe, sem compilar nem rodar nada do
// cliente (ADR-006). Só os arquivos com DbContext, configuração ou migration são lidos de saída; o
// arquivo de uma entidade é lido quando o modelo precisa da classe.
internal sealed class CodeIndex
{
    private static readonly string[] PrimaryMarkers =
        ["DbContext", "IEntityTypeConfiguration", "MigrationBuilder", "UseSnakeCaseNamingConvention", "UseSqlServer", "UseNpgsql"];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private readonly IReadOnlyList<SchemaFile> _files;
    private readonly Dictionary<string, SyntaxTree> _trees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ClassDeclarationSyntax Class, string File)?> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _enums = new(StringComparer.Ordinal);

    public CodeIndex(IReadOnlyList<SchemaFile> files)
    {
        _files = files;
        Primary = files.Where(f => PrimaryMarkers.Any(m => f.Text.Contains(m, StringComparison.Ordinal)))
            .Select(f => (Tree(f), f.RelativePath))
            .ToList();
    }

    public IReadOnlyList<(SyntaxTree Tree, string File)> Primary { get; }

    public SyntaxTree Tree(SchemaFile file)
    {
        if (!_trees.TryGetValue(file.RelativePath, out var tree))
        {
            tree = CSharpSyntaxTree.ParseText(file.Text, ParseOptions, path: file.RelativePath);
            _trees[file.RelativePath] = tree;
        }
        return tree;
    }

    // A primeira classe (ou record class) com esse nome, na ordem dos caminhos.
    public (ClassDeclarationSyntax Class, string File)? FindClass(string name)
    {
        if (_classes.TryGetValue(name, out var cached)) return cached;
        var pattern = new Regex(@"\b(class|record)\s+" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
        (ClassDeclarationSyntax, string)? found = null;
        foreach (var file in _files.Where(f => pattern.IsMatch(f.Text)))
        {
            var decl = Tree(file).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.ValueText == name);
            if (decl is not null) { found = (decl, file.RelativePath); break; }
        }
        _classes[name] = found;
        return found;
    }

    // O tipo de base do enum com esse nome ("int" quando não declarado), ou nulo se não é um enum do repositório.
    public string? EnumUnderlying(string name)
    {
        if (_enums.TryGetValue(name, out var cached)) return cached;
        var pattern = new Regex(@"\benum\s+" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
        string? found = null;
        foreach (var file in _files.Where(f => pattern.IsMatch(f.Text)))
        {
            var decl = Tree(file).GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().FirstOrDefault(e => e.Identifier.ValueText == name);
            if (decl is not null) { found = decl.BaseList?.Types.FirstOrDefault()?.Type.ToString() ?? "int"; break; }
        }
        _enums[name] = found;
        return found;
    }

    // Alguma chamada com esse nome nos arquivos principais (UseSqlServer, UseSnakeCaseNamingConvention).
    public bool Calls(string method) =>
        Primary.Any(p => p.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => CsSyntax.MethodName(i) == method));
}

// Leitura dos argumentos e das expressões que o modelo e as migrations usam, sem compilar.
internal static class CsSyntax
{
    public static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    public static SchemaSource Source(SchemaSourceKind kind, string file, SyntaxNode node) => new(kind, file, Line(node));

    public static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => null
    };

    public static TypeSyntax? GenericArgument(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name,
            SimpleNameSyntax s => s,
            _ => null
        };
        return name is GenericNameSyntax g && g.TypeArgumentList.Arguments.Count > 0 ? g.TypeArgumentList.Arguments[0] : null;
    }

    // Argumento pelo nome ("name: ...") ou, sem nome, pela posição.
    public static ExpressionSyntax? Arg(InvocationExpressionSyntax invocation, string name, int position = -1)
    {
        var args = invocation.ArgumentList.Arguments;
        var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name);
        if (named is not null) return named.Expression;
        if (position < 0 || position >= args.Count) return null;
        // Depois de um argumento com nome, a posição deixa de valer.
        for (var i = 0; i <= position; i++)
            if (args[i].NameColon is not null) return null;
        return args[position].Expression;
    }

    public static string? String(ExpressionSyntax? expression) => expression switch
    {
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => l.Token.ValueText,
        InvocationExpressionSyntax i when MethodName(i) == "nameof" && i.ArgumentList.Arguments.Count == 1 => LastName(i.ArgumentList.Arguments[0].Expression),
        InterpolatedStringExpressionSyntax s when s.Contents.All(c => c is InterpolatedStringTextSyntax) => string.Concat(s.Contents.Select(c => ((InterpolatedStringTextSyntax)c).TextToken.ValueText)),
        _ => null
    };

    public static int? Int(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax l && l.Token.Value is int value ? value : null;

    public static bool? Bool(ExpressionSyntax? expression) => expression switch
    {
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.TrueLiteralExpression) => true,
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.FalseLiteralExpression) => false,
        _ => null
    };

    // Strings de um "new[] { "a", "b" }" (ou de um argumento só).
    public static List<string> Strings(ExpressionSyntax? expression)
    {
        IEnumerable<ExpressionSyntax> items = expression switch
        {
            ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions,
            ArrayCreationExpressionSyntax a when a.Initializer is not null => a.Initializer.Expressions,
            CollectionExpressionSyntax c => c.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression),
            null => [],
            _ => [expression]
        };
        return items.Select(String).Where(s => s is not null).Select(s => s!).ToList();
    }

    // Membros de um lambda: "x => x.Id" dá [Id]; "x => new { x.A, x.B }" dá [A, B].
    public static List<string> LambdaMembers(ExpressionSyntax? expression)
    {
        var body = expression switch
        {
            SimpleLambdaExpressionSyntax s => s.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax p => p.ExpressionBody,
            _ => null
        };
        return body switch
        {
            MemberAccessExpressionSyntax m => [m.Name.Identifier.ValueText],
            AnonymousObjectCreationExpressionSyntax a => a.Initializers.Select(i => LastName(i.Expression)).Where(n => n is not null).Select(n => n!).ToList(),
            CastExpressionSyntax c when c.Expression is MemberAccessExpressionSyntax m => [m.Name.Identifier.ValueText],
            _ => []
        };
    }

    // Os membros de um lambda ou a lista de nomes em texto, o que o método receber.
    public static List<string> Members(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return [];
        if (args[0].Expression is LambdaExpressionSyntax) return LambdaMembers(args[0].Expression);
        return args.Select(a => String(a.Expression)).Where(s => s is not null).Select(s => s!).ToList();
    }

    public static string? LastName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        _ => null
    };

    // "DeleteBehavior.Cascade" e "ReferentialAction.SetNull".
    public static DeleteAction? Delete(ExpressionSyntax? expression) => (expression is null ? null : LastName(expression)) switch
    {
        "Cascade" or "ClientCascade" => DeleteAction.Cascade,
        "SetNull" => DeleteAction.SetNull,
        "SetDefault" => DeleteAction.SetDefault,
        "Restrict" => DeleteAction.Restrict,
        "NoAction" or "ClientNoAction" or "ClientSetNull" => DeleteAction.NoAction,
        _ => null
    };

    // As chamadas de uma cadeia, da primeira à última: "b.Property(x => x.A).HasMaxLength(20)" dá
    // [Property, HasMaxLength], com a raiz "b".
    public static (ExpressionSyntax Root, List<InvocationExpressionSyntax> Calls) Chain(ExpressionSyntax expression)
    {
        var calls = new List<InvocationExpressionSyntax>();
        var current = expression;
        while (current is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax member)
        {
            calls.Add(invocation);
            current = member.Expression;
        }
        calls.Reverse();
        return (current, calls);
    }

    // Os comandos de um lambda ou método: as expressões de cada linha, sem entrar em lambdas internos.
    public static IEnumerable<ExpressionSyntax> Statements(SyntaxNode? body) => body switch
    {
        BlockSyntax block => block.Statements.OfType<ExpressionStatementSyntax>().Select(s => s.Expression),
        ArrowExpressionClauseSyntax arrow => [arrow.Expression],
        ExpressionSyntax expression => [expression],
        _ => []
    };

    public static (string Parameter, SyntaxNode? Body)? Lambda(ExpressionSyntax? expression) => expression switch
    {
        SimpleLambdaExpressionSyntax s => (s.Parameter.Identifier.ValueText, (SyntaxNode?)s.Block ?? s.ExpressionBody),
        ParenthesizedLambdaExpressionSyntax p when p.ParameterList.Parameters.Count == 1 =>
            (p.ParameterList.Parameters[0].Identifier.ValueText, (SyntaxNode?)p.Block ?? p.ExpressionBody),
        _ => null
    };

    public static string BaseName(TypeSyntax type) => type switch
    {
        GenericNameSyntax g => g.Identifier.ValueText,
        QualifiedNameSyntax q => BaseName(q.Right),
        AliasQualifiedNameSyntax a => BaseName(a.Name),
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => type.ToString()
    };

    public static string? Attribute(MemberDeclarationSyntax member, string name, out AttributeSyntax? attribute)
    {
        attribute = member.AttributeLists.SelectMany(l => l.Attributes)
            .FirstOrDefault(a => BaseName(a.Name) is var n && (n == name || n == name + "Attribute"));
        return attribute?.Name.ToString();
    }

    public static ExpressionSyntax? AttributeArg(AttributeSyntax attribute, string? name, int position = -1)
    {
        var args = attribute.ArgumentList?.Arguments;
        if (args is null) return null;
        if (name is not null)
        {
            var named = args.Value.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name || a.NameColon?.Name.Identifier.ValueText == name);
            if (named is not null) return named.Expression;
        }
        var positional = args.Value.Where(a => a.NameEquals is null && a.NameColon is null).ToList();
        return position >= 0 && position < positional.Count ? positional[position].Expression : null;
    }
}
