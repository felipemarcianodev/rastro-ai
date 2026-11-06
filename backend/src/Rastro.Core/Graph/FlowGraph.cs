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
    string? System = null,
    // O que o método lê e grava no banco pelo EF Core (Fase 39). Nulo quando não toca o banco.
    IReadOnlyList<DbAccess>? Db = null);

// Operação de um passo no banco (Fase 39), pelo método do EF: consulta, inclusão, alteração,
// exclusão ou execução de procedure por SQL cru.
public enum DbOperation
{
    Read = 0,
    Insert = 1,
    Update = 2,
    Delete = 3,
    Execute = 4
}

// Target: a tabela (nome do esquema lido do código, ou o do DbSet quando o modelo não diz) ou, na
// execução, a procedure. Entity: a classe da entidade, quando há. Line: linha da chamada no arquivo do método.
// Sql (Fase 40): o texto do SQL cru (FromSql, ExecuteSql, SqlQuery) quando é literal no código, com os
// marcadores {0} e os buracos da interpolação trocados por @p0, @p1, para a tela analisar a consulta.
public record DbAccess(string Target, DbOperation Operation, int Line, string? Entity = null, string? Sql = null);

// Chamada HTTP de uma aresta entre sistemas. Method em maiúsculas (GET, POST, PUT, PATCH, DELETE);
// Route sem host e sem "/" no começo (api/pedidos/{id}/cancelamento), nula quando não resolveu.
//
// Fields (Fase 34): campos que a chamada envia, na ordem de declaração e sem repetidos: propriedades do
// corpo e parâmetros nomeados (rota e query). Nulo quando não há campo lido. MoreFields marca que havia
// campos que o extrator não conseguiu ler (espalhamento de outro objeto, corpo montado à parte), para a
// tela dizer "e outros campos" em vez de omitir. A igualdade compara os campos por valor.
public record HttpCall(string Method, string? Route, IReadOnlyList<string>? Fields = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool MoreFields = false)
{
    public virtual bool Equals(HttpCall? other) =>
        other is not null
        && Method == other.Method
        && Route == other.Route
        && MoreFields == other.MoreFields
        && (Fields is null || other.Fields is null ? Fields is null && other.Fields is null : Fields.SequenceEqual(other.Fields));

    public override int GetHashCode() => HashCode.Combine(Method, Route, MoreFields, Fields?.Count ?? 0);
}

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
