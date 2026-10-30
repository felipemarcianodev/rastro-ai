namespace Rastro.Core.Graph;

// Posição no código: arquivo relativo à pasta da solution (separador "/") e linha (base 1).
public record SourceLocation(string File, int Line);

// Regra do nó em linguagem humana. Sempre com a origem: quem escreveu fica visível (ADR-001).
public record NodeRule(string Text, ProposalOrigin Origin);

// Nó do grafo. Change e Intent só existem no rascunho de edição (Fase 7): a marca de mudança e,
// em linguagem do usuário, o que o passo deve fazer. Id estável entre análises do mesmo código:
//   m:<id do símbolo>  método     x:<id do símbolo>  externo
//   <tipo>:<arquivo>:<linha>:<coluna>  bifurcação, laço, try, return, throw, não resolvido
public record FlowNode(
    string Id,
    NodeKind Kind,
    string Label,
    string? Symbol = null,
    SourceLocation? Location = null,
    string? Condition = null,
    UnresolvedReason? Reason = null,
    string? Detail = null,
    NodeRule? Rule = null,
    ChangeKind? Change = null,
    string? Intent = null,
    MethodMetrics? Metrics = null,
    // Assembly (projeto) do método ou da chamada externa: base dos containers e sistemas do C4 (Fase 11).
    string? Project = null,
    // Repositório cadastrado a que o nó pertence quando o fluxo atravessa sistemas (Fase 25).
    // Nulo: primeiro trecho, o do repositório do levantamento.
    string? System = null);

// Chamada HTTP de uma aresta entre sistemas. Method em maiúsculas (GET, POST, PUT, PATCH, DELETE);
// Route sem host e sem "/" no começo (api/pedidos/{id}/cancelamento), nula quando não resolveu.
public record HttpCall(string Method, string? Route);

// Aresta do grafo. Order é a sequência de execução entre as arestas que saem do mesmo nó.
// Label diz o ramo (then, else, case 1, try, catch (Ex), finally, each) ou fica nulo numa
// chamada em sequência. Origin só existe quando a aresta veio de proposta (inferida ou confirmada).
// Location é onde a chamada acontece, quando o nó de destino não diz isso sozinho: o externo
// (HttpClient.PostAsync) é um nó só para várias chamadas e não tem arquivo nem linha (Fase 13).
public record FlowEdge(
    string From,
    string To,
    int Order,
    EdgeConfidence Confidence,
    string? Label = null,
    ProposalOrigin? Origin = null,
    ChangeKind? Change = null,
    SourceLocation? Location = null,
    // Só nas arestas que cruzam de um sistema para outro por HTTP (Fase 25).
    HttpCall? Http = null);

public record FlowGraph(
    int SchemaVersion,
    string EntryPoint,
    string EntryNodeId,
    IReadOnlyList<FlowNode> Nodes,
    IReadOnlyList<FlowEdge> Edges,
    IReadOnlyList<Finding>? Findings = null);
