import { fieldsText, filterBy } from './flow-insight';

describe('flow-insight (Fases 32 a 34)', () => {
  // WEB-INS-001
  it('filterBy_nuloEhTodosEValorFiltra', () => {
    const items = [{ k: 1 }, { k: 2 }, { k: 1 }];

    expect(filterBy(items, (i) => i.k, null).length).toBe(3);
    expect(filterBy(items, (i) => i.k, 1).length).toBe(2);
  });

  // WEB-INS-002
  it('fieldsText_listaOsCamposEDizEOutrosCamposQuandoHaMais', () => {
    expect(fieldsText({ fields: ['pedidoId', 'valor'] })).toBe('pedidoId, valor');
    expect(fieldsText({ fields: ['pedidoId'], moreFields: true })).toBe('pedidoId e outros campos');
    expect(fieldsText({ fields: null, moreFields: true })).toBe('outros campos');
    expect(fieldsText({ fields: [] })).toBeNull();
    expect(fieldsText(null)).toBeNull();
  });
});
