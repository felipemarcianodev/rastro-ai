import { LayoutDirection, NEXT_LAYOUT_DIRECTION, readLayoutDirection, saveLayoutDirection } from './layout';

describe('troca de desenho (Fase 28)', () => {
  afterEach(() => localStorage.removeItem('rastro.layoutDirection'));

  // WEB-DIR-001
  it('botao_passaPelosQuatroDesenhosEVoltaAoHorizontal', () => {
    let direction: LayoutDirection = LayoutDirection.Horizontal;
    const seen: LayoutDirection[] = [direction];
    for (let i = 0; i < 4; i++) {
      direction = NEXT_LAYOUT_DIRECTION[direction];
      seen.push(direction);
    }

    expect(seen).toEqual([LayoutDirection.Horizontal, LayoutDirection.Timeline, LayoutDirection.Tree, LayoutDirection.Consolidated, LayoutDirection.Horizontal]);
  });

  // WEB-DIR-002
  it('escolhaGuardada_voltaNaProximaVisitaEValorDesconhecidoEOHorizontal', () => {
    saveLayoutDirection(LayoutDirection.Consolidated);
    expect(readLayoutDirection()).toBe(LayoutDirection.Consolidated);

    localStorage.setItem('rastro.layoutDirection', '42');
    expect(readLayoutDirection()).toBe(LayoutDirection.Horizontal);
  });
});
