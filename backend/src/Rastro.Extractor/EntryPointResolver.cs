using Microsoft.CodeAnalysis;

namespace Rastro.Extractor;

public class FlowEntryNotFoundException(string message) : Exception(message);

// Acha o método de entrada a partir de "Tipo.Metodo" ou "Namespace.Tipo.Metodo".
internal static class EntryPointResolver
{
    // Quantos métodos de mesmo nome o erro sugere quando o tipo não bate.
    internal const int MaxSuggestions = 5;

    public static async Task<IMethodSymbol> ResolveAsync(Solution solution, string entryPoint, CancellationToken ct)
    {
        var lastDot = entryPoint.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == entryPoint.Length - 1)
            throw new FlowEntryNotFoundException($"Ponto de entrada inválido: \"{entryPoint}\". Use Tipo.Metodo ou Namespace.Tipo.Metodo.");

        var typeName = entryPoint[..lastDot];
        var methodName = entryPoint[(lastDot + 1)..];

        var found = new Dictionary<string, IMethodSymbol>();
        var sameName = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(methodName, SymbolFilter.Member, ct))
            {
                if (symbol is not IMethodSymbol method || !HasBody(method)) continue;

                var containing = method.ContainingType.ToDisplayString();
                if (containing != typeName && !containing.EndsWith("." + typeName, StringComparison.Ordinal))
                {
                    sameName.Add($"{containing}.{methodName}");
                    continue;
                }

                found.TryAdd(method.GetDocumentationCommentId() ?? method.ToDisplayString(), method);
            }
        }

        if (found.Count == 0)
            throw new FlowEntryNotFoundException(NotFoundMessage(entryPoint, methodName, sameName));

        var types = found.Values.Select(m => m.ContainingType.ToDisplayString()).Distinct().ToList();
        if (types.Count > 1)
            throw new FlowEntryNotFoundException(
                $"\"{entryPoint}\" é ambíguo. Use o nome completo: {string.Join(", ", types.Select(t => $"{t}.{methodName}"))}.");

        // Sobrecargas no mesmo tipo: a primeira declarada.
        return found.Values
            .OrderBy(m => m.Locations[0].SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(m => m.Locations[0].SourceSpan.Start)
            .First();
    }

    // Ex.: o usuário esqueceu a classe ("Namespace.Metodo") e o método existe em "Namespace.Classe.Metodo".
    private static string NotFoundMessage(string entryPoint, string methodName, SortedSet<string> sameName)
    {
        if (sameName.Count == 0)
            return $"Método \"{entryPoint}\" não encontrado na solution: nenhum método chamado {methodName}, com corpo, existe nela.";

        var shown = string.Join(", ", sameName.Take(MaxSuggestions));
        var more = sameName.Count > MaxSuggestions ? $" e mais {sameName.Count - MaxSuggestions}" : "";
        return $"Método \"{entryPoint}\" não encontrado na solution. Com o nome {methodName} existe: {shown}{more}. Use Tipo.Metodo ou Namespace.Tipo.Metodo.";
    }

    internal static bool HasBody(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length > 0 && !method.IsAbstract && !method.IsExtern;
}
