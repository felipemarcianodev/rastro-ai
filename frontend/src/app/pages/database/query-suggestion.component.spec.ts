import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AiStatusService } from '../../core/ai-status.service';
import { SqlDialect } from '../../core/database/db-schema';
import { lojaSchema } from '../../core/database/loja-schema.spec';
import { QueryAnalysis, QueryFindingKind, QuerySeverity } from '../../core/database/query-analysis';
import { ProposalOrigin, QueryAiSuggestion } from '../../core/database/query-suggestion';
import { AiProvider, AiUse } from '../../core/models';
import { QueryAnalysisComponent } from './query-analysis.component';

const SQL = 'SELECT * FROM pedidos p WHERE YEAR(p.criado_em) = 2025';
const REWRITE = 'SELECT p.id FROM pedidos p WHERE p.criado_em >= @ini AND p.criado_em < @fim';
const URL_ANALYZE = '/api/repositories/loja/schema/analyze';
const URL_SUGGEST = '/api/repositories/loja/schema/ai/suggestion';

function analysis(findings = 4): QueryAnalysis {
  return {
    dialect: SqlDialect.SqlServer,
    statements: 1,
    findings: Array.from({ length: findings }, (_, i) => ({
      kind: i === 0 ? QueryFindingKind.SelectStar : QueryFindingKind.FunctionOnFilterColumn,
      severity: QuerySeverity.Low,
      start: 0,
      length: 6,
      line: 1,
      snippet: 'SELECT',
      message: 'Achado.',
      table: null,
    })),
    tables: [{ name: 'pedidos', alias: 'p', inSchema: true }],
    error: null,
  };
}

function suggestion(overrides: Partial<QueryAiSuggestion['review']> = {}): QueryAiSuggestion {
  return {
    provider: AiProvider.Claude,
    review: {
      origin: ProposalOrigin.Provider,
      findingsBefore: 4,
      rewrite: {
        sql: REWRITE,
        accepted: true,
        refusals: [],
        after: { ...analysis(1), findings: [{ ...analysis(1).findings[0], kind: QueryFindingKind.OrderByWithoutLimit }] },
      },
      changes: [{ what: 'Trocou YEAR(p.criado_em) por uma faixa de datas', why: 'Deixa o índice ser usado.' }],
      indexes: [],
      plan: null,
      ...overrides,
    },
  };
}

function render(uses: AiUse[] | null) {
  TestBed.configureTestingModule({
    imports: [QueryAnalysisComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  if (uses) {
    TestBed.inject(AiStatusService).status.set({ enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses, tokensToday: 0, dailyTokenBudget: 0 });
  }
  const fixture = TestBed.createComponent(QueryAnalysisComponent);
  fixture.componentRef.setInput('repository', 'loja');
  fixture.componentRef.setInput('schema', lojaSchema());
  fixture.componentRef.setInput('initial', SQL);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  const el = fixture.nativeElement as HTMLElement;
  el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
  http.expectOne(URL_ANALYZE).flush(analysis());
  fixture.detectChanges();
  return { fixture, http, el };
}

function suggestButton(el: HTMLElement) {
  return [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Sugerir melhora com a IA'));
}

function ask(ctx: ReturnType<typeof render>, result: QueryAiSuggestion) {
  suggestButton(ctx.el)!.click();
  const request = ctx.http.expectOne(URL_SUGGEST);
  expect(request.request.method).toBe('POST');
  expect(request.request.body).toEqual({ sql: SQL });
  request.flush(result);
  ctx.fixture.detectChanges();
}

function buttonWith(el: HTMLElement, text: string) {
  return [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes(text));
}

describe('Sugestão da IA para a consulta (Fase 41)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-DBA-001
  it('sem IA ligada, nem o botão nem a dica aparecem', () => {
    const none = render(null);
    expect(suggestButton(none.el)).toBeUndefined();
    expect(none.el.textContent).not.toContain('Sugestão da IA');
    expect(none.el.textContent).not.toContain('Vai para');
    TestBed.resetTestingModule();

    const otherUse = render([AiUse.FillHoles]);
    expect(suggestButton(otherUse.el)).toBeUndefined();
  });

  // WEB-DBA-002
  it('com IA ligada, mostra o botão e a dica do que vai para o provedor, e manda a consulta', () => {
    const ctx = render([AiUse.Suggestions]);
    expect(suggestButton(ctx.el)).toBeDefined();
    expect(ctx.el.querySelector('.qa-ai')!.textContent).toContain('Vai para Claude (Anthropic): a consulta, os achados e só as tabelas que ela usa (nomes, colunas, tipos e índices). Os dados das tabelas e a string de conexão nunca vão.');

    suggestButton(ctx.el)!.click();
    ctx.fixture.detectChanges();
    expect(buttonWith(ctx.el, 'Perguntando à IA…')!.disabled).toBeTrue();
    const request = ctx.http.expectOne(URL_SUGGEST);
    expect(request.request.body).toEqual({ sql: SQL });
    request.flush(suggestion({ rewrite: null, changes: [] }));
    ctx.fixture.detectChanges();
    expect(ctx.el.textContent).toContain('A IA não reescreveu a consulta.');
  });

  // WEB-DBA-003
  it('reescrita aceita mostra antes e depois, a contagem de achados e o custo estimado', () => {
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion({ plan: { costBefore: 2.1, costAfter: 0.4, problem: null } }));

    const region = ctx.el.querySelector('app-query-suggestion section')!;
    expect(region.getAttribute('aria-label')).toBe('Sugestão da IA para a consulta');
    expect(region.querySelector('.origin-badge.is-provider')!.textContent).toBe('IA');
    expect(region.querySelector('[role="status"]')!.textContent).toContain('Proposta de Claude (Anthropic), conferida pelo Rastro. Nada foi aplicado: copie o que quiser usar.');
    const pres = [...region.querySelectorAll('.qs-diff pre')].map((p) => p.textContent);
    expect(pres).toEqual([SQL, REWRITE]);
    expect(region.textContent).toContain('4 achados antes, 1 achado depois');
    expect(region.textContent).toContain('ORDER BY sem limite');
    expect(region.textContent).toContain('Custo estimado pelo banco: 2,10 antes, 0,40 depois');
    expect(region.textContent).toContain('plano estimado, sem executar');
    expect(region.textContent).toContain('Trocou YEAR(p.criado_em) por uma faixa de datas');
    expect(region.textContent).toContain('Deixa o índice ser usado.');
    expect(buttonWith(ctx.el, 'Copiar a consulta reescrita')).toBeDefined();
    expect(buttonWith(ctx.el, 'Aplicar')).toBeUndefined();
  });

  // WEB-DBA-004
  it('copiar a reescrita manda o texto para a área de transferência e troca o rótulo', async () => {
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion());
    const writeText = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText } as unknown as Clipboard);

    buttonWith(ctx.el, 'Copiar a consulta reescrita')!.click();
    await Promise.resolve();
    await Promise.resolve();
    ctx.fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith(REWRITE);
    expect(buttonWith(ctx.el, 'Copiado')).toBeDefined();
  });

  // WEB-DBA-005
  it('custo com problema mostra o motivo em vez do custo', () => {
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion({ plan: { costBefore: 2.1, costAfter: null, problem: 'O banco não devolveu o plano da reescrita.' } }));

    expect(ctx.el.textContent).toContain('O banco não devolveu o plano da reescrita.');
    expect(ctx.el.textContent).not.toContain('Custo estimado pelo banco');
  });

  // WEB-DBA-006
  it('reescrita recusada mostra os motivos e não oferece copiar', () => {
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion({ rewrite: { sql: 'DROP TABLE pedidos', accepted: false, refusals: ['Só se aceita uma consulta de leitura.', 'A coluna total não existe.'], after: null } }));

    const text = ctx.el.textContent!;
    expect(text).toContain('Reescrita recusada pela conferência');
    expect(text).toContain('Só se aceita uma consulta de leitura.');
    expect(text).toContain('A coluna total não existe.');
    expect(buttonWith(ctx.el, 'Copiar a consulta reescrita')).toBeUndefined();
    expect(ctx.el.querySelector('.qs-diff')).toBeNull();
    // As mudanças falam do texto recusado (UX-DBA-04), e o botão vira "Pedir outra sugestão" (UX-DBA-05).
    expect(text).toContain('Mudanças da reescrita recusada');
    expect(suggestButton(ctx.el)).toBeUndefined();
    expect(buttonWith(ctx.el, 'Pedir outra sugestão')).toBeDefined();
  });

  // WEB-DBA-007
  it('índice aceito mostra o comando e copia; índice recusado mostra o motivo', async () => {
    const create = 'CREATE INDEX [IX_pedidos_criado_em] ON [pedidos] ([criado_em]) INCLUDE ([total]);';
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion({
      indexes: [
        { table: 'pedidos', columns: ['criado_em'], include: ['total'], unique: false, reason: 'Filtro por data.', accepted: true, refusal: null, createSql: create },
        { table: 'clientes', columns: ['nome'], include: [], unique: false, reason: 'Busca por nome.', accepted: false, refusal: 'A tabela clientes não tem a coluna nome.', createSql: null },
      ],
    }));

    const items = [...ctx.el.querySelectorAll('.qs-index')];
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Aceito');
    expect(items[0].querySelector('pre')!.textContent).toBe(create);
    expect(items[0].textContent).toContain('O Rastro não cria o índice: avalie e crie pela migration.');
    expect(items[1].textContent).toContain('Recusado');
    expect(items[1].textContent).toContain('Recusado: A tabela clientes não tem a coluna nome.');
    expect(items[1].querySelector('button')).toBeNull();

    const writeText = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText } as unknown as Clipboard);
    items[0].querySelector<HTMLButtonElement>('button')!.click();
    await Promise.resolve();
    await Promise.resolve();
    ctx.fixture.detectChanges();
    expect(writeText).toHaveBeenCalledWith(create);
    expect(items[0].querySelector('button')!.textContent).toContain('Copiado');
  });

  // WEB-DBA-008
  it('editar a consulta descarta a sugestão', () => {
    const ctx = render([AiUse.Suggestions]);
    ask(ctx, suggestion());
    expect(ctx.el.querySelector('app-query-suggestion')).not.toBeNull();

    buttonWith(ctx.el, 'Editar a consulta')!.click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('app-query-suggestion')).toBeNull();
    expect(ctx.el.querySelector('.qa-ai')).toBeNull();
  });

  // WEB-DBA-009
  it('erro do provedor aparece pela mensagem do código, sem mostrar a sugestão', () => {
    const ctx = render([AiUse.Suggestions]);
    suggestButton(ctx.el)!.click();
    ctx.http.expectOne(URL_SUGGEST).flush({ code: 'AI_PROVIDER_ERROR' }, { status: 502, statusText: 'Bad Gateway' });
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('.qa-ai [role="alert"]')!.textContent).toContain('O provedor de IA não respondeu. Tente de novo em instantes.');
    expect(ctx.el.querySelector('app-query-suggestion')).toBeNull();
    expect(suggestButton(ctx.el)!.disabled).toBeFalse();
  });
});
