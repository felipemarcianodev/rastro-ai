import { LayoutDirection, readLayoutDirection, saveLayoutDirection } from './layout';

describe('troca de desenho (Fases 28 e 31)', () => {
  afterEach(() => localStorage.removeItem('rastro.layoutDirection'));

  // WEB-DIR-002
  it('escolhaGuardada_voltaNaProximaVisitaEValorDesconhecidoEOHorizontal', () => {
    saveLayoutDirection(LayoutDirection.Consolidated);
    expect(readLayoutDirection()).toBe(LayoutDirection.Consolidated);

    saveLayoutDirection(LayoutDirection.Map);
    expect(readLayoutDirection()).toBe(LayoutDirection.Map);

    saveLayoutDirection(LayoutDirection.Business);
    expect(readLayoutDirection()).toBe(LayoutDirection.Business);

    localStorage.setItem('rastro.layoutDirection', '42');
    expect(readLayoutDirection()).toBe(LayoutDirection.Horizontal);
  });
});
