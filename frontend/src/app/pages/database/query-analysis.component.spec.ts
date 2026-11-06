import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SqlDialect } from '../../core/database/db-schema';
import { lojaSchema } from '../../core/database/loja-schema.spec';
import { QueryAnalysis, QueryFindingKind, QuerySeverity } from '../../core/database/query-analysis';
import { DatabasePage } from './database.page';
import { QueryAnalysisComponent } from './query-analysis.component';

const SQL = 'SELECT * FROM pedidos p JOIN fantasma f ON f.id = p.id WHERE UPPER(p.status) = @s';

function analysis(overrides: Partial<QueryAnalysis> = {}): QueryAnalysis {
  const star = SQL.indexOf('*');
  const upper = SQL.indexOf('UPPER');
  return {
    dialect: SqlDialect.SqlServer,
    statements: 1,
    findings: [
      { kind: QueryFindingKind.FunctionOnFilterColumn, severity: QuerySeverity.High, start: upper, length: 15, line: 1, snippet: 'UPPER(p.status)', message: 'A função impede o uso do índice.', table: 'pedidos' },
      { kind: QueryFindingKind.SelectStar, severity: QuerySeverity.Low, start: star, length: 1, line: 1, snippet: '*', message: 'Traz todas as colunas.', table: null },
    ],
    tables: [
      { name: 'pedidos', alias: 'p', inSchema: true },
      { name: 'fantasma', alias: 'f', inSchema: false },
    ],
    error: null,
    ...overrides,
  };
}

function render(initial?: string, origin?: string) {
  TestBed.configureTestingModule({
    imports: [QueryAnalysisComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const fixture = TestBed.createComponent(QueryAnalysisComponent);
  fixture.componentRef.setInput('repository', 'loja');
  fixture.componentRef.setInput('schema', lojaSchema());
  if (initial) fixture.componentRef.setInput('initial', initial);
  if (origin) fixture.componentRef.setInput('origin', origin);
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

function analyze(ctx: ReturnType<typeof render>, result: QueryAnalysis) {
  const box = ctx.el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
  box.value = SQL;
  box.dispatchEvent(new Event('input'));
  ctx.fixture.detectChanges();
  ctx.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
  const request = ctx.http.expectOne('/api/repositories/loja/schema/analyze');
  expect(request.request.method).toBe('POST');
  expect(request.request.body).toEqual({ sql: SQL });
  request.flush(result);
  ctx.fixture.detectChanges();
}

describe('QueryAnalysisComponent (Fase 40a)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-DBQ-010
  it('começa com a caixa de texto, o botão desabilitado sem texto e a origem da consulta', () => {
    const empty = render();
    expect(empty.el.querySelector('label[for="qa-sql"]')!.textContent).toContain('Consulta SQL (SQL Server)');
    expect(empty.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.disabled).toBeTrue();
    expect(empty.el.textContent).toContain('Cole uma consulta ou abra uma pelo passo do fluxo');
    TestBed.resetTestingModule();

    const filled = render('SELECT 1', 'PedidoRepositorio.DoAno');
    expect(filled.el.querySelector<HTMLTextAreaElement>('#qa-sql')!.value).toBe('SELECT 1');
    expect(filled.el.textContent).toContain('Veio do passo PedidoRepositorio.DoAno');
    expect(filled.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.disabled).toBeFalse();
  });

  // WEB-DBQ-018
  it('editada, a consulta deixa de dizer que veio do passo; sem achado, o título diz "Sem achados"', () => {
    const ctx = render('SELECT 1', 'PedidoRepositorio.DoAno');
    analyze(ctx, analysis({ findings: [] }));
    expect(ctx.el.textContent).not.toContain('Veio do passo');
    expect(ctx.el.querySelector('.qa-count .qa-title')!.textContent).toContain('Sem achados');
  });

  // WEB-DBQ-011
  it('analisa, marca os trechos na consulta e lista os achados com a gravidade em texto', () => {
    const ctx = render();
    analyze(ctx, analysis());

    const marks = [...ctx.el.querySelectorAll('mark')].map((m) => m.textContent);
    expect(marks).toEqual(['*', 'UPPER(p.status)']);
    expect(ctx.el.querySelector('.qa-sql')!.textContent).toBe(SQL);
    expect(ctx.el.textContent).toContain('2 achados');
    expect(ctx.el.textContent).toContain('Regra fixa, sem IA');
    const items = [...ctx.el.querySelectorAll('.qa-finding')];
    expect(items[0].textContent).toContain('Alta');
    expect(items[0].textContent).toContain('Função ou conversão na coluna do filtro');
    expect(items[0].textContent).toContain('A função impede o uso do índice.');
    expect(items[1].textContent).toContain('Baixa');
    expect(items[1].textContent).toContain('SELECT *');
  });

  // WEB-DBQ-012
  it('o clique de verdade no achado destaca o trecho, e clicar de novo desmarca', () => {
    const ctx = render();
    analyze(ctx, analysis());
    const buttons = [...ctx.el.querySelectorAll<HTMLButtonElement>('.qa-finding-btn')];

    // O botão é o alvo do clique no centro dele (nada por cima).
    buttons[0].scrollIntoView({ block: 'center' });
    const box = buttons[0].getBoundingClientRect();
    expect(document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)?.closest('.qa-finding-btn')).toBe(buttons[0]);

    buttons[0].click();
    ctx.fixture.detectChanges();
    expect(buttons[0].getAttribute('aria-pressed')).toBe('true');
    expect([...ctx.el.querySelectorAll('mark.is-active')].map((m) => m.textContent)).toEqual(['UPPER(p.status)']);

    buttons[1].click();
    ctx.fixture.detectChanges();
    expect([...ctx.el.querySelectorAll('mark.is-active')].map((m) => m.textContent)).toEqual(['*']);
    expect(buttons[0].getAttribute('aria-pressed')).toBe('false');

    buttons[1].click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelectorAll('mark.is-active').length).toBe(0);
  });

  // WEB-DBQ-013
  it('consulta limpa diz que não há achado, e Editar a consulta volta para a caixa', () => {
    const ctx = render();
    analyze(ctx, analysis({ findings: [] }));

    expect(ctx.el.textContent).toContain('Nenhum achado nesta consulta.');
    expect(ctx.el.textContent).toContain('Regra fixa, sem IA');
    expect(ctx.el.querySelectorAll('mark').length).toBe(0);

    [...ctx.el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Editar a consulta'))!.click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector<HTMLTextAreaElement>('#qa-sql')!.value).toBe(SQL);
    expect(ctx.el.querySelector('.qa-sql')).toBeNull();
  });

  // WEB-DBQ-014
  it('erro de leitura aparece no aviso, sem lista de achados', () => {
    const ctx = render();
    analyze(ctx, analysis({ findings: [], tables: [], error: { message: 'Sintaxe incorreta perto de FROM.', line: 2, column: 5 } }));

    const notice = ctx.el.querySelector('.notice.is-error')!;
    expect(notice.textContent).toContain('O leitor de T-SQL não entendeu a consulta (linha 2, coluna 5): Sintaxe incorreta perto de FROM.');
    expect(ctx.el.querySelector('.qa-findings')).toBeNull();
    expect(ctx.el.querySelector('.qa-side')).toBeNull();
  });

  // WEB-DBQ-015
  it('erro da API mostra a mensagem do código e deixa tentar de novo', () => {
    const ctx = render('SELECT 1');
    ctx.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
    ctx.http.expectOne('/api/repositories/loja/schema/analyze').flush({ code: 'QUERY_INVALID' }, { status: 400, statusText: 'Bad Request' });
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('[role="alert"]')!.textContent).toContain('Cole uma consulta de até 100 mil caracteres.');
    [...ctx.el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Tentar de novo'))!.click();
    ctx.http.expectOne('/api/repositories/loja/schema/analyze').flush(analysis({ findings: [] }));
    ctx.fixture.detectChanges();
    expect(ctx.el.textContent).toContain('Nenhum achado nesta consulta.');
  });

  // WEB-DBQ-016
  it('tabela fora do esquema avisa, e a do esquema leva ao mapa', () => {
    const ctx = render();
    const picked: string[] = [];
    ctx.fixture.componentInstance.pickTable.subscribe((name: string) => picked.push(name));
    analyze(ctx, analysis());

    const side = ctx.el.querySelector('.qa-side')!;
    expect(side.textContent).toContain('Tabelas da consulta');
    expect(side.textContent).toContain('apelido p');
    expect(side.textContent).toContain('fora do esquema lido do código: os achados de índice e de nulo não valem para ela');
    expect(side.querySelectorAll('button').length).toBe(1);

    side.querySelector<HTMLButtonElement>('button')!.click();
    [...ctx.el.querySelectorAll<HTMLButtonElement>('.qa-map')][0].click();
    expect(picked).toEqual(['pedidos', 'pedidos']);
  });

  // WEB-DBQ-017
  it('mudar o texto descarta o resultado, e Ctrl+Enter também analisa', () => {
    const ctx = render('SELECT 1');
    const box = ctx.el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    ctx.http.expectOne('/api/repositories/loja/schema/analyze').flush(analysis({ findings: [] }));
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.qa-sql')).not.toBeNull();

    (ctx.fixture.componentInstance as unknown as { edit(v: string): void }).edit('SELECT 2');
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.qa-sql')).toBeNull();
    expect(ctx.el.querySelector<HTMLTextAreaElement>('#qa-sql')!.value).toBe('SELECT 2');
  });
});

describe('DatabasePage: abas (Fase 40a)', () => {
  function renderPage(aba?: string) {
    TestBed.configureTestingModule({
      imports: [DatabasePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(DatabasePage);
    fixture.componentRef.setInput('nome', 'loja');
    if (aba) fixture.componentRef.setInput('aba', aba);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:/loja' }]);
    http.expectOne('/api/repositories/loja/connection').flush({ configured: false, server: null, database: null, updatedAt: null });
    http.expectOne('/api/repositories/loja/schema').flush(lojaSchema());
    http.expectOne('/api/repositories/loja/schema/usage').flush({ uses: [], findings: [] });
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-DBQ-020
  it('abre o mapa por padrão, com o Exportar, e a aba consulta pela URL, sem o Exportar', () => {
    const map = renderPage();
    const tabs = [...map.el.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
    expect(tabs.map((t) => t.textContent!.trim())).toEqual(['Mapa do banco', 'Analisar consulta']);
    expect(tabs.map((t) => t.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(map.el.querySelector('.popover')).not.toBeNull();
    expect(map.el.querySelector<HTMLElement>('.db-body')!.hidden).toBeFalse();
    expect(map.el.querySelector<HTMLElement>('app-query-analysis')!.hidden).toBeTrue();
    expect(map.el.textContent).toContain('Nenhum banco é consultado');
    TestBed.resetTestingModule();

    const query = renderPage('consulta');
    const selected = [...query.el.querySelectorAll<HTMLButtonElement>('[role="tab"]')].map((t) => t.getAttribute('aria-selected'));
    expect(selected).toEqual(['false', 'true']);
    expect(query.el.querySelector('.popover')).toBeNull();
    expect(query.el.querySelector<HTMLElement>('.db-body')!.hidden).toBeTrue();
    expect(query.el.querySelector<HTMLElement>('app-query-analysis')!.hidden).toBeFalse();
    expect(query.el.querySelector('[role="tabpanel"]:not([hidden])')!.id).toBe('db-panel-1');
  });

  // WEB-DBQ-021
  it('trocar de aba atualiza a URL e mantém o resto dos parâmetros', () => {
    const { fixture, el } = renderPage();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const tabs = [...el.querySelectorAll<HTMLButtonElement>('[role="tab"]')];

    tabs[1].click();
    expect(navigate).toHaveBeenCalledWith([], { queryParams: { aba: 'consulta' }, queryParamsHandling: 'merge' });

    fixture.componentRef.setInput('aba', 'consulta');
    fixture.detectChanges();
    navigate.calls.reset();
    tabs[0].click();
    expect(navigate).toHaveBeenCalledWith([], { queryParams: { aba: null }, queryParamsHandling: 'merge' });
  });

  // WEB-DBQ-022
  it('a tabela escolhida na análise leva ao mapa com ela selecionada', () => {
    const { fixture, el } = renderPage('consulta');
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const box = el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
    box.value = 'SELECT 1';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
    TestBed.inject(HttpTestingController).expectOne('/api/repositories/loja/schema/analyze').flush(analysis({ findings: [], tables: [{ name: 'pedidos', alias: null, inSchema: true }] }));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('.qa-side .source-link')!.click();
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith([], { queryParams: { aba: null, tabela: 'pedidos' }, queryParamsHandling: 'merge' });
    expect(el.querySelector('.db-card[data-table="pedidos"]')!.classList).toContain('is-selected');
  });
});

describe('QueryAnalysisComponent no PostgreSQL (Fase 42)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-PG-001
  it('o rótulo da caixa e o erro de leitura saem do dialeto do repositório', () => {
    TestBed.configureTestingModule({
      imports: [QueryAnalysisComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(QueryAnalysisComponent);
    fixture.componentRef.setInput('repository', 'loja');
    fixture.componentRef.setInput('schema', { ...lojaSchema(), dialect: SqlDialect.PostgreSql });
    fixture.detectChanges();
    const ctx = { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
    expect(ctx.el.querySelector('label[for="qa-sql"]')!.textContent).toContain('Consulta SQL (PostgreSQL)');

    analyze(ctx, analysis({ dialect: SqlDialect.PostgreSql, findings: [], tables: [], error: { message: 'Expected identifier, found (', line: 1, column: 14 } }));

    expect(ctx.el.querySelector('.notice.is-error')!.textContent).toContain('O leitor de SQL do PostgreSQL não entendeu a consulta (linha 1, coluna 14): Expected identifier, found (');
  });
  // WEB-DBQ-031
  it('trechos sobrepostos pegam a cor da gravidade maior (alta vence baixa) e o achado novo tem título', () => {
    const upper = SQL.indexOf('UPPER');
    const ctx = render();
    analyze(ctx, analysis({
      findings: [
        { kind: QueryFindingKind.ReadWithoutLimit, severity: QuerySeverity.Low, start: upper, length: 15, line: 1, snippet: 'UPPER(p.status)', message: 'Sem limite.', table: 'pedidos' },
        { kind: QueryFindingKind.FunctionOnFilterColumn, severity: QuerySeverity.High, start: upper, length: 15, line: 1, snippet: 'UPPER(p.status)', message: 'A função impede o uso do índice.', table: 'pedidos' },
      ],
    }));

    expect(ctx.el.querySelector('mark')!.classList).toContain('is-high');
    expect(ctx.el.textContent).toContain('Leitura sem limite, filtrada só por igualdade');
  });
});
