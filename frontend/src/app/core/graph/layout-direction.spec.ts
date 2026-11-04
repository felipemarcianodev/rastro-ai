import { LayoutDirection, NEXT_LAYOUT_DIRECTION, nextDrawing, readLayoutDirection, saveLayoutDirection } from './layout';

describe('troca de desenho (Fases 28 e 31)', () => {
  afterEach(() => localStorage.removeItem('rastro.layoutDirection'));

  // WEB-DIR-001
  it('botao_passaPelosCincoDesenhosEVoltaAoHorizontal', () => {
    let direction: LayoutDirection = LayoutDirection.Horizontal;
    const seen: LayoutDirection[] = [direction];
    for (let i = 0; i < 5; i++) {
      direction = NEXT_LAYOUT_DIRECTION[direction];
      seen.push(direction);
    }

    expect(seen).toEqual([
      LayoutDirection.Horizontal,
      LayoutDirection.Timeline,
      LayoutDirection.Tree,
      LayoutDirection.Consolidated,
      LayoutDirection.Map,
      LayoutDirection.Horizontal,
    ]);
  });

  // WEB-DIR-003
  it('naEdicaoENaComparacao_oBotaoPulaOConsolidadoEOMapa', () => {
    expect(nextDrawing(LayoutDirection.Tree, true)).toBe(LayoutDirection.Horizontal);
    expect(nextDrawing(LayoutDirection.Tree, false)).toBe(LayoutDirection.Consolidated);
    expect(nextDrawing(LayoutDirection.Horizontal, true)).toBe(LayoutDirection.Timeline);
  });

  // WEB-DIR-002
  it('escolhaGuardada_voltaNaProximaVisitaEValorDesconhecidoEOHorizontal', () => {
    saveLayoutDirection(LayoutDirection.Consolidated);
    expect(readLayoutDirection()).toBe(LayoutDirection.Consolidated);

    saveLayoutDirection(LayoutDirection.Map);
    expect(readLayoutDirection()).toBe(LayoutDirection.Map);

    localStorage.setItem('rastro.layoutDirection', '42');
    expect(readLayoutDirection()).toBe(LayoutDirection.Horizontal);
  });
});
