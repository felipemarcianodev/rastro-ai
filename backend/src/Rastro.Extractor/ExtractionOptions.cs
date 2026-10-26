namespace Rastro.Extractor;

public record ExtractionOptions
{
    // Quantos níveis de chamada percorrer a partir da entrada. Além disso, o nó vira
    // não resolvido com o motivo DepthLimit.
    public int MaxDepth { get; init; } = 15;

    // Pasta base para os caminhos relativos do grafo. Nula: pasta da solution.
    public string? BaseDirectory { get; init; }
}
