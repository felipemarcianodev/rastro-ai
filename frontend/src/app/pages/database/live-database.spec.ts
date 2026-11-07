import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LiveConnectionView, QueryPlan, missingIndexText, operatorLabel } from '../../core/database/live-database';
import { lojaSchema } from '../../core/database/loja-schema.spec';
import { SqlDialect } from '../../core/database/db-schema';
import { QueryAnalysis } from '../../core/database/query-analysis';
import { DatabasePage } from './database.page';

const NONE: LiveConnectionView = { configured: false, server: null, database: null, updatedAt: null, dialect: SqlDialect.SqlServer };
const LIVE: LiveConnectionView = { configured: true, server: 'srv-loja', database: 'Loja', updatedAt: '2026-11-06T10:00:00Z', dialect: SqlDialect.SqlServer };
const LIVE_PG: LiveConnectionView = { configured: true, server: 'pg-loja:5433', database: 'loja', updatedAt: '2026-11-06T10:00:00Z', dialect: SqlDialect.PostgreSql };
const SECRET = 'Server=srv-loja;Database=Loja;User Id=leitor;Password=segredo123';
const SQL = 'SELECT * FROM pedidos WHERE criado_em > @p0';
const CONNECTION_URL = '/api/repositories/loja/connection';
const PLAN_URL = '/api/repositories/loja/schema/plan';

const PLAN: QueryPlan = {
  totalCost: 4.82,
  operations: [
    { physicalOp: 'Clustered Index Scan', logicalOp: 'Clustered Index Scan', object: 'pedidos.PK_pedidos', cost: 4.2, percent: 87, estimatedRows: 1200, scan: true },
    { physicalOp: 'Sort', logicalOp: 'Sort', object: null, cost: 0.6, percent: 13, estimatedRows: 1200, scan: false },
    { physicalOp: 'Spool Exótico', logicalOp: 'Spool', object: null, cost: 0, percent: 0, estimatedRows: 1, scan: false },
  ],
  missingIndexes: [{ table: 'pedidos', equality: [], inequality: ['criado_em'], include: ['status', 'total'], impact: 87 }],
  warnings: ['Conversão implícita em pedidos.status pode impedir o uso do índice.'],
};

function analysis(): QueryAnalysis {
  return { dialect: SqlDialect.SqlServer, statements: 1, findings: [], tables: [{ name: 'pedidos', alias: null, inSchema: true }], error: null };
}

function renderPage(connection: LiveConnectionView, aba?: string) {
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
  flushLoad(http, connection);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, http, el };
}

// A carga da tela: conexão, esquema e usos (o mesmo disparo do botão "Tentar de novo" e do salvar).
function flushLoad(http: HttpTestingController, connection: LiveConnectionView): void {
  http.expectOne(CONNECTION_URL).flush(connection);
  http.expectOne('/api/repositories/loja/schema').flush(lojaSchema());
  http.expectOne('/api/repositories/loja/schema/usage').flush({ uses: [], findings: [] });
}

const button = (el: HTMLElement, text: string) => [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.trim().includes(text))!;

// Clique de verdade: o botão precisa ser o alvo no centro dele, com o diálogo sobre o overlay.
function realClick(target: HTMLButtonElement): void {
  target.scrollIntoView({ block: 'center' });
  const box = target.getBoundingClientRect();
  expect(document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)?.closest('button')).toBe(target);
  target.click();
}

function open(ctx: ReturnType<typeof renderPage>, label: string): HTMLButtonElement {
  const opener = button(ctx.el, label);
  opener.focus();
  opener.click();
  ctx.fixture.detectChanges();
  return opener;
}

function typeKey(ctx: ReturnType<typeof renderPage>, value: string): void {
  const input = ctx.el.querySelector<HTMLInputElement>('#live-key')!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  ctx.fixture.detectChanges();
}

function analyze(ctx: ReturnType<typeof renderPage>): void {
  const box = ctx.el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
  box.value = SQL;
  box.dispatchEvent(new Event('input'));
  ctx.fixture.detectChanges();
  ctx.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
  ctx.http.expectOne('/api/repositories/loja/schema/analyze').flush(analysis());
  ctx.fixture.detectChanges();
}

describe('Banco vivo (Fase 40b)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-DBL-001
  it('sem conexão, a promessa é "Lido do código" e o botão convida a conectar, sem selo', () => {
    const { el } = renderPage(NONE);

    expect(el.textContent).toContain('Lido do código. Nenhum banco é consultado, a menos que você conecte um banco vivo, só de leitura.');
    expect(el.querySelector('.db-live-pill')).toBeNull();
    expect(button(el, 'Conectar banco vivo')).toBeDefined();
  });

  // WEB-DBL-002
  it('com conexão, o selo mostra servidor e banco, e a promessa fala do catálogo do banco vivo', () => {
    const { el } = renderPage(LIVE);

    expect(el.querySelector('.db-live-pill')!.textContent).toContain('Banco vivo: srv-loja/Loja, só leitura');
    expect(el.textContent).toContain('Lido do código e do catálogo do banco vivo. Nenhum dado das tabelas é lido.');
    expect(el.textContent).not.toContain('Nenhum banco é consultado');
    expect(button(el, 'Banco vivo')).toBeDefined();
    expect(el.textContent).not.toContain('Conectar banco vivo');
  });

  // WEB-DBL-003
  it('o diálogo salva com PUT, nunca mostra a string e recarrega o esquema e a conexão', () => {
    const ctx = renderPage(NONE);
    open(ctx, 'Conectar banco vivo');

    const input = ctx.el.querySelector<HTMLInputElement>('#live-key')!;
    const save = button(ctx.el, 'Salvar');
    expect(input.type).toBe('password');
    expect(input.autocomplete).toBe('off');
    expect(input.value).toBe('');
    expect(save.disabled).toBeTrue();
    expect(button(ctx.el, 'Testar conexão')).toBeUndefined();
    expect(ctx.el.querySelector('.dialog')!.textContent).toContain('GRANT VIEW DEFINITION TO usuario');
    expect(ctx.el.querySelector('.dialog code')!.textContent).toContain('GRANT');

    typeKey(ctx, SECRET);
    expect(save.disabled).toBeFalse();
    realClick(save);

    const put = ctx.http.expectOne(CONNECTION_URL);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ connectionString: SECRET });
    put.flush(LIVE);
    ctx.fixture.detectChanges();
    // Salvou: a tela lê de novo o esquema (a fonte "banco vivo" entra) e a conexão.
    flushLoad(ctx.http, LIVE);
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector<HTMLInputElement>('#live-key')!.value).toBe('');
    expect(ctx.el.innerHTML).not.toContain('segredo123');
    expect(ctx.el.querySelector('.db-live-pill')!.textContent).toContain('srv-loja/Loja');
    expect(ctx.el.querySelector('.dialog')!.textContent).toContain('Conexão salva (SQL Server): srv-loja, banco Loja.');
    expect(ctx.el.querySelector('.dialog [role="status"]')!.textContent).toContain('Conexão salva');
  });

  // WEB-DBL-004
  it('string inválida mostra a mensagem do código no diálogo', () => {
    const ctx = renderPage(NONE);
    open(ctx, 'Conectar banco vivo');
    typeKey(ctx, 'lixo');
    button(ctx.el, 'Salvar').click();
    ctx.http.expectOne(CONNECTION_URL).flush({ code: 'CONNECTION_INVALID' }, { status: 400, statusText: 'Bad Request' });
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('.dialog [role="alert"]')!.textContent).toContain('String de conexão inválida');
  });

  // WEB-DBL-005
  it('testa a conexão salva e mostra o resultado, de sucesso (status) e de falha (alert)', () => {
    const ctx = renderPage(LIVE);
    open(ctx, 'Banco vivo');

    realClick(button(ctx.el, 'Testar conexão'));
    let post: TestRequest = ctx.http.expectOne(`${CONNECTION_URL}/test`);
    expect(post.request.method).toBe('POST');
    post.flush({ ok: true, message: 'Conectou: 12 tabelas no catálogo.' });
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.dialog [role="status"]')!.textContent).toContain('Conectou: 12 tabelas no catálogo.');

    button(ctx.el, 'Testar conexão').click();
    post = ctx.http.expectOne(`${CONNECTION_URL}/test`);
    post.flush({ ok: false, message: 'Não conectou: login recusado.' });
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.dialog [role="alert"]')!.textContent).toContain('Não conectou: login recusado.');
  });

  // WEB-DBL-006
  it('remove só depois da confirmação no próprio diálogo e recarrega o esquema', () => {
    const ctx = renderPage(LIVE);
    open(ctx, 'Banco vivo');

    realClick(button(ctx.el, 'Remover conexão'));
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.dialog')!.textContent).toContain('Remover a conexão? O Rastro volta a ler só o código.');
    ctx.http.expectNone(CONNECTION_URL);

    realClick(button(ctx.el, 'Remover'));
    const del = ctx.http.expectOne(CONNECTION_URL);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    ctx.fixture.detectChanges();
    flushLoad(ctx.http, NONE);
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('.dialog')).toBeNull();
    expect(ctx.el.querySelector('.db-live-pill')).toBeNull();
    expect(ctx.el.textContent).toContain('Lido do código. Nenhum banco é consultado, a menos que você conecte um banco vivo, só de leitura.');
  });

  // WEB-DBL-007
  it('Esc fecha o diálogo e o foco volta ao botão que abriu', () => {
    const ctx = renderPage(LIVE);
    const opener = open(ctx, 'Banco vivo');
    expect(ctx.el.querySelector('.dialog')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('.dialog')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  // WEB-DBL-008
  it('o plano só é pedido pelo botão e mostra operações, porcentagem, varredura, índice faltando e aviso', () => {
    const ctx = renderPage(LIVE, 'consulta');
    analyze(ctx);

    const card = ctx.el.querySelector('app-query-plan')!;
    expect(card.textContent).toContain('Plano estimado pelo banco');
    expect(card.textContent).toContain('SET SHOWPLAN_XML: o banco diz como faria, sem executar a consulta. Parâmetros (@p0) entram como texto.');
    // Está acima de "Tabelas da consulta".
    const side = ctx.el.querySelector('.qa-side')!;
    expect(side.firstElementChild!.tagName).toBe('APP-QUERY-PLAN');
    ctx.http.expectNone(PLAN_URL);

    realClick(button(card as HTMLElement, 'Pedir o plano estimado'));
    const post = ctx.http.expectOne(PLAN_URL);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ sql: SQL });
    post.flush(PLAN);
    ctx.fixture.detectChanges();

    // A operação abaixo de 1% fica atrás do botão (UX-DBL-04).
    expect(card.querySelectorAll('.plan-op').length).toBe(2);
    realClick(button(card as HTMLElement, 'mais 1 operação abaixo de 1%'));
    ctx.fixture.detectChanges();
    const ops = [...card.querySelectorAll('.plan-op')];
    expect(ops.length).toBe(3);
    expect(ops[0].textContent).toContain('Varredura do índice clustered');
    expect(ops[0].textContent).toContain('pedidos.PK_pedidos');
    expect(ops[0].textContent).toContain('87%');
    expect(ops[0].textContent).toContain('varredura');
    expect(ops[0].querySelector<HTMLElement>('.plan-bar-fill')!.style.width).toBe('87%');
    expect(ops[1].textContent).toContain('Ordenação');
    expect(ops[1].textContent).not.toContain('varredura');
    expect(ops[2].textContent).toContain('Spool Exótico');
    expect(card.textContent).toContain('O próprio banco aponta índice faltando em pedidos (criado_em), ganho estimado de 87%');
    expect(card.textContent).toContain('inclui: status, total');
    expect(card.textContent).toContain('Conversão implícita em pedidos.status pode impedir o uso do índice.');
  });

  // WEB-DBL-009
  it('plano recusado mostra o motivo do title; outro erro, a mensagem do código', () => {
    const ctx = renderPage(LIVE, 'consulta');
    analyze(ctx);
    const card = ctx.el.querySelector('app-query-plan') as HTMLElement;

    button(card, 'Pedir o plano estimado').click();
    ctx.http.expectOne(PLAN_URL).flush({ code: 'PLAN_NOT_ALLOWED', title: 'O plano não sai para EXEC.' }, { status: 400, statusText: 'Bad Request' });
    ctx.fixture.detectChanges();
    expect(card.querySelector('[role="alert"]')!.textContent).toContain('O plano não sai para EXEC.');

    button(card, 'Pedir o plano estimado').click();
    ctx.http.expectOne(PLAN_URL).flush({ code: 'LIVE_DATABASE_ERROR' }, { status: 502, statusText: 'Bad Gateway' });
    ctx.fixture.detectChanges();
    expect(card.querySelector('[role="alert"]')!.textContent).toContain('O banco vivo não respondeu.');
  });

  // WEB-DBL-010
  it('sem conexão não há cartão nem botão de plano', () => {
    const ctx = renderPage(NONE, 'consulta');
    analyze(ctx);

    expect(ctx.el.querySelector('app-query-plan')).toBeNull();
    expect(ctx.el.textContent).not.toContain('Pedir o plano estimado');
  });

  // WEB-DBL-011
  it('editar a consulta descarta o plano', () => {
    const ctx = renderPage(LIVE, 'consulta');
    analyze(ctx);
    button(ctx.el, 'Pedir o plano estimado').click();
    ctx.http.expectOne(PLAN_URL).flush(PLAN);
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.plan-op')).not.toBeNull();

    button(ctx.el, 'Editar a consulta').click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('app-query-plan')).toBeNull();

    ctx.el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
    ctx.http.expectOne('/api/repositories/loja/schema/analyze').flush(analysis());
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.plan-op')).toBeNull();
    expect(button(ctx.el, 'Pedir o plano estimado')).toBeDefined();
  });
});

describe('Banco vivo: rótulos (Fase 40b)', () => {
  // WEB-DBL-012
  it('rótulos dos operadores em pt-BR e texto do índice faltando', () => {
    expect(operatorLabel('Index Seek')).toBe('Busca no índice');
    expect(operatorLabel('Key Lookup')).toBe('Busca da chave');
    expect(operatorLabel('Nested Loops')).toBe('Laços aninhados');
    expect(operatorLabel('Constant Scan')).toBe('Constant Scan');
    expect(operatorLabel('toString')).toBe('toString');
    expect(missingIndexText({ table: 'pedidos', equality: ['status'], inequality: ['criado_em'], include: [], impact: 86.6 })).toBe(
      'O próprio banco aponta índice faltando em pedidos (status, criado_em), ganho estimado de 87%',
    );
  });
});

describe('Banco vivo do PostgreSQL (Fase 42b)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-PGL-001
  it('o diálogo mostra o dialeto e a permissão mínima do PostgreSQL', () => {
    const ctx = renderPage(LIVE_PG);
    open(ctx, 'Banco vivo');

    const dialog = ctx.el.querySelector('.dialog')!.textContent!;
    expect(dialog).toContain('Conexão salva (PostgreSQL): pg-loja:5433, banco loja.');
    expect(dialog).toContain('GRANT CONNECT ON DATABASE banco TO usuario');
    expect(dialog).toContain('ALTER ROLE usuario SET default_transaction_read_only = on');
    expect(dialog).toContain('EXPLAIN sem ANALYZE');
    expect(dialog).not.toContain('SHOWPLAN');
  });

  // WEB-PGL-002
  it('o cartão do plano diz que o EXPLAIN não executa e traduz os operadores do PostgreSQL', () => {
    const ctx = renderPage(LIVE_PG, 'consulta');
    analyze(ctx);

    const card = ctx.el.querySelector('app-query-plan')!;
    expect(card.textContent).toContain('EXPLAIN sem ANALYZE, numa conexão só de leitura');
    expect(card.textContent).not.toContain('SHOWPLAN');
    expect(operatorLabel('Seq Scan')).toBe('Varredura da tabela');
    expect(operatorLabel('Hash Join')).toBe('Junção por hash');
  });

  // WEB-DBL-020 (B24)
  it('salvar a conexão mantém a consulta e o resultado, e o esquema atual fica até o novo chegar', () => {
    const ctx = renderPage(NONE, 'consulta');
    analyze(ctx);
    open(ctx, 'Conectar banco vivo');
    typeKey(ctx, SECRET);
    button(ctx.el, 'Salvar').click();
    ctx.http.expectOne(CONNECTION_URL).flush(null, { status: 204, statusText: 'No Content' });
    ctx.fixture.detectChanges();

    // Antes de o esquema novo chegar, a análise continua na tela.
    expect(ctx.el.querySelector('.qa-sql')!.textContent).toBe(SQL);
    flushLoad(ctx.http, LIVE);
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.qa-sql')!.textContent).toBe(SQL);
    expect(ctx.el.querySelector('.db-live-pill')).not.toBeNull();
  });

  // WEB-DBL-021 (B24)
  it('remover a conexão avisa que o Rastro volta a ler só o código, sem apagar a consulta', () => {
    const ctx = renderPage(LIVE, 'consulta');
    analyze(ctx);
    open(ctx, 'Banco vivo');
    realClick(button(ctx.el, 'Remover conexão'));
    ctx.fixture.detectChanges();
    realClick(button(ctx.el, 'Remover'));
    ctx.http.expectOne(CONNECTION_URL).flush(null, { status: 204, statusText: 'No Content' });
    ctx.fixture.detectChanges();
    flushLoad(ctx.http, NONE);
    ctx.fixture.detectChanges();

    expect(ctx.el.querySelector('.toast')!.textContent).toContain('Conexão removida. O Rastro volta a ler só o código.');
    expect(ctx.el.querySelector('.qa-sql')!.textContent).toBe(SQL);
  });

  // WEB-DBL-022 (B23)
  it('o campo da conexão vem antes das permissões, que são uma lista, e Fechar vem antes de Salvar', () => {
    const ctx = renderPage(NONE);
    open(ctx, 'Conectar banco vivo');

    const dialog = ctx.el.querySelector('.dialog')!;
    const field = dialog.querySelector('#live-key')!;
    const grants = dialog.querySelector('.live-grants')!;
    expect(field.compareDocumentPosition(grants) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(grants.querySelectorAll('ul > li').length).toBeGreaterThan(2);
    const labels = [...dialog.querySelectorAll<HTMLButtonElement>('.dialog-foot button')].map((b) => b.textContent!.trim());
    expect(labels.indexOf('Fechar')).toBeGreaterThanOrEqual(0);
    expect(labels.indexOf('Fechar')).toBeLessThan(labels.indexOf('Salvar'));
  });

  // WEB-DBL-023 (B23)
  it('clicar fora fecha o diálogo vazio, mas não quando há texto digitado', () => {
    const ctx = renderPage(NONE);
    open(ctx, 'Conectar banco vivo');
    typeKey(ctx, SECRET);

    ctx.el.querySelector<HTMLElement>('.overlay')!.click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.dialog')).not.toBeNull();
    expect((ctx.el.querySelector('#live-key') as HTMLInputElement).value).toBe(SECRET);

    typeKey(ctx, '');
    ctx.el.querySelector<HTMLElement>('.overlay')!.click();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('.dialog')).toBeNull();
  });
});
