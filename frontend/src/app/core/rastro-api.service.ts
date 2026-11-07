import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, throwError } from 'rxjs';
import {
  API_ERROR_MESSAGE,
  AiSettingsUpdate,
  AiSettingsView,
  AiStatus,
  DescribeResponse,
  FillHoleResponse,
  ChangePrompt,
  CreateSurveyRequest,
  FlowGraph,
  FlowMap,
  FlowQuestion,
  FlowRule,
  QuestionAnswer,
  EdgeDecision,
  RepositoryItem,
  RastroHealth,
  SourceSnippet,
  SurveyDetail,
  SurveyStaleness,
  ValidationView,
  RefactorPrompt,
  C4Export,
  NodeNote,
  SurveyDiagram,
  PeopleSuggestion,
  CrossFlowFinding,
  RefactorSuggestion,
  C4Person,
  SurveySummary,
} from './models';
import { DbSchema, DbUsageReport } from './database/db-schema';
import { QueryAnalysis } from './database/query-analysis';
import { QueryAiSuggestion } from './database/query-suggestion';
import { LiveConnectionTest, LiveConnectionView, QueryPlan } from './database/live-database';

// Códigos cujo title traz o motivo exato em pt-BR, escrito pelo Rastro (nunca a mensagem crua do driver).
const TITLE_CODES: ReadonlySet<string> = new Set(['PLAN_NOT_ALLOWED', 'LIVE_DATABASE_ERROR', 'AI_PROVIDER_ERROR']);

@Injectable({ providedIn: 'root' })
export class RastroApi {
  private readonly http = inject(HttpClient);

  repositories(): Observable<RepositoryItem[]> {
    return this.http.get<RepositoryItem[]>('/api/repositories');
  }

  addRepository(name: string, path: string): Observable<RepositoryItem> {
    return this.http.post<RepositoryItem>('/api/repositories', { name, path });
  }

  removeRepository(name: string): Observable<void> {
    return this.http.delete<void>(`/api/repositories/${encodeURIComponent(name)}`);
  }

  setRepositoryCommands(name: string, buildCommand: string | null, testCommand: string | null): Observable<RepositoryItem> {
    return this.http.put<RepositoryItem>(`/api/repositories/${encodeURIComponent(name)}/commands`, { buildCommand, testCommand });
  }

  solutions(repository: string): Observable<string[]> {
    return this.http.get<string[]>(`/api/repositories/${encodeURIComponent(repository)}/solutions`);
  }

  // Esquema do banco lido do código do repositório (Fase 37); nenhum banco é consultado.
  schema(repository: string): Observable<DbSchema> {
    return this.http.get<DbSchema>(`/api/repositories/${encodeURIComponent(repository)}/schema`);
  }

  // Passos dos levantamentos do repositório que leem ou gravam cada tabela (Fase 39).
  schemaUsage(repository: string): Observable<DbUsageReport> {
    return this.http.get<DbUsageReport>(`/api/repositories/${encodeURIComponent(repository)}/schema/usage`);
  }

  // Análise de uma consulta (T-SQL ou PostgreSQL, o dialeto do repositório) contra o esquema do código, por regras fixas (Fase 40a).
  analyzeQuery(repository: string, sql: string): Observable<QueryAnalysis> {
    return this.http.post<QueryAnalysis>(`/api/repositories/${encodeURIComponent(repository)}/schema/analyze`, { sql });
  }

  // A IA (modo provedor) sugere a melhora da consulta; o Rastro confere e a tela só mostra (Fase 41).
  suggestQuery(repository: string, sql: string): Observable<QueryAiSuggestion> {
    return this.http.post<QueryAiSuggestion>(`/api/repositories/${encodeURIComponent(repository)}/schema/ai/suggestion`, { sql });
  }

  // Banco vivo (Fase 40b): a conexão só leitura do repositório. A string nunca volta na resposta.
  connection(repository: string): Observable<LiveConnectionView> {
    return this.http.get<LiveConnectionView>(this.connectionUrl(repository));
  }

  saveConnection(repository: string, connectionString: string): Observable<LiveConnectionView> {
    return this.http.put<LiveConnectionView>(this.connectionUrl(repository), { connectionString });
  }

  removeConnection(repository: string): Observable<void> {
    return this.http.delete<void>(this.connectionUrl(repository));
  }

  testConnection(repository: string): Observable<LiveConnectionTest> {
    return this.http.post<LiveConnectionTest>(`${this.connectionUrl(repository)}/test`, {});
  }

  // Plano estimado pelo banco (SET SHOWPLAN_XML): a consulta não é executada.
  queryPlan(repository: string, sql: string): Observable<QueryPlan> {
    return this.http.post<QueryPlan>(`/api/repositories/${encodeURIComponent(repository)}/schema/plan`, { sql });
  }

  private connectionUrl(repository: string): string {
    return `/api/repositories/${encodeURIComponent(repository)}/connection`;
  }

  health(): Observable<RastroHealth> {
    return this.http.get<RastroHealth>('/api/health');
  }

  surveys(): Observable<SurveySummary[]> {
    return this.http.get<SurveySummary[]>('/api/surveys');
  }

  survey(id: string): Observable<SurveyDetail> {
    return this.http.get<SurveyDetail>(`/api/surveys/${id}`);
  }

  createSurvey(request: CreateSurveyRequest): Observable<SurveySummary> {
    return this.http.post<SurveySummary>('/api/surveys', request);
  }

  decideEdge(id: string, decision: EdgeDecision): Observable<SurveyDetail> {
    return this.http.post<SurveyDetail>(`/api/surveys/${id}/edges/decision`, decision);
  }

  aiSettings(): Observable<AiSettingsView> {
    return this.http.get<AiSettingsView>('/api/settings/ai');
  }

  saveAiSettings(update: AiSettingsUpdate): Observable<AiSettingsView> {
    return this.http.put<AiSettingsView>('/api/settings/ai', update);
  }

  aiStatus(): Observable<AiStatus> {
    return this.http.get<AiStatus>('/api/ai/status');
  }

  fillHole(id: string, nodeId: string): Observable<FillHoleResponse> {
    return this.http.post<FillHoleResponse>(`/api/surveys/${id}/ai/fill-hole`, { nodeId });
  }

  describeRules(id: string, nodeIds: string[] | null, overwrite: boolean): Observable<DescribeResponse> {
    return this.http.post<DescribeResponse>(`/api/surveys/${id}/ai/describe`, { nodeIds, overwrite });
  }

  // Rascunho de edição; null quando ainda não existe.
  draft(id: string): Observable<FlowGraph | null> {
    return this.http.get<FlowGraph>(`/api/surveys/${id}/draft`).pipe(
      catchError((error) => (error instanceof HttpErrorResponse && error.status === 404 ? of(null) : throwError(() => error))),
    );
  }

  saveDraft(id: string, draft: FlowGraph): Observable<FlowGraph> {
    return this.http.put<FlowGraph>(`/api/surveys/${id}/draft`, draft);
  }

  changePrompt(id: string): Observable<ChangePrompt> {
    return this.http.get<ChangePrompt>(`/api/surveys/${id}/prompt`);
  }

  refactorPrompt(id: string, index: number): Observable<RefactorPrompt> {
    return this.http.get<RefactorPrompt>(`/api/surveys/${id}/findings/${index}/prompt`);
  }

  c4(id: string): Observable<C4Export> {
    return this.http.get<C4Export>(`/api/surveys/${id}/c4`);
  }

  suggestRefactor(id: string, index: number): Observable<RefactorSuggestion> {
    return this.http.post<RefactorSuggestion>(`/api/surveys/${id}/findings/${index}/ai/suggestion`, null);
  }

  crossFlow(id: string): Observable<CrossFlowFinding[]> {
    return this.http.get<CrossFlowFinding[]>(`/api/surveys/${id}/cross-flow`);
  }

  suggestPeople(id: string): Observable<PeopleSuggestion> {
    return this.http.post<PeopleSuggestion>(`/api/surveys/${id}/c4/ai/people`, null);
  }

  deleteSurvey(id: string): Observable<void> {
    return this.http.delete<void>(`/api/surveys/${id}`);
  }

  notes(id: string): Observable<NodeNote[]> {
    return this.http.get<NodeNote[]>(`/api/surveys/${id}/notes`);
  }

  // Texto vazio apaga (a API responde 204 e aqui volta null).
  saveNote(id: string, nodeId: string, text: string): Observable<NodeNote | null> {
    return this.http.put<NodeNote | null>(`/api/surveys/${id}/notes`, { nodeId, text });
  }

  // Visão consolidada (Fase 27); null quando ainda não há diagrama salvo nem anterior para herdar.
  diagram(id: string): Observable<SurveyDiagram | null> {
    return this.http.get<SurveyDiagram>(`/api/surveys/${id}/diagram`).pipe(
      catchError((error) => (error instanceof HttpErrorResponse && error.status === 404 ? of(null) : throwError(() => error))),
    );
  }

  saveDiagram(id: string, mermaid: string): Observable<SurveyDiagram> {
    return this.http.put<SurveyDiagram>(`/api/surveys/${id}/diagram`, { mermaid });
  }

  deleteDiagram(id: string): Observable<void> {
    return this.http.delete<void>(`/api/surveys/${id}/diagram`);
  }

  // Mapa do fluxo (Fase 30): faixas por sistema, blocos, ligações e caminhos.
  map(id: string): Observable<FlowMap> {
    return this.http.get<FlowMap>(`/api/surveys/${id}/map`);
  }

  // Regras do fluxo (Fase 32) e perguntas prontas por papel (Fase 33).
  rules(id: string): Observable<FlowRule[]> {
    return this.http.get<FlowRule[]>(`/api/surveys/${id}/rules`);
  }

  questions(id: string): Observable<FlowQuestion[]> {
    return this.http.get<FlowQuestion[]>(`/api/surveys/${id}/questions`);
  }

  // Pergunta livre (IA, uso Sugestões): a resposta vem com selo e fontes já conferidas.
  ask(id: string, question: string): Observable<QuestionAnswer> {
    return this.http.post<QuestionAnswer>(`/api/surveys/${id}/ai/ask`, { question });
  }

  saveC4People(id: string, people: C4Person[]): Observable<C4Export> {
    return this.http.put<C4Export>(`/api/surveys/${id}/c4/people`, people);
  }

  // Validação da mudança (Fase 9).
  requestValidation(id: string): Observable<ValidationView> {
    return this.http.post<ValidationView>(`/api/surveys/${id}/validations`, null);
  }

  validations(id: string): Observable<ValidationView[]> {
    return this.http.get<ValidationView[]>(`/api/surveys/${id}/validations`);
  }

  validation(validationId: string): Observable<ValidationView> {
    return this.http.get<ValidationView>(`/api/validations/${validationId}`);
  }

  approveValidation(validationId: string): Observable<ValidationView> {
    return this.http.post<ValidationView>(`/api/validations/${validationId}/approve`, null);
  }

  validationDocument(validationId: string): Observable<string> {
    return this.http.get(`/api/validations/${validationId}/document`, { responseType: 'text' });
  }

  deleteDraft(id: string): Observable<void> {
    return this.http.delete<void>(`/api/surveys/${id}/draft`);
  }

  staleness(id: string): Observable<SurveyStaleness> {
    return this.http.get<SurveyStaleness>(`/api/surveys/${id}/stale`);
  }

  // "system": repositório do trecho quando o passo é de outro sistema do fluxo (Fase 25).
  source(id: string, file: string, line: number, context = 8, system?: string): Observable<SourceSnippet> {
    let params = new HttpParams().set('file', file).set('line', line).set('context', context);
    if (system) params = params.set('system', system);
    return this.http.get<SourceSnippet>(`/api/surveys/${id}/source`, { params });
  }
}

// Mensagem para a tela a partir do erro da API (pelo código estável, nunca pelo texto).
export function apiErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const details = error.error?.errors as string[] | undefined;
    if (Array.isArray(details) && details.length > 0) return details.join(' ');
    const code = error.error?.code as string | undefined;
    // O plano recusado e a falha do banco vivo trazem o motivo exato em pt-BR no title do problema.
    const title = error.error?.title as string | undefined;
    if (code && TITLE_CODES.has(code) && typeof title === 'string' && title.trim()) return title;
    if (code && API_ERROR_MESSAGE[code]) return API_ERROR_MESSAGE[code];
    if (error.status === 0) return 'O Rastro não respondeu. Confira se o comando rastro continua aberto no terminal e tente de novo.';
    if (error.status === 404) return 'Não encontrado.';
  }
  return 'Algo deu errado no Rastro. Tente de novo.';
}
