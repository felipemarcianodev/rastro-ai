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

export const CHANGE_KIND_LABEL: Record<ChangeKind, string> = {
  [ChangeKind.Added]: 'Passo novo',
  [ChangeKind.Removed]: 'Sai do fluxo',
  [ChangeKind.Modified]: 'Muda',
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
}

export interface FlowGraph {
  schemaVersion: number;
  entryPoint: string;
  entryNodeId: string;
  nodes: FlowNode[];
  edges: FlowEdge[];
  findings?: Finding[];
}

export interface SurveySummary {
  id: string;
  repository: string;
  solutionPath: string;
  entryPoint: string;
  maxDepth: number;
  commitHash?: string;
  branch?: string;
  status: SurveyStatus;
  error?: string;
  schemaVersion: number;
  nodeCount: number;
  edgeCount: number;
  unresolvedCount: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface SurveyDetail {
  survey: SurveySummary;
  graph?: FlowGraph;
}

export interface SurveyStaleness {
  stale: boolean;
  surveyCommit?: string;
  currentCommit?: string;
}

export interface CreateSurveyRequest {
  repository: string;
  solutionPath: string;
  entryPoint: string;
  maxDepth?: number;
}

export interface RepositoryItem {
  name: string;
  // Pasta absoluta cadastrada (a API é local; vale também para o "Abrir no VS Code").
  path: string;
  hasGit: boolean;
  hasBuildCommand: boolean;
  hasTestCommand: boolean;
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

export const NODE_KIND_LABEL: Record<NodeKind, string> = {
  [NodeKind.Entry]: 'Entrada',
  [NodeKind.Method]: 'Método',
  [NodeKind.Decision]: 'Decisão',
  [NodeKind.Loop]: 'Laço',
  [NodeKind.Try]: 'Tratamento de erro',
  [NodeKind.Return]: 'Retorno antecipado',
  [NodeKind.Throw]: 'Exceção',
  [NodeKind.External]: 'Chamada externa',
  [NodeKind.Unresolved]: 'Não resolvido',
  [NodeKind.Jump]: 'Desvio do laço',
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

export const EDGE_CONFIDENCE_LABEL: Record<EdgeConfidence, string> = {
  [EdgeConfidence.Extracted]: 'Extraída do código',
  [EdgeConfidence.Inferred]: 'Inferida (a conferir)',
  [EdgeConfidence.Confirmed]: 'Confirmada por você',
};

// Selo curto da origem de uma regra ou ligação (painel e linha do tempo).
export const PROPOSAL_ORIGIN_BADGE: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'Você',
  [ProposalOrigin.Agent]: 'Agente',
  [ProposalOrigin.Provider]: 'IA',
};

export const PROPOSAL_ORIGIN_SLUG: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'manual',
  [ProposalOrigin.Agent]: 'agent',
  [ProposalOrigin.Provider]: 'provider',
};

export const PROPOSAL_ORIGIN_LABEL: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'você',
  [ProposalOrigin.Agent]: 'um agente (MCP)',
  [ProposalOrigin.Provider]: 'o provedor de IA',
};

export const UNRESOLVED_REASON_LABEL: Record<UnresolvedReason, string> = {
  [UnresolvedReason.MultipleImplementations]: 'Mais de uma implementação',
  [UnresolvedReason.NoImplementation]: 'Sem implementação no código',
  [UnresolvedReason.DynamicDispatch]: 'Chamada dinâmica',
  [UnresolvedReason.Delegate]: 'Delegate ou evento',
  [UnresolvedReason.MessageDispatch]: 'Mensagem para mediator ou barramento',
  [UnresolvedReason.Reflection]: 'Reflection',
  [UnresolvedReason.DepthLimit]: 'Limite de profundidade',
};

// O que o motivo significa, em uma frase, para quem vai completar o buraco.
export const UNRESOLVED_REASON_HELP: Record<UnresolvedReason, string> = {
  [UnresolvedReason.MultipleImplementations]: 'A interface tem várias implementações e nenhum registro de DI decide qual roda aqui.',
  [UnresolvedReason.NoImplementation]: 'A interface não tem implementação nesta solution. Ela pode vir de um pacote ou de outro repositório.',
  [UnresolvedReason.DynamicDispatch]: 'O compilador não ligou a chamada a um método (dynamic ou código que não compilou).',
  [UnresolvedReason.Delegate]: 'Quem roda depende de quem atribuiu o delegate ou assinou o evento.',
  [UnresolvedReason.MessageDispatch]: 'O handler da mensagem é escolhido em tempo de execução pelo mediator ou barramento.',
  [UnresolvedReason.Reflection]: 'O método é chamado por reflection, então o destino só existe em tempo de execução.',
  [UnresolvedReason.DepthLimit]: 'O caminho continua, mas parou no limite de profundidade escolhido.',
};

export const SURVEY_STATUS_SLUG: Record<SurveyStatus, string> = {
  [SurveyStatus.Pending]: 'pending',
  [SurveyStatus.Running]: 'running',
  [SurveyStatus.Completed]: 'completed',
  [SurveyStatus.Failed]: 'failed',
};

export const SURVEY_STATUS_LABEL: Record<SurveyStatus, string> = {
  [SurveyStatus.Pending]: 'Na fila',
  [SurveyStatus.Running]: 'Analisando',
  [SurveyStatus.Completed]: 'Pronto',
  [SurveyStatus.Failed]: 'Falhou',
};

export const API_ERROR_MESSAGE: Record<string, string> = {
  REPOSITORY_NOT_ALLOWED: 'Esse repositório não está cadastrado. Cadastre a pasta em Configurações.',
  SOLUTION_PATH_INVALID: 'Use um .sln, .slnx ou .csproj relativo à raiz do repositório.',
  SOLUTION_NOT_FOUND: 'Não há solution nesse caminho.',
  ENTRY_POINT_REQUIRED: 'Informe o ponto de entrada, por exemplo PedidosController.Criar.',
  DEPTH_OUT_OF_RANGE: 'A profundidade precisa ficar entre 1 e 40.',
  SOURCE_PATH_INVALID: 'Esse arquivo não pode ser lido.',
  EDGE_NOT_PROPOSED: 'Essa aresta não é uma proposta (ou já foi decidida).',
  DRAFT_INVALID: 'O rascunho não passou na validação.',
  DRAFT_REQUIRED: 'Desenhe e salve ao menos uma mudança antes de validar.',
  VALIDATION_RUNNING: 'Já existe uma validação em andamento para este levantamento.',
  VALIDATION_NOT_APPROVABLE: 'Só uma validação concluída e não reprovada pode ser aprovada.',
  C4_PEOPLE_INVALID: 'Confira as pessoas: cada uma precisa de nome, sem repetir.',
  NOTE_INVALID: 'A anotação precisa ser de um passo deste fluxo e ter até 500 caracteres.',
  DIAGRAM_INVALID: 'O diagrama está vazio ou grande demais.',
  SURVEY_RUNNING: 'O levantamento ainda está em análise. Espere terminar para excluir.',
  AI_SETTINGS_INVALID: 'Configuração inválida: confira modelos, chaves e orçamento.',
  AI_DISABLED: 'A IA está desligada na configuração.',
  AI_USE_DISABLED: 'Este uso da IA está desligado na configuração.',
  AI_NOT_CONFIGURED: 'Falta a chave de API do provedor escolhido.',
  AI_BUDGET_EXCEEDED: 'O orçamento diário de tokens acabou.',
  REPOSITORY_NAME_INVALID: 'Nome inválido: use letras, números, ponto, hífen ou sublinhado, começando por letra ou número.',
  REPOSITORY_PATH_INVALID: 'Use o caminho completo da pasta, como C:\\codigo\\meu-sistema.',
  REPOSITORY_FOLDER_NOT_FOUND: 'Essa pasta não existe neste computador.',
  REPOSITORY_EXISTS: 'Já existe um repositório com esse nome.',
  REPOSITORY_NOT_FOUND: 'Esse repositório não está mais cadastrado.',
  ORIGIN_NOT_ALLOWED: 'Pedido recusado: só a tela do próprio Rastro pode alterar dados.',
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

export interface FillHoleResult {
  proposed: number;
  accepted: number;
  messages: string[];
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

export const VALIDATION_VERDICT_LABEL: Record<ValidationVerdict, string> = {
  [ValidationVerdict.Approved]: 'Aprovada',
  [ValidationVerdict.Partial]: 'Parcial',
  [ValidationVerdict.Rejected]: 'Reprovada',
};

export const VALIDATION_VERDICT_SLUG: Record<ValidationVerdict, string> = {
  [ValidationVerdict.Approved]: 'approved',
  [ValidationVerdict.Partial]: 'partial',
  [ValidationVerdict.Rejected]: 'rejected',
};

export const CHECK_STATUS_LABEL: Record<CheckStatus, string> = {
  [CheckStatus.Passed]: 'passou',
  [CheckStatus.Failed]: 'falhou',
  [CheckStatus.Skipped]: 'não configurado',
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
}

export enum FindingCategory {
  Metric = 0,
  CleanCode = 1,
  Ddd = 2,
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
};

export const FINDING_CATEGORY_LABEL: Record<FindingCategory, string> = {
  [FindingCategory.Ddd]: 'Regras de DDD',
  [FindingCategory.CleanCode]: 'Código limpo',
  [FindingCategory.Metric]: 'Métricas',
};

export const FINDING_KIND_LABEL: Record<FindingKind, string> = {
  [FindingKind.HighComplexity]: 'Complexidade alta',
  [FindingKind.LongMethod]: 'Método longo',
  [FindingKind.DeepNesting]: 'Aninhamento fundo',
  [FindingKind.TooManyDependencies]: 'Dependências demais',
  [FindingKind.SilentCatch]: 'Catch que engole a falha',
  [FindingKind.ServiceLocator]: 'Service locator',
  [FindingKind.EntityChangedOutsideDomain]: 'Entidade alterada fora do domínio',
  [FindingKind.PublicSetterOnEntity]: 'Setter público na entidade',
  [FindingKind.PersistenceOutsideDomain]: 'Gravação fora do domínio',
};

export const FINDING_SEVERITY_LABEL: Record<FindingSeverity, string> = {
  [FindingSeverity.Info]: 'Informativo',
  [FindingSeverity.Warning]: 'Atenção',
  [FindingSeverity.Critical]: 'Crítico',
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
