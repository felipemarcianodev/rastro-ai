namespace Rastro.Extractor;

public record ExtractionOptions
{
    // Quantos níveis de chamada percorrer a partir da entrada. Além disso, o nó vira
    // não resolvido com o motivo DepthLimit.
    public int MaxDepth { get; init; } = 15;

    // Pasta base para os caminhos relativos do grafo. Nula: pasta da solution.
    public string? BaseDirectory { get; init; }

    // Fase 25: cada chamada do HttpClient vira um nó próprio (por local de chamada), com verbo e rota,
    // para a costura entre sistemas trocar por uma aresta até o método que atende. Falso: a chamada
    // continua sendo o nó externo de sempre e o grafo sai idêntico ao de antes.
    public bool HttpCalls { get; init; }
}
