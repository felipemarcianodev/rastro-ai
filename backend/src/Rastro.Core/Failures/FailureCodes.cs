namespace Rastro.Core.Failures;

// Falha do levantamento. Viaja como código (int no JSON) e parâmetros; a tela traduz pelas chaves
// failure.survey.<membro>. O texto em português de FailureText continua gravado ao lado, para o log,
// para o MCP e para quem lê o banco.
public enum SurveyFailure
{
    // Erro que o Rastro não previu. O detalhe técnico fica só no log.
    Unexpected = 0,
    Interrupted = 1,
    TargetUnavailable = 2,
    // Args: ponto de entrada.
    EntryInvalid = 3,
    // Args: ponto de entrada, nome do método.
    EntryNotFound = 4,
    // Args: ponto de entrada, nome do método, sugestões (separadas por vírgula).
    EntryNotFoundSuggest = 5,
    // Args: ponto de entrada, nome do método, sugestões, quantas a mais.
    EntryNotFoundSuggestMore = 6,
    // Args: ponto de entrada, lista de tipos candidatos (Tipo.Metodo, separados por vírgula).
    EntryAmbiguous = 7,
    AngularScriptMissing = 8,
    // Args: segundos do limite.
    AngularTimeout = 9,
    NodeMissing = 10,
    AngularUnreadable = 11,
    // Args: a mensagem que o próprio script do extrator devolveu.
    AngularScriptError = 12,
    RestoreFailed = 13
}

// Falha da validação da mudança.
public enum ValidationFailure
{
    Unexpected = 0,
    Interrupted = 1,
    DraftDeleted = 2,
    TargetUnavailable = 3,
    AngularUnsupported = 4,
    // Args: ponto de entrada.
    EntryGone = 5,
    // A análise do código novo falhou. Args: o código do SurveyFailure, depois os parâmetros dele.
    Analysis = 6
}

// Motivo de recusa de uma proposta de agente ou de provedor.
public enum ProposalRefusal
{
    SurveyNotFound = 0,
    NodeNotFound = 1,
    // Args: mínimo, máximo.
    RuleLength = 2,
    NotUnresolved = 3,
    TargetUnavailable = 4,
    AngularUnsupported = 5,
    CodeChanged = 6,
    // A solution não abriu. Args: o código do SurveyFailure, depois os parâmetros dele.
    AnalysisFailed = 7,
    DuplicateTarget = 8,
    HttpNeedsSystemNoCross = 9,
    // Args: repositórios da costura (separados por vírgula).
    HttpNeedsSystem = 10,
    HttpCallMissing = 11,
    // Args: repositório de destino.
    DestinationUnavailable = 12,
    // Args: repositório de destino.
    DestinationCodeChanged = 13,
    ProofIncomplete = 14,
    // O método de destino não existe. Args: o código do SurveyFailure, depois os parâmetros dele.
    ProofTargetInvalid = 15,
    // Args: método.
    ProofNoBody = 16,
    // Args: método, arquivo real, linha, arquivo declarado.
    ProofWrongFile = 17,
    // Args: método, primeira linha, última linha, arquivo, linha declarada.
    ProofLineOutside = 18,
    // Args: método de destino.
    RouteNotIndexed = 19,
    // Args: método de destino, verbo da chamada.
    VerbMismatch = 20,
    // Args: método de destino, rota servida, verbo da chamada, rota chamada.
    RouteMismatch = 21
}

// Erro do rascunho de edição do fluxo.
public enum DraftError
{
    EntryChanged = 0,
    // Args: ids repetidos (separados por vírgula).
    DuplicateNodes = 1,
    // Args: nome do passo.
    CodeStepMissing = 2,
    CodeStepAltered = 3,
    CodeStepMarkedNew = 4,
    CodeStepModifiedNoIntent = 5,
    // Args: máximo.
    TooManyAdded = 6,
    // Args: nome do passo, prefixo do id.
    NewStepBadId = 7,
    // Args: máximo.
    NewStepNoName = 8,
    // Args: nome do passo.
    NewStepNoIntent = 9,
    NewStepBadKind = 10,
    // Args: nome do passo, máximo.
    IntentTooLong = 11,
    // Args: origem, destino.
    EdgeMissing = 12,
    EdgeMarkedNew = 13,
    EdgeDangling = 14,
    ForeignSystem = 15
}

// Código e parâmetros de uma falha, como saem para a tela.
public record FailureInfo(int Code, IReadOnlyList<string> Args);
