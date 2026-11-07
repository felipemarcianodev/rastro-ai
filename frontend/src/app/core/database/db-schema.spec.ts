import { DB_CARD, DB_CARD_MAX_ROWS, dbMapGeometry, dbMapSvg, shortType } from './db-map';
import { Cardinality, KEY_MARK_LABEL, KeyMark, columnKeys, estimatedRowsText, findingText, keyMark, linkTableOf, matches, neighborhood, relationsOf, toErMermaid } from './db-schema';
import { lojaSchema, wideSchema } from './loja-schema.spec';

const colors = { paper: '#f2f4f1', surface: '#ffffff', surfaceSunk: '#e8ece9', ink: '#1d2a2e', inkSoft: '#5c6a6e', lineStrong: '#b7c1bc', edge: '#6b787c' };

describe('esquema do banco (Fase 38)', () => {
  // WEB-DB-001
  it('liga as FKs à tabela de destino com a cardinalidade, com e sem o schema no nome', () => {
    const relations = relationsOf(lojaSchema());

    expect(relations.map((r) => `${r.from}>${r.to}`)).toEqual(['itens_pedido>pedidos', 'itens_pedido>produtos', 'pagamentos>pedidos', 'pedidos>clientes']);
    expect(relations.every((r) => r.cardinality === Cardinality.ManyToOne)).toBeTrue();
  });

  // WEB-DB-002
  it('reconhece a tabela de ligação (N para N) pela chave feita das duas FKs', () => {
    const schema = lojaSchema();

    expect(linkTableOf(schema.tables.find((t) => t.name === 'itens_pedido')!, schema)).toEqual(['pedidos', 'produtos']);
    expect(linkTableOf(schema.tables.find((t) => t.name === 'pagamentos')!, schema)).toBeNull();
  });

  // WEB-DB-003
  it('recorta a tabela e as vizinhas até N saltos, nos dois sentidos da FK', () => {
    const relations = relationsOf(lojaSchema());

    expect([...neighborhood(relations, 'pagamentos', 1)].sort()).toEqual(['pagamentos', 'pedidos']);
    expect([...neighborhood(relations, 'pagamentos', 2)].sort()).toEqual(['clientes', 'itens_pedido', 'pagamentos', 'pedidos']);
    expect(neighborhood(relations, 'log_eventos', 3).size).toBe(1);
  });

  // WEB-DB-004
  it('busca pelo nome da tabela e pelo nome do campo', () => {
    const clientes = lojaSchema().tables[0];

    expect(matches('CLI', clientes)).toBeTrue();
    expect(matches('cpf', clientes, clientes.columns.map((c) => c.name))).toBeTrue();
    expect(matches('estoque', clientes, clientes.columns.map((c) => c.name))).toBeFalse();
    expect(matches('  ', clientes)).toBeTrue();
  });

  // WEB-DB-005
  it('desenha quem é apontado à esquerda e quem aponta à direita, sem repetir posição', () => {
    const schema = lojaSchema();
    const g = dbMapGeometry(schema.tables, relationsOf(schema))!;
    const x = (key: string) => g.cards.find((c) => c.key === key)!.x;

    expect(x('clientes')).toBeLessThan(x('pedidos'));
    expect(x('pedidos')).toBeLessThan(x('pagamentos'));
    expect(x('produtos')).toBeLessThan(x('itens_pedido'));
    expect(new Set(g.cards.map((c) => `${c.x}:${c.y}`)).size).toBe(g.cards.length);
    expect(g.links.length).toBe(4);
    expect(g.links.every((l) => l.text === '1 : N')).toBeTrue();
    expect(dbMapGeometry([], [])).toBeNull();
  });

  // WEB-DB-006
  it('exporta o erDiagram só com o que está no mapa, com chave, dado pessoal e tipo sem vírgula', () => {
    const text = toErMermaid(lojaSchema(), new Set(['pagamentos', 'pedidos']));

    expect(text.startsWith('erDiagram\n')).toBeTrue();
    expect(text).toContain('  pagamentos {');
    expect(text).toContain('    decimal(10_2) valor');
    expect(text).toContain('    int pedido_id FK');
    expect(text).toContain('    divergente status');
    expect(text).toContain('  pedidos ||--o{ pagamentos : "pedido_id"');
    expect(text).not.toContain('clientes');

    const clientes = toErMermaid(lojaSchema(), new Set(['clientes']));
    expect(clientes).toContain('    char(11) cpf "dado pessoal"');
    expect(clientes).toContain('    int id PK');
  });

  // WEB-DB-007
  it('gera o SVG do mapa com os blocos, as ligações e o selo do achado', () => {
    const schema = lojaSchema();
    const svg = dbMapSvg(dbMapGeometry(schema.tables, relationsOf(schema))!, colors, new Map([['pagamentos', 'FK sem índice']]));
    const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');

    expect(doc.querySelector('parsererror')).toBeNull();
    expect(doc.querySelectorAll('path[marker-end]').length).toBe(4);
    expect(svg).toContain('FK sem índice');
    expect(svg).toContain('cpf *');
  });

  // WEB-DB-008
  it('explica o achado com a fonte que criou a FK', () => {
    expect(findingText(lojaSchema().findings[1])).toContain('migration Loja.Api/Migrations/20261020_Pagamentos.cs:24 cria a FK pedido_id sem índice');
  });

  // CORE-DBM-020 (B26)
  it('shortType abrevia os tipos longos do PostgreSQL e mantém os outros', () => {
    expect(shortType('character varying(120)')).toBe('varchar(120)');
    expect(shortType('timestamp with time zone')).toBe('timestamptz');
    expect(shortType('numeric(10, 2)')).toBe('numeric(10,2)');
    expect(shortType('nvarchar(max)')).toBe('nvarchar(max)');
  });

  // CORE-DBM-021 (B27)
  it('sem chave estrangeira, o mapa distribui os blocos em grade', () => {
    const tables = lojaSchema().tables.map((t) => ({ ...t, foreignKeys: [] }));
    const geometry = dbMapGeometry(tables, [])!;

    expect(new Set(geometry.cards.map((c) => c.x)).size).toBe(3);
    expect(geometry.cards.length).toBe(6);
  });

  // UX-FIM-B17
  it('a marca de chave é um enum numérico com o rótulo vindo do Record, e o bloco do mapa traz o booleano da chave primária', () => {
    const schema = lojaSchema();
    const pedidos = schema.tables.find((t) => t.name === 'pedidos')!;
    const itens = schema.tables.find((t) => t.name === 'itens_pedido')!;

    expect(keyMark(columnKeys(pedidos, 'id'))).toBe(KeyMark.Pk);
    expect(keyMark(columnKeys(pedidos, 'cliente_id'))).toBe(KeyMark.Fk);
    expect(keyMark(columnKeys(pedidos, 'status'))).toBe(KeyMark.None);
    expect(keyMark(columnKeys(itens, 'pedido_id'))).toBe(KeyMark.PkFk);
    expect(Object.values(KeyMark).filter((v) => typeof v === 'number').map((v) => KEY_MARK_LABEL[v as KeyMark])).toEqual(['', 'PK', 'FK', 'PK FK']);

    const card = dbMapGeometry(schema.tables, relationsOf(schema))!.cards.find((c) => c.key === 'pedidos')!;
    const byName = new Map(card.rows.map((r) => [r.name, r]));
    expect(byName.get('id')).toEqual(jasmine.objectContaining({ mark: KeyMark.Pk, markLabel: 'PK', isPrimaryKey: true }));
    expect(byName.get('cliente_id')).toEqual(jasmine.objectContaining({ mark: KeyMark.Fk, markLabel: 'FK', isPrimaryKey: false }));
  });

  // WEB-DB-009
  it('o cartão aberto mostra todos os campos, desce os de baixo e liga a FK que estava no "+N"', () => {
    const schema = wideSchema();
    const relations = relationsOf(schema);
    const closed = dbMapGeometry(schema.tables, relations)!;
    const pedidos = closed.cards.find((c) => c.key === 'pedidos')!;

    expect(pedidos.rows.length).toBe(DB_CARD_MAX_ROWS);
    expect(pedidos.more).toBe(6);
    expect(pedidos.extra).toBe(6);
    expect(pedidos.open).toBeFalse();
    expect(pedidos.rows.some((r) => r.name === 'cliente_id')).toBeFalse();

    const open = dbMapGeometry(schema.tables, relations, new Set(['pedidos']))!;
    const opened = open.cards.find((c) => c.key === 'pedidos')!;
    expect(opened.rows.length).toBe(18);
    expect(opened.more).toBe(0);
    expect(opened.open).toBeTrue();
    expect(opened.height - pedidos.height).toBe(6 * DB_CARD.row);
    for (const card of open.cards.filter((c) => c.x === opened.x && c.y > opened.y)) {
      expect(card.y - closed.cards.find((c) => c.key === card.key)!.y).toBe(6 * DB_CARD.row);
    }

    // A ligação de pedidos para clientes saía do cabeçalho; aberto, sai da linha do cliente_id.
    const link = (g: typeof open) => g.links.find((l) => l.relation.from === 'pedidos')!.d;
    const rowY = opened.y + DB_CARD.header + opened.rows.findIndex((r) => r.name === 'cliente_id') * DB_CARD.row + DB_CARD.row / 2;
    expect(link(closed)).toContain(` ${pedidos.y + DB_CARD.header / 2} `);
    expect(link(open)).toContain(` ${rowY} `);

    // Tabela sem campo escondido não abre, e o mesmo pedido dá o mesmo desenho.
    expect(dbMapGeometry(schema.tables, relations, new Set(['clientes']))).toEqual(closed);
    expect(dbMapGeometry(schema.tables, relations, new Set(['pedidos']))).toEqual(open);
  });
});

// Fase 44: estimativa de linhas do banco vivo no painel da tabela.
describe('estimatedRowsText', () => {
  it('formata com ponto de milhar e diz que é estimativa', () => {
    expect(estimatedRowsText(123456)).toBe('Cerca de 123.456 linhas, pela estimativa do banco vivo.');
    expect(estimatedRowsText(1)).toBe('Cerca de 1 linha, pela estimativa do banco vivo.');
  });

  it('sem banco vivo ou sem estatística, nada', () => {
    expect(estimatedRowsText(null)).toBeNull();
    expect(estimatedRowsText(undefined)).toBeNull();
    expect(estimatedRowsText(0)).toBeNull();
  });
});
