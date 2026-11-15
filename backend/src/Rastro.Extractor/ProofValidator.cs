using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Http;

namespace Rastro.Extractor;

// Prova de uma proposta: o método de destino e onde ele está declarado.
public record EdgeProof(string Target, string File, int Line);

// Refusal e RefusalArgs dizem o motivo da recusa em código (a tela traduz); Message é o texto em português.
public record ProofVerdict(bool Accepted, string Message, string? TargetDisplay = null,
    ProposalRefusal? Refusal = null, IReadOnlyList<string>? RefusalArgs = null)
{
    public static ProofVerdict Reject(ProposalRefusal refusal, params string[] args) =>
        new(false, FailureText.Of(refusal, args), null, refusal, args);
}

// O compilador confere a prova (ADR-001). Sem isso, nada que veio de IA entra no grafo.
// Aceita só quando o método existe na solution, tem corpo e o arquivo:linha cai dentro da
// declaração dele.
public static class ProofValidator
{
    public static async Task<ProofVerdict> ValidateAsync(Solution solution, string baseDirectory, EdgeProof proof, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(proof.Target) || string.IsNullOrWhiteSpace(proof.File) || proof.Line < 1)
            return ProofVerdict.Reject(ProposalRefusal.ProofIncomplete);

        IMethodSymbol method;
        try
        {
            method = await EntryPointResolver.ResolveAsync(solution, proof.Target.Trim(), ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            return ProofVerdict.Reject(ProposalRefusal.ProofTargetInvalid, [.. FailureText.Nest(ex.Failure, ex.Args)]);
        }

        var declaration = method.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .FirstOrDefault(s => s is BaseMethodDeclarationSyntax { Body: not null } or BaseMethodDeclarationSyntax { ExpressionBody: not null } or LocalFunctionStatementSyntax);
        if (declaration is null)
            return ProofVerdict.Reject(ProposalRefusal.ProofNoBody, method.ToDisplayString());

        var span = declaration.GetLocation().GetLineSpan();
        var actualFile = Relative(baseDirectory, span.Path);
        var firstLine = span.StartLinePosition.Line + 1;
        var lastLine = span.EndLinePosition.Line + 1;

        var claimedFile = proof.File.Replace('\\', '/').TrimStart('/');
        if (!string.Equals(actualFile, claimedFile, StringComparison.OrdinalIgnoreCase))
            return ProofVerdict.Reject(ProposalRefusal.ProofWrongFile, method.ToDisplayString(), actualFile, Number(firstLine), claimedFile);

        if (proof.Line < firstLine || proof.Line > lastLine)
            return ProofVerdict.Reject(ProposalRefusal.ProofLineOutside, method.ToDisplayString(), Number(firstLine), Number(lastLine), actualFile, Number(proof.Line));

        return new ProofVerdict(true, $"Prova conferida: {method.ToDisplayString()} em {actualFile}:{firstLine}.", method.ToDisplayString());
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Prova de uma ligação HTTP entre sistemas (Fase 25): além de o método existir no arquivo e na linha,
    // ele precisa ser um destino HTTP de verdade, ou seja, estar no índice de rotas do repositório com o
    // mesmo verbo da chamada e, quando a rota da chamada é conhecida, com um template que case.
    public static async Task<ProofVerdict> ValidateHttpAsync(
        Solution solution, string baseDirectory, EdgeProof proof, HttpCall call, IReadOnlyList<RouteEntry> index, CancellationToken ct = default)
    {
        var verdict = await ValidateAsync(solution, baseDirectory, proof, ct);
        if (!verdict.Accepted) return verdict;

        // "Ns.Tipo.Metodo(parâmetros)" -> "Ns.Tipo.Metodo", como o índice guarda.
        var display = verdict.TargetDisplay ?? string.Empty;
        var target = display.Contains('(') ? display[..display.IndexOf('(')] : display;

        var routes = index.Where(e => e.Target == target).ToList();
        if (routes.Count == 0)
            return ProofVerdict.Reject(ProposalRefusal.RouteNotIndexed, target);

        if (call.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
        {
            routes = routes.Where(e => string.Equals(e.Method, call.Method, StringComparison.OrdinalIgnoreCase)).ToList();
            if (routes.Count == 0)
                return ProofVerdict.Reject(ProposalRefusal.VerbMismatch, target, call.Method);
        }

        if (!string.IsNullOrWhiteSpace(call.Route)
            && !routes.Any(e => RouteTemplate.Matches(e.Template, call.Route) || RouteTemplate.MatchesSuffix(e.Template, call.Route)))
        {
            var served = string.Join(", ", routes.Select(e => $"{e.Method} {e.Template}"));
            return ProofVerdict.Reject(ProposalRefusal.RouteMismatch, target, served, call.Method, call.Route);
        }

        return verdict;
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
