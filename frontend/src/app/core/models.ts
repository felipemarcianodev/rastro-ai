// Contrato com a API (Rastro.Core.Graph e Rastro.Api.Controllers). Enums viajam como int:
// os valores aqui espelham os do C# e não podem mudar. Slug e rótulo saem sempre de Record.

export enum NodeKind {
  Entry = 0,
  Method = 1,
  Decision = 2,
  Loop = 3,
  Try = 4,
  Return = 5,
  Throw = 6,
  External = 7,
  Unresolved = 8,
  Jump = 9,
}

export enum EdgeConfidence {
  Extracted = 0,
  Inferred = 1,
  Confirmed = 2,
}

export enum ProposalOrigin {
  Manual = 0,
  Agent = 1,
  Provider = 2,
}

export enum UnresolvedReason {
  MultipleImplementations = 0,
  NoImplementation = 1,
  DynamicDispatch = 2,
  Delegate = 3,
  MessageDispatch = 4,
  Reflection = 5,
  DepthLimit = 6,
  RouteNotResolved = 7,
  NoMatchingEndpoint = 8,
  MultipleEndpoints = 9,
}

export enum SurveyStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
}

export interface SourceLocation {
  file: string;
  line: number;
}

// Marca de mudança no rascunho de edição (o grafo extraído nunca tem).
export enum ChangeKind {
  Added = 0,
  Removed = 1,
  Modified = 2,
}

export const CHANGE_KIND_SLUG: Record<ChangeKind, string> = {
  [ChangeKind.Added]: 'added',
  [ChangeKind.Removed]: 'removed',
  [ChangeKind.Modified]: 'modified',
};

export const CHANGE_KIND_LABEL_KEY: Record<ChangeKind, string> = {
  [ChangeKind.Added]: 'enum.changeKind.added',
  [ChangeKind.Removed]: 'enum.changeKind.removed',
  [ChangeKind.Modified]: 'enum.changeKind.modified',
};

export interface NodeRule {
  text: string;
  origin: ProposalOrigin;
}

export interface FlowNode {
  id: string;
  kind: NodeKind;
  label: string;
  symbol?: string;
  location?: SourceLocation;
  condition?: string;
  reason?: UnresolvedReason;
  detail?: string;
  rule?: NodeRule;
  change?: ChangeKind;
  intent?: string;
  metrics?: MethodMetrics;
  // Repositório de onde o passo vem, quando não é o primeiro sistema do fluxo (Fase 25).
  system?: string;
  // O que o método lê e grava no banco pelo EF Core (Fase 39).
  db?: DbAccess[];
}

// Operação de um passo no banco (Fase 39).
export enum DbOperation {
  Read = 0,
  Insert = 1,
  Update = 2,
  Delete = 3,
  Execute = 4,
}

// target: a tabela ou, na execução, a procedure; line: a linha da chamada no arquivo do método.
export interface DbAccess {
  target: string;
  operation: DbOperation;
  line: number;
  entity?: string;
  // SQL cru do passo, quando é um literal no código (Fase 40a).
  sql?: string | null;
}

export const DB_OPERATION_LABEL_KEY: Record<DbOperation, string> = {
  [DbOperation.Read]: 'enum.dbOperation.read',
  [DbOperation.Insert]: 'enum.dbOperation.insert',
  [DbOperation.Update]: 'enum.dbOperation.update',
  [DbOperation.Delete]: 'enum.dbOperation.delete',
  [DbOperation.Execute]: 'enum.dbOperation.execute',
};

export const DB_OPERATION_SHORT_KEY: Record<DbOperation, string> = {
  [DbOperation.Read]: 'enum.dbOperationShort.read',
  [DbOperation.Insert]: 'enum.dbOperationShort.insert',
  [DbOperation.Update]: 'enum.dbOperationShort.update',
  [DbOperation.Delete]: 'enum.dbOperationShort.delete',
  [DbOperation.Execute]: 'enum.dbOperationShort.execute',
};

export const DB_OPERATION_SLUG: Record<DbOperation, string> = {
  [DbOperation.Read]: 'read',
  [DbOperation.Insert]: 'insert',
  [DbOperation.Update]: 'update',
  [DbOperation.Delete]: 'delete',
  [DbOperation.Execute]: 'execute',
};

// Chamada HTTP entre sistemas (Fase 25): verbo em maiúsculas e rota sem host, nula se não resolveu.
export interface HttpCall {
  method: string;
  route?: string;
}

export interface FlowEdge {
  from: string;
  to: string;
  order: number;
  confidence: EdgeConfidence;
  label?: string;
  origin?: ProposalOrigin;
  change?: ChangeKind;
  // Onde a chamada acontece, quando o destino não tem posição própria (nó externo, Fase 13).
  location?: SourceLocation;
  // Presente quando a ligação é uma chamada HTTP entre sistemas.
  http?: HttpCall;
}

export interface FlowGraph {
  schemaVersion: number;
  entryPoint: string;
  entryNodeId: string;
  nodes: FlowNode[];
  edges: FlowEdge[];
  findings?: Finding[];
}

// Linguagem de um trecho do fluxo (Fase 26): .NET lê a solution; Angular lê o tsconfig.
export enum SourceLanguage {
  DotNet = 0,
  Angular = 1,
}

export const SOURCE_LANGUAGE_LABEL: Record<SourceLanguage, string> = {
  [SourceLanguage.DotNet]: '.NET',
  [SourceLanguage.Angular]: 'Angular',
};

// Trecho do fluxo em outro repositório (Fase 25), na ordem em que foi costurado.
export interface SurveySegment {
  order: number;
  language: SourceLanguage;
  repository: string;
  solutionPath: string;
  entryPoint: string;
  commitHash?: string;
  branch?: string;
}

export interface SurveySummary {
  id: string;
  repository: string;
  solutionPath: string;
  language: SourceLanguage;
  entryPoint: string;
  maxDepth: number;
  commitHash?: string;
  branch?: string;
  status: SurveyStatus;
  error?: string;
  // Código e parâmetros da falha (a tela traduz por eles); nulos nas falhas gravadas antes da Fase 49.
  failure?: SurveyFailure | null;
  failureArgs?: string[];
  schemaVersion: number;
  nodeCount: number;
  edgeCount: number;
  unresolvedCount: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  crossRepositories: string[];
  segments: SurveySegment[];
}

export interface SurveyDetail {
  survey: SurveySummary;
  graph?: FlowGraph;
}

export interface SegmentStaleness {
  repository: string;
  stale: boolean;
  surveyCommit?: string;
  currentCommit?: string;
}

export interface SurveyStaleness {
  stale: boolean;
  surveyCommit?: string;
  currentCommit?: string;
  // Um por trecho além do primeiro (Fase 25).
  segments: SegmentStaleness[];
}

// Resposta de GET /api/health. "angular": o Node está instalado e dá para levantar fluxos Angular.
export interface RastroHealth {
  status: string;
  version: string;
  schemaVersion: number;
  angular: boolean;
}

export interface CreateSurveyRequest {
  repository: string;
  solutionPath: string;
  entryPoint: string;
  maxDepth?: number;
  // Repositórios cujas chamadas HTTP o levantamento segue (Fase 25).
  crossRepositories?: string[];
}

export interface RepositoryItem {
  name: string;
  // Pasta absoluta cadastrada (a API é local; vale também para o "Abrir no VS Code").
  path: string;
  hasGit: boolean;
  hasBuildCommand: boolean;
  hasTestCommand: boolean;
  // Texto dos comandos (ADR-008); a tela mostra e edita.
  buildCommand?: string | null;
  testCommand?: string | null;
}

// Mesma regra do contrato da API (REPOSITORY_NAME_INVALID).
export const REPOSITORY_NAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/;

export interface EdgeDecision {
  from: string;
  to: string;
  confirm: boolean;
}

export interface SourceSnippet {
  file: string;
  startLine: number;
  highlightLine: number;
  lines: string[];
}

// Rótulos e slugs ------------------------------------------------------------

export const NODE_KIND_SLUG: Record<NodeKind, string> = {
  [NodeKind.Entry]: 'entry',
  [NodeKind.Method]: 'method',
  [NodeKind.Decision]: 'decision',
  [NodeKind.Loop]: 'loop',
  [NodeKind.Try]: 'try',
  [NodeKind.Return]: 'return',
  [NodeKind.Throw]: 'throw',
  [NodeKind.External]: 'external',
  [NodeKind.Unresolved]: 'unresolved',
  [NodeKind.Jump]: 'jump',
};

export const NODE_KIND_LABEL_KEY: Record<NodeKind, string> = {
  [NodeKind.Entry]: 'enum.nodeKind.entry',
  [NodeKind.Method]: 'enum.nodeKind.method',
  [NodeKind.Decision]: 'enum.nodeKind.decision',
  [NodeKind.Loop]: 'enum.nodeKind.loop',
  [NodeKind.Try]: 'enum.nodeKind.try',
  [NodeKind.Return]: 'enum.nodeKind.return',
  [NodeKind.Throw]: 'enum.nodeKind.throw',
  [NodeKind.External]: 'enum.nodeKind.external',
  [NodeKind.Unresolved]: 'enum.nodeKind.unresolved',
  [NodeKind.Jump]: 'enum.nodeKind.jump',
};

// Letra do selo do nó: identifica o tipo sem depender de cor.
export const NODE_KIND_GLYPH: Record<NodeKind, string> = {
  [NodeKind.Entry]: 'E',
  [NodeKind.Method]: 'M',
  [NodeKind.Decision]: '?',
  [NodeKind.Loop]: '↻',
  [NodeKind.Try]: 'T',
  [NodeKind.Return]: '↩',
  [NodeKind.Throw]: '!',
  [NodeKind.External]: '↗',
  [NodeKind.Unresolved]: '…',
  [NodeKind.Jump]: '⤳',
};

export const EDGE_CONFIDENCE_SLUG: Record<EdgeConfidence, string> = {
  [EdgeConfidence.Extracted]: 'extracted',
  [EdgeConfidence.Inferred]: 'inferred',
  [EdgeConfidence.Confirmed]: 'confirmed',
};

export const EDGE_CONFIDENCE_LABEL_KEY: Record<EdgeConfidence, string> = {
  [EdgeConfidence.Extracted]: 'enum.edgeConfidence.extracted',
  [EdgeConfidence.Inferred]: 'enum.edgeConfidence.inferred',
  [EdgeConfidence.Confirmed]: 'enum.edgeConfidence.confirmed',
};

// Selo curto da origem de uma regra ou ligação (painel e linha do tempo).
export const PROPOSAL_ORIGIN_BADGE_KEY: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'enum.proposalOrigin.manual',
  [ProposalOrigin.Agent]: 'enum.proposalOrigin.agent',
  [ProposalOrigin.Provider]: 'enum.proposalOrigin.provider',
};

export const PROPOSAL_ORIGIN_SLUG: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'manual',
  [ProposalOrigin.Agent]: 'agent',
  [ProposalOrigin.Provider]: 'provider',
};

export const UNRESOLVED_REASON_LABEL_KEY: Record<UnresolvedReason, string> = {
  [UnresolvedReason.MultipleImplementations]: 'enum.unresolvedReason.multipleImplementations',
  [UnresolvedReason.NoImplementation]: 'enum.unresolvedReason.noImplementation',
  [UnresolvedReason.DynamicDispatch]: 'enum.unresolvedReason.dynamicDispatch',
  [UnresolvedReason.Delegate]: 'enum.unresolvedReason.delegate',
  [UnresolvedReason.MessageDispatch]: 'enum.unresolvedReason.messageDispatch',
  [UnresolvedReason.Reflection]: 'enum.unresolvedReason.reflection',
  [UnresolvedReason.DepthLimit]: 'enum.unresolvedReason.depthLimit',
  [UnresolvedReason.RouteNotResolved]: 'enum.unresolvedReason.routeNotResolved',
  [UnresolvedReason.NoMatchingEndpoint]: 'enum.unresolvedReason.noMatchingEndpoint',
  [UnresolvedReason.MultipleEndpoints]: 'enum.unresolvedReason.multipleEndpoints',
};

// O que o motivo significa, em uma frase, para quem vai completar o buraco.
export const UNRESOLVED_REASON_HELP_KEY: Record<UnresolvedReason, string> = {
  [UnresolvedReason.MultipleImplementations]: 'enum.unresolvedReasonHint.multipleImplementations',
  [UnresolvedReason.NoImplementation]: 'enum.unresolvedReasonHint.noImplementation',
  [UnresolvedReason.DynamicDispatch]: 'enum.unresolvedReasonHint.dynamicDispatch',
  [UnresolvedReason.Delegate]: 'enum.unresolvedReasonHint.delegate',
  [UnresolvedReason.MessageDispatch]: 'enum.unresolvedReasonHint.messageDispatch',
  [UnresolvedReason.Reflection]: 'enum.unresolvedReasonHint.reflection',
  [UnresolvedReason.DepthLimit]: 'enum.unresolvedReasonHint.depthLimit',
  [UnresolvedReason.RouteNotResolved]: 'enum.unresolvedReasonHint.routeNotResolved',
  [UnresolvedReason.NoMatchingEndpoint]: 'enum.unresolvedReasonHint.noMatchingEndpoint',
  [UnresolvedReason.MultipleEndpoints]: 'enum.unresolvedReasonHint.multipleEndpoints',
};

export const SURVEY_STATUS_SLUG: Record<SurveyStatus, string> = {
  [SurveyStatus.Pending]: 'pending',
  [SurveyStatus.Running]: 'running',
  [SurveyStatus.Completed]: 'completed',
  [SurveyStatus.Failed]: 'failed',
};

export const SURVEY_STATUS_LABEL_KEY: Record<SurveyStatus, string> = {
  [SurveyStatus.Pending]: 'enum.surveyStatus.pending',
  [SurveyStatus.Running]: 'enum.surveyStatus.running',
  [SurveyStatus.Completed]: 'enum.surveyStatus.completed',
  [SurveyStatus.Failed]: 'enum.surveyStatus.failed',
};

// Falha do levantamento, da validação, motivo de recusa de proposta e erro do rascunho (Fase 49): o servidor manda
// o código (int) e os parâmetros, e a tela traduz. Os valores são os mesmos dos enums do servidor.
export enum SurveyFailure {
  Unexpected = 0,
  Interrupted = 1,
  TargetUnavailable = 2,
  EntryInvalid = 3,
  EntryNotFound = 4,
  EntryNotFoundSuggest = 5,
  EntryNotFoundSuggestMore = 6,
  EntryAmbiguous = 7,
  AngularScriptMissing = 8,
  AngularTimeout = 9,
  NodeMissing = 10,
  AngularUnreadable = 11,
  AngularScriptError = 12,
  RestoreFailed = 13,
}

export enum ValidationFailure {
  Unexpected = 0,
  Interrupted = 1,
  DraftDeleted = 2,
  TargetUnavailable = 3,
  AngularUnsupported = 4,
  EntryGone = 5,
  Analysis = 6,
}

export enum ProposalRefusal {
  SurveyNotFound = 0,
  NodeNotFound = 1,
  RuleLength = 2,
  NotUnresolved = 3,
  TargetUnavailable = 4,
  AngularUnsupported = 5,
  CodeChanged = 6,
  AnalysisFailed = 7,
  DuplicateTarget = 8,
  HttpNeedsSystemNoCross = 9,
  HttpNeedsSystem = 10,
  HttpCallMissing = 11,
  DestinationUnavailable = 12,
  DestinationCodeChanged = 13,
  ProofIncomplete = 14,
  ProofTargetInvalid = 15,
  ProofNoBody = 16,
  ProofWrongFile = 17,
  ProofLineOutside = 18,
  RouteNotIndexed = 19,
  VerbMismatch = 20,
  RouteMismatch = 21,
}

export enum DraftError {
  EntryChanged = 0,
  DuplicateNodes = 1,
  CodeStepMissing = 2,
  CodeStepAltered = 3,
  CodeStepMarkedNew = 4,
  CodeStepModifiedNoIntent = 5,
  TooManyAdded = 6,
  NewStepBadId = 7,
  NewStepNoName = 8,
  NewStepNoIntent = 9,
  NewStepBadKind = 10,
  IntentTooLong = 11,
  EdgeMissing = 12,
  EdgeMarkedNew = 13,
  EdgeDangling = 14,
  ForeignSystem = 15,
}

// Os marcadores dos textos são a0, a1... entre chaves (os parâmetros na ordem em que o servidor manda).
export const SURVEY_FAILURE_LABEL_KEY: Record<SurveyFailure, string> = {
  [SurveyFailure.Unexpected]: 'failure.survey.unexpected',
  [SurveyFailure.Interrupted]: 'failure.survey.interrupted',
  [SurveyFailure.TargetUnavailable]: 'failure.survey.targetUnavailable',
  [SurveyFailure.EntryInvalid]: 'failure.survey.entryInvalid',
  [SurveyFailure.EntryNotFound]: 'failure.survey.entryNotFound',
  [SurveyFailure.EntryNotFoundSuggest]: 'failure.survey.entryNotFoundSuggest',
  [SurveyFailure.EntryNotFoundSuggestMore]: 'failure.survey.entryNotFoundSuggestMore',
  [SurveyFailure.EntryAmbiguous]: 'failure.survey.entryAmbiguous',
  [SurveyFailure.AngularScriptMissing]: 'failure.survey.angularScriptMissing',
  [SurveyFailure.AngularTimeout]: 'failure.survey.angularTimeout',
  [SurveyFailure.NodeMissing]: 'failure.survey.nodeMissing',
  [SurveyFailure.AngularUnreadable]: 'failure.survey.angularUnreadable',
  [SurveyFailure.AngularScriptError]: 'failure.survey.angularScriptError',
  [SurveyFailure.RestoreFailed]: 'failure.survey.restoreFailed',
};

// Analysis leva a causa aninhada: o primeiro parâmetro é o código do SurveyFailure (o marcador é reason).
export const VALIDATION_FAILURE_LABEL_KEY: Record<ValidationFailure, string> = {
  [ValidationFailure.Unexpected]: 'failure.validation.unexpected',
  [ValidationFailure.Interrupted]: 'failure.validation.interrupted',
  [ValidationFailure.DraftDeleted]: 'failure.validation.draftDeleted',
  [ValidationFailure.TargetUnavailable]: 'failure.validation.targetUnavailable',
  [ValidationFailure.AngularUnsupported]: 'failure.validation.angularUnsupported',
  [ValidationFailure.EntryGone]: 'failure.validation.entryGone',
  [ValidationFailure.Analysis]: 'failure.validation.analysis',
};

// AnalysisFailed e ProofTargetInvalid levam a causa aninhada (marcador reason).
export const PROPOSAL_REFUSAL_LABEL_KEY: Record<ProposalRefusal, string> = {
  [ProposalRefusal.SurveyNotFound]: 'refusal.surveyNotFound',
  [ProposalRefusal.NodeNotFound]: 'refusal.nodeNotFound',
  [ProposalRefusal.RuleLength]: 'refusal.ruleLength',
  [ProposalRefusal.NotUnresolved]: 'refusal.notUnresolved',
  [ProposalRefusal.TargetUnavailable]: 'refusal.targetUnavailable',
  [ProposalRefusal.AngularUnsupported]: 'refusal.angularUnsupported',
  [ProposalRefusal.CodeChanged]: 'refusal.codeChanged',
  [ProposalRefusal.AnalysisFailed]: 'refusal.analysisFailed',
  [ProposalRefusal.DuplicateTarget]: 'refusal.duplicateTarget',
  [ProposalRefusal.HttpNeedsSystemNoCross]: 'refusal.httpNeedsSystemNoCross',
  [ProposalRefusal.HttpNeedsSystem]: 'refusal.httpNeedsSystem',
  [ProposalRefusal.HttpCallMissing]: 'refusal.httpCallMissing',
  [ProposalRefusal.DestinationUnavailable]: 'refusal.destinationUnavailable',
  [ProposalRefusal.DestinationCodeChanged]: 'refusal.destinationCodeChanged',
  [ProposalRefusal.ProofIncomplete]: 'refusal.proofIncomplete',
  [ProposalRefusal.ProofTargetInvalid]: 'refusal.proofTargetInvalid',
  [ProposalRefusal.ProofNoBody]: 'refusal.proofNoBody',
  [ProposalRefusal.ProofWrongFile]: 'refusal.proofWrongFile',
  [ProposalRefusal.ProofLineOutside]: 'refusal.proofLineOutside',
  [ProposalRefusal.RouteNotIndexed]: 'refusal.routeNotIndexed',
  [ProposalRefusal.VerbMismatch]: 'refusal.verbMismatch',
  [ProposalRefusal.RouteMismatch]: 'refusal.routeMismatch',
};

export const DRAFT_ERROR_LABEL_KEY: Record<DraftError, string> = {
  [DraftError.EntryChanged]: 'draftError.entryChanged',
  [DraftError.DuplicateNodes]: 'draftError.duplicateNodes',
  [DraftError.CodeStepMissing]: 'draftError.codeStepMissing',
  [DraftError.CodeStepAltered]: 'draftError.codeStepAltered',
  [DraftError.CodeStepMarkedNew]: 'draftError.codeStepMarkedNew',
  [DraftError.CodeStepModifiedNoIntent]: 'draftError.codeStepModifiedNoIntent',
  [DraftError.TooManyAdded]: 'draftError.tooManyAdded',
  [DraftError.NewStepBadId]: 'draftError.newStepBadId',
  [DraftError.NewStepNoName]: 'draftError.newStepNoName',
  [DraftError.NewStepNoIntent]: 'draftError.newStepNoIntent',
  [DraftError.NewStepBadKind]: 'draftError.newStepBadKind',
  [DraftError.IntentTooLong]: 'draftError.intentTooLong',
  [DraftError.EdgeMissing]: 'draftError.edgeMissing',
  [DraftError.EdgeMarkedNew]: 'draftError.edgeMarkedNew',
  [DraftError.EdgeDangling]: 'draftError.edgeDangling',
  [DraftError.ForeignSystem]: 'draftError.foreignSystem',
};

export const API_ERROR_MESSAGE_KEY: Record<string, string> = {
  REPOSITORY_COMMAND_INVALID: 'error.REPOSITORY_COMMAND_INVALID',
  LANGUAGE_INVALID: 'error.LANGUAGE_INVALID',
  REPOSITORY_NOT_ALLOWED: 'error.REPOSITORY_NOT_ALLOWED',
  SOLUTION_PATH_INVALID: 'error.SOLUTION_PATH_INVALID',
  ANGULAR_UNAVAILABLE: 'error.ANGULAR_UNAVAILABLE',
  SOLUTION_NOT_FOUND: 'error.SOLUTION_NOT_FOUND',
  ENTRY_POINT_REQUIRED: 'error.ENTRY_POINT_REQUIRED',
  DEPTH_OUT_OF_RANGE: 'error.DEPTH_OUT_OF_RANGE',
  SOURCE_PATH_INVALID: 'error.SOURCE_PATH_INVALID',
  EDGE_NOT_PROPOSED: 'error.EDGE_NOT_PROPOSED',
  DRAFT_INVALID: 'error.DRAFT_INVALID',
  DRAFT_REQUIRED: 'error.DRAFT_REQUIRED',
  VALIDATION_RUNNING: 'error.VALIDATION_RUNNING',
  VALIDATION_NOT_APPROVABLE: 'error.VALIDATION_NOT_APPROVABLE',
  C4_PEOPLE_INVALID: 'error.C4_PEOPLE_INVALID',
  NOTE_INVALID: 'error.NOTE_INVALID',
  DIAGRAM_INVALID: 'error.DIAGRAM_INVALID',
  SURVEY_RUNNING: 'error.SURVEY_RUNNING',
  AI_SETTINGS_INVALID: 'error.AI_SETTINGS_INVALID',
  AI_DISABLED: 'error.AI_DISABLED',
  AI_USE_DISABLED: 'error.AI_USE_DISABLED',
  BUSINESS_LABEL_INVALID: 'error.BUSINESS_LABEL_INVALID',
  BUSINESS_KIND_INVALID: 'error.BUSINESS_KIND_INVALID',
  BUSINESS_STEP_NOT_FOUND: 'error.BUSINESS_STEP_NOT_FOUND',
  BUSINESS_PROPOSAL_NOT_FOUND: 'error.BUSINESS_PROPOSAL_NOT_FOUND',
  AI_NOT_CONFIGURED: 'error.AI_NOT_CONFIGURED',
  AI_BUDGET_EXCEEDED: 'error.AI_BUDGET_EXCEEDED',
  AI_PROVIDER_ERROR: 'error.AI_PROVIDER_ERROR',
  AI_QUESTION_INVALID: 'error.AI_QUESTION_INVALID',
  REPOSITORY_NAME_INVALID: 'error.REPOSITORY_NAME_INVALID',
  REPOSITORY_PATH_INVALID: 'error.REPOSITORY_PATH_INVALID',
  REPOSITORY_FOLDER_NOT_FOUND: 'error.REPOSITORY_FOLDER_NOT_FOUND',
  REPOSITORY_EXISTS: 'error.REPOSITORY_EXISTS',
  REPOSITORY_NOT_FOUND: 'error.REPOSITORY_NOT_FOUND',
  CROSS_REPOSITORIES_INVALID: 'error.CROSS_REPOSITORIES_INVALID',
  QUERY_INVALID: 'error.QUERY_INVALID',
  CONNECTION_INVALID: 'error.CONNECTION_INVALID',
  CONNECTION_MISSING: 'error.CONNECTION_MISSING',
  PLAN_NOT_ALLOWED: 'error.PLAN_NOT_ALLOWED',
  LIVE_DATABASE_ERROR: 'error.LIVE_DATABASE_ERROR',
  ORIGIN_NOT_ALLOWED: 'error.ORIGIN_NOT_ALLOWED',
  BOARD_NOT_FOUND: 'error.BOARD_NOT_FOUND',
  BOARD_NAME_INVALID: 'error.BOARD_NAME_INVALID',
  BOARD_GLOSSARY_INVALID: 'error.BOARD_GLOSSARY_INVALID',
  BOARD_LANES_INVALID: 'error.BOARD_LANES_INVALID',
  BOARD_SURVEY_NOT_FOUND: 'error.BOARD_SURVEY_NOT_FOUND',
  BOARD_SURVEY_NOT_READY: 'error.BOARD_SURVEY_NOT_READY',
  BOARD_LANE_EXISTS: 'error.BOARD_LANE_EXISTS',
  BOARD_LANE_NOT_FOUND: 'error.BOARD_LANE_NOT_FOUND',
};

export function isFinished(status: SurveyStatus): boolean {
  return status === SurveyStatus.Completed || status === SurveyStatus.Failed;
}

// IA (modo provedor) ---------------------------------------------------------

export enum AiProvider {
  Claude = 0,
  OpenAi = 1,
  Gemini = 2,
}

export enum AiUse {
  FillHoles = 0,
  DescribeRules = 1,
  Suggestions = 2,
  ContextSuggestions = 3,
}

export const AI_PROVIDER_LABEL: Record<AiProvider, string> = {
  [AiProvider.Claude]: 'Claude (Anthropic)',
  [AiProvider.OpenAi]: 'OpenAI',
  [AiProvider.Gemini]: 'Gemini (Google)',
};

export const AI_PROVIDER_SLUG: Record<AiProvider, 'claude' | 'openAi' | 'gemini'> = {
  [AiProvider.Claude]: 'claude',
  [AiProvider.OpenAi]: 'openAi',
  [AiProvider.Gemini]: 'gemini',
};

export interface AiSettingsView {
  enabled: boolean;
  provider: AiProvider;
  claudeModel: string;
  claudeHasKey: boolean;
  openAiModel: string;
  openAiHasKey: boolean;
  geminiModel: string;
  geminiHasKey: boolean;
  dailyTokenBudget: number;
  useFillHoles: boolean;
  useDescribeRules: boolean;
  useSuggestions: boolean;
  useContextSuggestions: boolean;
  updatedAt: string;
}

// Chave: null mantém a atual, '' apaga, texto troca.
export interface AiSettingsUpdate {
  enabled: boolean;
  provider: AiProvider;
  claudeModel: string;
  claudeKey: string | null;
  openAiModel: string;
  openAiKey: string | null;
  geminiModel: string;
  geminiKey: string | null;
  dailyTokenBudget: number;
  useFillHoles: boolean;
  useDescribeRules: boolean;
  useSuggestions: boolean;
  useContextSuggestions: boolean;
}

export interface AiStatus {
  enabled: boolean;
  configured: boolean;
  provider: AiProvider;
  model: string;
  uses: AiUse[];
  tokensToday: number;
  dailyTokenBudget: number;
}

// Um destino proposto pela IA e o que o validador decidiu; recusado traz o motivo em código.
export interface FillHoleItem {
  target: string;
  accepted: boolean;
  message: string;
  refusal?: ProposalRefusal | null;
  refusalArgs: string[];
}

export interface FillHoleResult {
  proposed: number;
  accepted: number;
  messages: string[];
  items?: FillHoleItem[];
}

export interface FillHoleResponse {
  result: FillHoleResult;
  survey: SurveyDetail;
}

export function aiCan(status: AiStatus | null, use: AiUse): boolean {
  return !!status && status.enabled && status.configured && status.uses.includes(use);
}

export interface DescribeResult {
  written: number;
  fromCache: number;
  messages: string[];
}

export interface DescribeResponse {
  result: DescribeResult;
  survey: SurveyDetail;
}

export interface ChangePrompt {
  prompt: string;
  changes: number;
}

// Validação da mudança (Fase 9). Enums numéricos, iguais aos do backend.
export enum ValidationStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
}

export enum CheckStatus {
  Passed = 0,
  Failed = 1,
  Skipped = 2,
}

export enum ValidationVerdict {
  Approved = 0,
  Partial = 1,
  Rejected = 2,
}

export const VALIDATION_VERDICT_LABEL_KEY: Record<ValidationVerdict, string> = {
  [ValidationVerdict.Approved]: 'enum.validationVerdict.approved',
  [ValidationVerdict.Partial]: 'enum.validationVerdict.partial',
  [ValidationVerdict.Rejected]: 'enum.validationVerdict.rejected',
};

export const VALIDATION_VERDICT_SLUG: Record<ValidationVerdict, string> = {
  [ValidationVerdict.Approved]: 'approved',
  [ValidationVerdict.Partial]: 'partial',
  [ValidationVerdict.Rejected]: 'rejected',
};

export const CHECK_STATUS_LABEL_KEY: Record<CheckStatus, string> = {
  [CheckStatus.Passed]: 'enum.checkStatus.passed',
  [CheckStatus.Failed]: 'enum.checkStatus.failed',
  [CheckStatus.Skipped]: 'enum.checkStatus.skipped',
};

export const CHECK_STATUS_SLUG: Record<CheckStatus, string> = {
  [CheckStatus.Passed]: 'passed',
  [CheckStatus.Failed]: 'failed',
  [CheckStatus.Skipped]: 'skipped',
};

export interface ValidationCheck {
  description: string;
  status: CheckStatus;
  detail?: string;
}

export interface ChangeValidationResult {
  expectations: ValidationCheck[];
  unexpected: string[];
  decisionPointsBefore: number;
  decisionPointsAfter: number;
  build: ValidationCheck;
  tests: ValidationCheck;
  verdict: ValidationVerdict;
  score: number;
}

export interface ValidationView {
  id: string;
  surveyId: string;
  newSurveyId?: string;
  status: ValidationStatus;
  error?: string;
  failure?: ValidationFailure | null;
  failureArgs?: string[];
  result?: ChangeValidationResult;
  createdAt: string;
  completedAt?: string;
  approvedAt?: string;
  hasDocument: boolean;
}

export function isValidationFinished(status: ValidationStatus): boolean {
  return status === ValidationStatus.Completed || status === ValidationStatus.Failed;
}

// Qualidade do fluxo (Fase 10): métricas por método e achados (métrica, código limpo, DDD).
export interface MethodMetrics {
  cyclomatic: number;
  lines: number;
  maxNesting: number;
  dependencies: number;
}

export enum FindingKind {
  HighComplexity = 0,
  LongMethod = 1,
  DeepNesting = 2,
  TooManyDependencies = 3,
  SilentCatch = 4,
  ServiceLocator = 5,
  EntityChangedOutsideDomain = 6,
  PublicSetterOnEntity = 7,
  PersistenceOutsideDomain = 8,
  // Banco no fluxo (Fase 39).
  QueryInLoop = 9,
  MaterializeBeforeFilter = 10,
  ReadWithoutNoTracking = 11,
  ProcedureByRawSql = 12,
  // Banco nos fluxos, repositório genérico (Fase 43b).
  LoadToDelete = 13,
  LeadingWildcardLike = 14,
}

export enum FindingCategory {
  Metric = 0,
  CleanCode = 1,
  Ddd = 2,
  Database = 3,
}

export enum FindingSeverity {
  Info = 0,
  Warning = 1,
  Critical = 2,
}

export interface Finding {
  kind: FindingKind;
  severity: FindingSeverity;
  nodeId: string;
  message: string;
  location?: SourceLocation;
  // Nos achados de banco (Fase 39): a tabela ou a procedure.
  target?: string;
}

export const FINDING_CATEGORY_OF: Record<FindingKind, FindingCategory> = {
  [FindingKind.HighComplexity]: FindingCategory.Metric,
  [FindingKind.LongMethod]: FindingCategory.Metric,
  [FindingKind.DeepNesting]: FindingCategory.Metric,
  [FindingKind.TooManyDependencies]: FindingCategory.Metric,
  [FindingKind.SilentCatch]: FindingCategory.CleanCode,
  [FindingKind.ServiceLocator]: FindingCategory.CleanCode,
  [FindingKind.EntityChangedOutsideDomain]: FindingCategory.Ddd,
  [FindingKind.PublicSetterOnEntity]: FindingCategory.Ddd,
  [FindingKind.PersistenceOutsideDomain]: FindingCategory.Ddd,
  [FindingKind.QueryInLoop]: FindingCategory.Database,
  [FindingKind.MaterializeBeforeFilter]: FindingCategory.Database,
  [FindingKind.ReadWithoutNoTracking]: FindingCategory.Database,
  [FindingKind.ProcedureByRawSql]: FindingCategory.Database,
  [FindingKind.LoadToDelete]: FindingCategory.Database,
  [FindingKind.LeadingWildcardLike]: FindingCategory.Database,
};

export const FINDING_CATEGORY_LABEL_KEY: Record<FindingCategory, string> = {
  [FindingCategory.Ddd]: 'enum.findingCategory.ddd',
  [FindingCategory.CleanCode]: 'enum.findingCategory.cleanCode',
  [FindingCategory.Metric]: 'enum.findingCategory.metric',
  [FindingCategory.Database]: 'enum.findingCategory.database',
};

export const FINDING_KIND_LABEL_KEY: Record<FindingKind, string> = {
  [FindingKind.HighComplexity]: 'enum.findingKind.highComplexity',
  [FindingKind.LongMethod]: 'enum.findingKind.longMethod',
  [FindingKind.DeepNesting]: 'enum.findingKind.deepNesting',
  [FindingKind.TooManyDependencies]: 'enum.findingKind.tooManyDependencies',
  [FindingKind.SilentCatch]: 'enum.findingKind.silentCatch',
  [FindingKind.ServiceLocator]: 'enum.findingKind.serviceLocator',
  [FindingKind.EntityChangedOutsideDomain]: 'enum.findingKind.entityChangedOutsideDomain',
  [FindingKind.PublicSetterOnEntity]: 'enum.findingKind.publicSetterOnEntity',
  [FindingKind.PersistenceOutsideDomain]: 'enum.findingKind.persistenceOutsideDomain',
  [FindingKind.QueryInLoop]: 'enum.findingKind.queryInLoop',
  [FindingKind.MaterializeBeforeFilter]: 'enum.findingKind.materializeBeforeFilter',
  [FindingKind.ReadWithoutNoTracking]: 'enum.findingKind.readWithoutNoTracking',
  [FindingKind.ProcedureByRawSql]: 'enum.findingKind.procedureByRawSql',
  [FindingKind.LoadToDelete]: 'enum.findingKind.loadToDelete',
  [FindingKind.LeadingWildcardLike]: 'enum.findingKind.leadingWildcardLike',
};

export const FINDING_SEVERITY_LABEL_KEY: Record<FindingSeverity, string> = {
  [FindingSeverity.Info]: 'enum.findingSeverity.info',
  [FindingSeverity.Warning]: 'enum.findingSeverity.warning',
  [FindingSeverity.Critical]: 'enum.findingSeverity.critical',
};

export const FINDING_SEVERITY_SLUG: Record<FindingSeverity, string> = {
  [FindingSeverity.Info]: 'info',
  [FindingSeverity.Warning]: 'warning',
  [FindingSeverity.Critical]: 'critical',
};

export interface RefactorPrompt {
  prompt: string;
}

// Diagramas C4 (Fase 11): o DSL do Structurizr e as pessoas do C1 informadas pelo usuário.
export interface C4Person {
  name: string;
  description?: string;
  // Veio da IA e ninguém editou depois: o C1 marca como inferida.
  fromAi?: boolean;
}

export interface C4Export {
  dsl: string;
  people: C4Person[];
}

// Anotação do usuário num passo (Fase 12): fica por repositório e passo.
export interface NodeNote {
  nodeId: string;
  text: string;
  updatedAt: string;
}

export const NOTE_MAX_LENGTH = 500;

// Visão consolidada (Fase 27): o Mermaid salvo do levantamento, com a edição do dono.
export interface SurveyDiagram {
  mermaid: string;
  commitHash?: string;
  updatedAt: string;
  // Preenchido quando o diagrama é herdado de um levantamento anterior (o próprio ainda não salvou).
  fromSurveyId?: string;
}

// Sugestão de refactor da IA para um achado (Fase 13): texto para ler, marcado como da IA.
export interface RefactorSuggestion {
  summary: string;
  steps: string[];
  provider: AiProvider;
}

// Regra cruzada entre fluxos (Fase 13): este fluxo grava a entidade sem a guarda que outros usam.
export interface CrossFlowFinding {
  nodeId: string;
  label: string;
  entity: string;
  guard: string;
  flows: string[];
}

// Pessoas do C1 sugeridas pela IA (Fase 13): só entram quando o usuário acrescenta e salva.
export interface PeopleSuggestion {
  people: C4Person[];
  provider: AiProvider;
}

// Links do código (Fase 56): onde o repositório mora, lido do remote do git.
export enum RepositoryHostKind {
  Unknown = 0,
  AzureDevOps = 1,
  GitHub = 2,
  GitLab = 3,
  Bitbucket = 4,
}

// Modelos de link de um repositório do levantamento, com {path} e {line} a preencher (link-template.ts).
export interface RepositoryLinks {
  repository: string;
  host: RepositoryHostKind;
  commitTemplate: string | null;
  branchTemplate: string | null;
  editorTemplate: string | null;
}

// Visão de negócio (Fases 55 a 57): os passos-chave do fluxo, com texto de negócio e referência ao código.
export enum BusinessKind {
  Start = 0,
  Decision = 1,
  Notification = 2,
  Rejection = 3,
  CrossSystem = 4,
  Step = 5,
  End = 6,
}

export enum LabelOrigin {
  Rule = 0,
  Ai = 1,
  Agent = 2,
  User = 3,
}

export enum LabelState {
  Proposed = 0,
  Confirmed = 1,
}

export interface CodeRef {
  repository: string;
  file: string;
  line: number;
  commitHash: string | null;
  branch: string | null;
}

export interface BusinessStep {
  id: string;
  nodeId: string;
  kind: BusinessKind;
  label: string;
  labelOrigin: LabelOrigin;
  labelState: LabelState;
  ref: CodeRef | null;
  // Nós do grafo que este passo-chave resume.
  sourceNodeIds: string[];
  inferred: boolean;
  // Fase 59: o código do passo mudou depois de o texto ser confirmado.
  codeChanged: boolean;
  // Proposta de tipo do agente, à espera do usuário (some quando não há proposta).
  kindProposal?: BusinessKindProposal;
}

export interface BusinessKindProposal {
  kind: BusinessKind;
  reason: string;
}

export interface BusinessLabelSuggestion {
  proposed: number;
  skipped: number;
}

export interface BusinessEdge {
  from: string;
  to: string;
  label: string | null;
  inferred: boolean;
}

export interface BusinessFlow {
  surveyId: string;
  schemaVersion: number;
  steps: BusinessStep[];
  edges: BusinessEdge[];
  collapsedDecisions: number;
}

// Posição salva dos balões da visão de negócio (Fase 58): a posição de cada passo-chave e os pontos de dobra
// de cada ligação. Passo ou ligação que não está aqui fica na organização automática.
export interface StepPosition {
  stepId: string;
  x: number;
  y: number;
}

export interface EdgePoints {
  from: string;
  to: string;
  points: { x: number; y: number }[];
}

export interface BusinessLayout {
  steps: StepPosition[];
  edges: EdgePoints[];
}

// Quadro de fluxos (Fase 60): vários levantamentos lado a lado, cada um numa faixa. A posição é por quadro, e o
// id do passo é por levantamento: cada item leva o levantamento da faixa.
export const BOARD_MAX_LANES = 12;
export const BOARD_NAME_MAX = 120;
export const BOARD_TITLE_MAX = 120;
export const BOARD_GLOSSARY_MAX = 8000;

export interface BoardSummary {
  id: string;
  name: string;
  laneCount: number;
  updatedAt: string;
}

export interface BoardLane {
  surveyId: string;
  order: number;
  title: string;
  entryPoint: string;
  repositories: string[];
  status: SurveyStatus;
  stale: boolean;
  flow: BusinessFlow | null;
}

export interface BoardStepPosition extends StepPosition {
  laneSurveyId: string;
}

export interface BoardEdgePoints extends EdgePoints {
  laneSurveyId: string;
}

export interface BoardLayout {
  steps: BoardStepPosition[];
  edges: BoardEdgePoints[];
}

export interface BoardDetail {
  id: string;
  name: string;
  glossary: string | null;
  lanes: BoardLane[];
  layout: BoardLayout;
}

export interface BoardCreateRequest {
  name: string;
  surveyIds: string[];
  titles?: string[];
}

export interface BoardLaneUpdate {
  surveyId: string;
  order: number;
  title: string;
}

export interface BoardUpdateRequest {
  name: string;
  glossary: string | null;
  lanes: BoardLaneUpdate[];
}

export interface RemovedStep {
  stepId: string;
  nodeId: string;
  label: string;
}

export interface ReplaceLaneResult {
  board: BoardDetail;
  removedSteps: RemovedStep[];
}

// Mapa do fluxo (Fase 30): faixas por sistema, blocos de leitura, ligações e caminhos de ponta a ponta.
export enum MapLaneKind {
  Code = 0,
  Outside = 1,
}

export enum MapBlockKind {
  Entry = 0,
  Step = 1,
  Decision = 2,
  Outside = 3,
}

export enum MapLinkKind {
  Sequence = 0,
  Outcome = 1,
  Call = 2,
}

// "system" é nulo na faixa do primeiro trecho (o repositório do levantamento) e na de fora do código.
export interface MapLane {
  id: string;
  kind: MapLaneKind;
  system: string | null;
}

export interface MapOutcome {
  label: string;
  // Primeiro bloco do ramo; nulo quando o ramo não tem bloco próprio.
  to: string | null;
  ends: boolean;
  fails: boolean;
}

export interface MapBlock {
  id: string;
  kind: MapBlockKind;
  lane: string;
  order: number;
  title: string;
  // Nó do grafo que dá nome ao bloco e todos os nós que ele recolhe.
  nodeId: string;
  nodeIds: string[];
  symbol?: string | null;
  location?: SourceLocation | null;
  condition?: string | null;
  outcomes?: MapOutcome[] | null;
  // Bloco de entrada do trecho (nulo na faixa de fora) e saltos entre sistemas até ele (Fase 35).
  segment?: string | null;
  depth?: number;
}

export interface MapLink {
  from: string;
  to: string;
  kind: MapLinkKind;
  label?: string | null;
  // A API manda a rota nula quando a chamada não a resolveu.
  http?: { method: string; route?: string | null; fields?: string[] | null; moreFields?: boolean } | null;
}

export interface MapChoice {
  decision: string;
  outcome: number;
}

export interface MapPath {
  id: string;
  blocks: string[];
  choices: MapChoice[];
  fails: boolean;
}

export interface FlowMap {
  lanes: MapLane[];
  blocks: MapBlock[];
  links: MapLink[];
  paths: MapPath[];
  pathsTruncated: boolean;
}

// Regras do fluxo (Fase 32): cada decisão promovida vira uma regra com o efeito, o texto e a fonte.
export enum RuleEffect {
  Blocks = 0,
  Allows = 1,
  SideEffect = 2,
}

export interface RuleSource {
  nodeId: string;
  blockId: string;
  lane: string;
  location?: SourceLocation | null;
}

export interface FlowRule {
  id: string;
  effect: RuleEffect;
  text: string;
  condition: string | null;
  // Escrita pelo usuário (NodeRule) e não tirada da condição.
  fromUser: boolean;
  // A mesma condição vale em mais de um sistema.
  duplicated: boolean;
  sources: RuleSource[];
}

// Perguntas sobre o fluxo (Fase 33): prontas por papel, e a livre quando há IA.
export enum QuestionRole {
  TechLead = 0,
  Staff = 1,
  Manager = 2,
}

export enum AnswerSeal {
  Extracted = 0,
  Inferred = 1,
  Outside = 2,
  None = 3,
}

export interface FlowAnswer {
  text: string;
  seal: AnswerSeal;
  // Ids de nós do grafo que sustentam a resposta.
  sources: string[];
  gap: string | null;
}

export interface FlowQuestion {
  id: string;
  role: QuestionRole;
  text: string;
  answer: FlowAnswer;
}

export interface QuestionAnswer {
  answer: FlowAnswer;
  provider: AiProvider;
}
