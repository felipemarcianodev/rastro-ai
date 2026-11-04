namespace Rastro.Core.Graph;

// Mapa do fluxo (Fase 30): o nível acima dos desenhos passo a passo, no jeito de um diagrama C2.
// Faixas por sistema, poucos blocos de leitura por faixa, ligações entre eles e os caminhos de
// ponta a ponta. Determinístico e sem IA (ADR-001): o mesmo grafo dá sempre o mesmo mapa.

public enum MapLaneKind
{
    // Um sistema do levantamento (o repositório do primeiro trecho ou um da costura).
    Code = 0,

    // O que está fora do código levantado: nós externos e chamadas HTTP sem destino resolvido.
    Outside = 1
}

public enum MapBlockKind
{
    // A entrada de um trecho: o ponto de entrada do levantamento ou o destino de uma chamada HTTP.
    Entry = 0,

    // Um método chamado pelo trecho, com o que ele chama no mesmo sistema recolhido dentro.
    Step = 1,

    // Uma bifurcação que muda o desfecho em pelo menos um ramo.
    Decision = 2,

    // Um ponto fora do código levantado.
    Outside = 3
}

public enum MapLinkKind
{
    // Um bloco segue o outro no mesmo sistema.
    Sequence = 0,

    // O ramo de uma decisão leva ao bloco.
    Outcome = 1,

    // Chamada para outro sistema ou para fora do código.
    Call = 2
}

// System é o repositório cadastrado; nulo no primeiro trecho e na faixa de fora do código.
public record MapLane(string Id, MapLaneKind Kind, string? System);

// Um ramo da decisão. To é o primeiro bloco do ramo (nulo quando o ramo não tem bloco próprio).
// Ends: o ramo encerra o fluxo (return ou throw). Fails: encerra com exceção.
public record MapOutcome(string Label, string? To, bool Ends, bool Fails);

// NodeId é o nó do grafo que dá nome ao bloco; NodeIds, todos os nós que o bloco recolhe
// (o painel do bloco e o recorte "ver só este trecho" usam esta lista).
public record MapBlock(
    string Id,
    MapBlockKind Kind,
    string Lane,
    int Order,
    string Title,
    string NodeId,
    IReadOnlyList<string> NodeIds,
    string? Symbol = null,
    SourceLocation? Location = null,
    string? Condition = null,
    IReadOnlyList<MapOutcome>? Outcomes = null);

// Label é o texto do ramo na ligação de desfecho. Http só existe na chamada entre sistemas.
public record MapLink(string From, string To, MapLinkKind Kind, string? Label = null, HttpCall? Http = null);

// Desfecho escolhido numa decisão do caminho: o bloco da decisão e o índice do desfecho.
public record MapChoice(string Decision, int Outcome);

// Um caminho de ponta a ponta: os blocos na ordem em que o fluxo passa por eles.
public record MapPath(string Id, IReadOnlyList<string> Blocks, IReadOnlyList<MapChoice> Choices, bool Fails);

public record FlowMap(
    IReadOnlyList<MapLane> Lanes,
    IReadOnlyList<MapBlock> Blocks,
    IReadOnlyList<MapLink> Links,
    IReadOnlyList<MapPath> Paths,
    bool PathsTruncated);
