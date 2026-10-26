using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rastro.Extractor;

// Prova de uma proposta: o método de destino e onde ele está declarado.
public record EdgeProof(string Target, string File, int Line);

public record ProofVerdict(bool Accepted, string Message, string? TargetDisplay = null)
{
    public static ProofVerdict Reject(string message) => new(false, message);
}

// O compilador confere a prova (ADR-001). Sem isso, nada que veio de IA entra no grafo.
// Aceita só quando o método existe na solution, tem corpo e o arquivo:linha cai dentro da
// declaração dele.
public static class ProofValidator
{
    public static async Task<ProofVerdict> ValidateAsync(Solution solution, string baseDirectory, EdgeProof proof, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(proof.Target) || string.IsNullOrWhiteSpace(proof.File) || proof.Line < 1)
            return ProofVerdict.Reject("Prova incompleta: informe o método de destino, o arquivo e a linha.");

        IMethodSymbol method;
        try
        {
            method = await EntryPointResolver.ResolveAsync(solution, proof.Target.Trim(), ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            return ProofVerdict.Reject($"O método de destino não confere: {ex.Message}");
        }

        var declaration = method.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .FirstOrDefault(s => s is BaseMethodDeclarationSyntax { Body: not null } or BaseMethodDeclarationSyntax { ExpressionBody: not null } or LocalFunctionStatementSyntax);
        if (declaration is null)
            return ProofVerdict.Reject($"{method.ToDisplayString()} não tem corpo no código (abstrato, externo ou gerado).");

        var span = declaration.GetLocation().GetLineSpan();
        var actualFile = Relative(baseDirectory, span.Path);
        var firstLine = span.StartLinePosition.Line + 1;
        var lastLine = span.EndLinePosition.Line + 1;

        var claimedFile = proof.File.Replace('\\', '/').TrimStart('/');
        if (!string.Equals(actualFile, claimedFile, StringComparison.OrdinalIgnoreCase))
            return ProofVerdict.Reject($"A prova não confere: {method.ToDisplayString()} está em {actualFile}:{firstLine}, não em {claimedFile}.");

        if (proof.Line < firstLine || proof.Line > lastLine)
            return ProofVerdict.Reject($"A prova não confere: {method.ToDisplayString()} vai da linha {firstLine} à {lastLine} de {actualFile}; a linha {proof.Line} está fora.");

        return new ProofVerdict(true, $"Prova conferida: {method.ToDisplayString()} em {actualFile}:{firstLine}.", method.ToDisplayString());
    }

    // Métodos com corpo cujo nome (ou Tipo.Metodo) contém o texto, para o agente montar a prova.
    public static async Task<IReadOnlyList<(string Name, string File, int Line)>> SearchMethodsAsync(
        Solution solution, string baseDirectory, string query, int limit, CancellationToken ct = default)
    {
        var results = new Dictionary<string, (string, string, int)>(StringComparer.Ordinal);
        var term = query.Trim();
        if (term.Length == 0) return [];

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(n => n.Contains(term.Split('.').Last(), StringComparison.OrdinalIgnoreCase), SymbolFilter.Member, ct))
            {
                if (symbol is not IMethodSymbol method || !EntryPointResolver.HasBody(method)) continue;
                var name = $"{method.ContainingType.ToDisplayString()}.{method.Name}";
                if (!name.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;

                var location = method.Locations.FirstOrDefault(l => l.IsInSource);
                if (location is null) continue;
                var span = location.GetLineSpan();
                results.TryAdd(name + "|" + span.Path, (name, Relative(baseDirectory, span.Path), span.StartLinePosition.Line + 1));
                if (results.Count >= limit) return [.. results.Values];
            }
        }

        return [.. results.Values.OrderBy(r => r.Item1, StringComparer.Ordinal)];
    }

    private static string Relative(string baseDirectory, string path) =>
        (string.IsNullOrEmpty(baseDirectory) ? path : Path.GetRelativePath(baseDirectory, path)).Replace('\\', '/');
}
