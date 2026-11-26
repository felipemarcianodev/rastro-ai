import { BusinessEdge, BusinessFlow, BusinessKind, BusinessStep, LabelOrigin, LabelState } from '../models';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_LABEL_KEY, BUSINESS_KIND_SLUG, LABEL_ORIGIN_LABEL_KEY, pathTo } from './business';

// Fase 57: rótulos da visão de negócio e caminho da entrada até um passo.

function step(id: string, kind: BusinessKind = BusinessKind.Step, label = id): BusinessStep {
  return { id, nodeId: `n-${id}`, kind, label, labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: null, sourceNodeIds: [`n-${id}`], inferred: false };
}

function flowOf(steps: BusinessStep[], edges: [string, string, string?][]): BusinessFlow {
  return {
    surveyId: 's',
    schemaVersion: 1,
    collapsedDecisions: 0,
    steps,
    edges: edges.map(([from, to, label]): BusinessEdge => ({ from, to, label: label ?? null, inferred: false })),
  };
}

describe('business', () => {
  // WEB-BIZ-001
  it('tipos_todosTemSlugRotuloEGlifoDistintos', () => {
    const kinds = Object.values(BusinessKind).filter((v): v is BusinessKind => typeof v === 'number');
    expect(kinds.length).toBe(7);
    expect(new Set(kinds.map((k) => BUSINESS_KIND_SLUG[k])).size).toBe(7);
    expect(new Set(kinds.map((k) => BUSINESS_KIND_GLYPH[k])).size).toBe(7);
    expect(kinds.every((k) => BUSINESS_KIND_LABEL_KEY[k] === `business.kind.${BUSINESS_KIND_SLUG[k]}`)).toBeTrue();
    expect(BUSINESS_KIND_SLUG[BusinessKind.CrossSystem]).toBe('cross-system');
    expect(Object.values(LABEL_ORIGIN_LABEL_KEY).length).toBe(4);
  });

  // WEB-BIZ-002
  it('pathTo_devolveEntradaAteOPassoComORotuloDoRamo', () => {
    const flow = flowOf(
      [step('a', BusinessKind.Start), step('b', BusinessKind.Decision), step('c'), step('d', BusinessKind.End)],
      [['a', 'b'], ['b', 'c', 'Sim'], ['b', 'd', 'Não']],
    );

    const path = pathTo(flow, 'd');

    expect(path.map((p) => p.step.id)).toEqual(['a', 'b', 'd']);
    expect(path.map((p) => p.via)).toEqual([null, null, 'Não']);
  });

  // WEB-BIZ-003
  it('pathTo_escolheOCaminhoMaisCurtoEOPrimeiroNoEmpate', () => {
    const flow = flowOf(
      [step('a', BusinessKind.Start), step('b'), step('c'), step('d')],
      [['a', 'b', 'x'], ['a', 'c', 'y'], ['b', 'd'], ['c', 'd'], ['a', 'd', 'direto']],
    );

    expect(pathTo(flow, 'd').map((p) => p.step.id)).toEqual(['a', 'd']);
    expect(pathTo(flowOf(flow.steps, [['a', 'b', 'x'], ['a', 'c', 'y'], ['b', 'd'], ['c', 'd']]), 'd').map((p) => p.step.id)).toEqual(['a', 'b', 'd']);
  });

  // WEB-BIZ-004
  it('pathTo_comCicloTerminaEPassoDesconhecidoOuInalcancavelDaVazio', () => {
    const flow = flowOf([step('a', BusinessKind.Start), step('b'), step('c'), step('solto')], [['a', 'b'], ['b', 'a'], ['b', 'c']]);

    expect(pathTo(flow, 'c').map((p) => p.step.id)).toEqual(['a', 'b', 'c']);
    expect(pathTo(flow, 'a').map((p) => p.step.id)).toEqual(['a']);
    expect(pathTo(flow, 'solto')).toEqual([]);
    expect(pathTo(flow, 'nao-existe')).toEqual([]);
    expect(pathTo(flowOf([], []), 'a')).toEqual([]);
  });
});
