import { QUERY_FINDING_TITLE, QUERY_SEVERITY_LABEL, QUERY_SEVERITY_SLUG, QueryFindingKind, QuerySeverity, querySegments } from './query-analysis';

describe('querySegments (Fase 40a)', () => {
  const at = (start: number, length: number) => ({ start, length });

  // WEB-DBQ-001
  it('sem achados devolve o texto inteiro num pedaço só, e texto vazio devolve nada', () => {
    expect(querySegments('SELECT 1', [])).toEqual([{ text: 'SELECT 1', findings: [] }]);
    expect(querySegments('', [at(0, 3)])).toEqual([]);
  });

  // WEB-DBQ-002
  it('marca o trecho do achado e deixa o resto sem marca', () => {
    const segments = querySegments('SELECT * FROM t', [at(7, 1)]);

    expect(segments).toEqual([
      { text: 'SELECT ', findings: [] },
      { text: '*', findings: [0] },
      { text: ' FROM t', findings: [] },
    ]);
  });

  // WEB-DBQ-003
  it('trechos sobrepostos dividem o texto nos pedaços com os índices certos', () => {
    // 0123456789
    // abcdefghij: achado 0 cobre c..g, achado 1 cobre e..i
    const segments = querySegments('abcdefghij', [at(2, 5), at(4, 5)]);

    expect(segments.map((s) => s.text)).toEqual(['ab', 'cd', 'efg', 'hi', 'j']);
    expect(segments.map((s) => s.findings)).toEqual([[], [0], [0, 1], [1], []]);
    expect(segments.map((s) => s.text).join('')).toBe('abcdefghij');
  });

  // WEB-DBQ-004
  it('corta start e length que passam do texto e ignora trecho vazio', () => {
    const segments = querySegments('abcde', [at(3, 50), at(-2, 4), at(10, 3), at(1, 0)]);

    expect(segments.map((s) => s.text).join('')).toBe('abcde');
    expect(segments.map((s) => s.text)).toEqual(['ab', 'c', 'de']);
    expect(segments.map((s) => s.findings)).toEqual([[1], [], [0]]);
  });
});

describe('escala de gravidade e rótulos (Fase 43a)', () => {
  // WEB-DBQ-005
  it('a gravidade segue a escala do FindingSeverity (baixa 0, média 1, alta 2) e todo tipo tem título', () => {
    expect([QuerySeverity.Low, QuerySeverity.Medium, QuerySeverity.High]).toEqual([0, 1, 2]);
    expect(QUERY_SEVERITY_LABEL[QuerySeverity.High]).toBe('Alta');
    expect(QUERY_SEVERITY_SLUG[QuerySeverity.Low]).toBe('low');
    const kinds = Object.values(QueryFindingKind).filter((v): v is QueryFindingKind => typeof v === 'number');
    expect(kinds.length).toBe(12);
    for (const kind of kinds) expect(QUERY_FINDING_TITLE[kind]).toBeTruthy();
  });
});
