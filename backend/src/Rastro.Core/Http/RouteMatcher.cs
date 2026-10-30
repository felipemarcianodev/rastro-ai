using Rastro.Core.Graph;

namespace Rastro.Core.Http;

// Descobre qual método atende uma chamada HTTP, olhando os índices de rotas dos repositórios.
public static class RouteMatcher
{
    // Mesmo verbo; primeiro o casamento exato em todos os índices, e só se nenhum casar, o por sufixo.
    // Chamada sem rota não casa com nada. Ordem determinística: repositório, template e alvo.
    public static IReadOnlyList<(string Repository, RouteEntry Entry)> Match(
        HttpCall call, IReadOnlyDictionary<string, IReadOnlyList<RouteEntry>> indexes)
    {
        if (string.IsNullOrWhiteSpace(call.Route)) return [];

        var candidates = indexes
            .SelectMany(i => i.Value.Select(e => (Repository: i.Key, Entry: e)))
            .Where(c => string.Equals(c.Entry.Method, call.Method, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var exact = Order(candidates.Where(c => RouteTemplate.Matches(c.Entry.Template, call.Route)));
        if (exact.Count > 0) return exact;

        return Order(candidates.Where(c => RouteTemplate.MatchesSuffix(c.Entry.Template, call.Route)));
    }

    private static List<(string Repository, RouteEntry Entry)> Order(IEnumerable<(string Repository, RouteEntry Entry)> items) =>
        items
            .OrderBy(c => c.Repository, StringComparer.Ordinal)
            .ThenBy(c => c.Entry.Template, StringComparer.Ordinal)
            .ThenBy(c => c.Entry.Target, StringComparer.Ordinal)
            .ToList();
}
