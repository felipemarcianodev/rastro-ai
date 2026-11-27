import { BusinessEdge, BusinessFlow, BusinessKind, BusinessStep, LabelOrigin, LabelState } from '../models';
import { BRANCH_LABEL_POSITION, BUSINESS_BOX, BUSINESS_TEXT, businessBoxSize, businessLayout, businessTextLines, edgeKey, flowNeighbour, snapToGrid, withSavedPositions } from './business-layout';

function step(id: string, kind: BusinessKind = BusinessKind.Step, label = id): BusinessStep {
  return { id, nodeId: `n-${id}`, kind, label, labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: null, sourceNodeIds: [`n-${id}`], inferred: false, codeChanged: false };
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

// Fase 57: o desenho da visão de negócio é determinístico, sem sobreposição e em grade de 10 px.

const flow = () =>
  flowOf(
    [
      step('a', BusinessKind.Start),
      step('b', BusinessKind.Decision, 'Está dentro do prazo?'),
      step('c', BusinessKind.Notification),
      step('d', BusinessKind.Rejection),
      step('e', BusinessKind.End),
    ],
    [['a', 'b'], ['b', 'c', 'Sim'], ['b', 'd', 'Não'], ['c', 'e'], ['d', 'e']],
  );

function overlaps(a: { x: number; y: number; width: number; height: number }, b: { x: number; y: number; width: number; height: number }): boolean {
  return a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
}

describe('businessLayout', () => {
  // WEB-BIZ-010
  it('camadas_sobemPelaMaiorDistanciaDaEntradaERamosFicamLadoALado', () => {
    const { boxes } = businessLayout(flow());
    const at = (id: string) => boxes.get(id)!;

    expect(at('a').y).toBeLessThan(at('b').y);
    expect(at('c').y).toBe(at('d').y);
    expect(at('c').y).toBeGreaterThan(at('b').y);
    expect(at('e').y).toBeGreaterThan(at('c').y);
    // Ordem da ligação: "Sim" (c) antes de "Não" (d), com 40 px entre as colunas.
    expect(at('c').x).toBeLessThan(at('d').x);
    expect(at('d').x - (at('c').x + at('c').width)).toBe(BUSINESS_BOX.gapX);
    expect(at('b').y - (at('a').y + at('a').height)).toBeGreaterThanOrEqual(BUSINESS_BOX.gapY);
  });

  // WEB-BIZ-011
  it('tamanhos_decisaoLosangoMaiorQuandoOTextoPassaDe40Caracteres', () => {
    const longo = 'Cliente ainda está dentro do prazo de arrependimento de sete dias?';
    const { boxes } = businessLayout(flowOf([step('a', BusinessKind.Start), step('b', BusinessKind.Decision, longo), step('c', BusinessKind.Decision, 'Prazo?')], [['a', 'b'], ['b', 'c']]));

    expect(boxes.get('a')).toEqual(jasmine.objectContaining(BUSINESS_BOX.step));
    expect(boxes.get('b')).toEqual(jasmine.objectContaining(BUSINESS_BOX.decisionLong));
    expect(boxes.get('c')).toEqual(jasmine.objectContaining(BUSINESS_BOX.decision));
  });

  // WEB-BIZ-040
  it('tamanho_textoCurtoFicaNoBalaoBase', () => {
    expect(businessBoxSize(step('a', BusinessKind.Step, 'Valida o pedido'))).toEqual(BUSINESS_BOX.step);
    expect(businessBoxSize(step('b', BusinessKind.Decision, 'Prazo?'))).toEqual(BUSINESS_BOX.decision);
  });

  // WEB-BIZ-041
  it('tamanho_textoLongoCresceAlturaPorLinhaEPassaDoTeto', () => {
    const medio = step('a', BusinessKind.Step, 'x'.repeat(100));
    const enorme = step('b', BusinessKind.Step, 'x'.repeat(2000));

    expect(businessTextLines(medio)).toBeGreaterThan(BUSINESS_TEXT.stepBaseLines);
    expect(businessBoxSize(medio).height).toBe(BUSINESS_BOX.step.height + (businessTextLines(medio) - BUSINESS_TEXT.stepBaseLines) * BUSINESS_TEXT.lineHeight);
    expect(businessTextLines(enorme)).toBe(BUSINESS_TEXT.maxLines);
    expect(businessBoxSize(enorme).height).toBe(BUSINESS_BOX.step.height + (BUSINESS_TEXT.maxLines - BUSINESS_TEXT.stepBaseLines) * BUSINESS_TEXT.lineHeight);
    expect(businessBoxSize(medio)).toEqual(businessBoxSize(step('c', BusinessKind.Step, 'x'.repeat(100))));
  });

  // WEB-BIZ-042
  it('tamanho_losangoCresceComTextoLongoESemSobreposicaoNoLayout', () => {
    const longo = step('b', BusinessKind.Decision, 'palavra '.repeat(30));
    const { boxes } = businessLayout(flowOf([step('a', BusinessKind.Start), longo, step('c', BusinessKind.Step, 'x'.repeat(200))], [['a', 'b'], ['b', 'c']]));

    expect(boxes.get('b')!.height).toBeGreaterThan(BUSINESS_BOX.decisionLong.height);
    expect(boxes.get('c')!.y).toBeGreaterThanOrEqual(boxes.get('b')!.y + boxes.get('b')!.height + BUSINESS_BOX.gapY);
  });

  // WEB-BIZ-012
  it('desenho_semSobreposicaoEmGradeDeDezPxDentroDoTamanhoTotal', () => {
    const { boxes, width, height } = businessLayout(flow());
    const all = [...boxes.values()];

    for (let i = 0; i < all.length; i++) {
      for (let j = i + 1; j < all.length; j++) expect(overlaps(all[i], all[j])).withContext(`${i} x ${j}`).toBeFalse();
    }
    for (const box of all) {
      expect(box.x % 10).toBe(0);
      expect(box.y % 10).toBe(0);
      expect(box.x + box.width).toBeLessThanOrEqual(width);
      expect(box.y + box.height).toBeLessThanOrEqual(height);
    }
  });

  // WEB-BIZ-013
  it('desenho_eDeterministico', () => {
    const first = businessLayout(flow());
    const second = businessLayout(flow());

    expect([...second.boxes]).toEqual([...first.boxes]);
    expect(second.width).toBe(first.width);
    expect(second.height).toBe(first.height);
  });

  // WEB-BIZ-014
  it('ciclo_ligacaoDeVoltaNaoEmpurraACamada', () => {
    const { boxes } = businessLayout(flowOf([step('a', BusinessKind.Start), step('b'), step('c')], [['a', 'b'], ['b', 'c'], ['c', 'b'], ['c', 'a']]));

    expect(boxes.get('a')!.y).toBeLessThan(boxes.get('b')!.y);
    expect(boxes.get('b')!.y).toBeLessThan(boxes.get('c')!.y);
    expect(boxes.size).toBe(3);
  });

  // WEB-BIZ-015
  it('passoSoltoEFluxoVazio_naoQuebram', () => {
    const solto = businessLayout(flowOf([step('a', BusinessKind.Start), step('x')], [['a', 'a']]));
    expect(solto.boxes.size).toBe(2);
    expect([...solto.boxes.values()].every((b) => b.y === 0)).toBeTrue();

    const vazio = businessLayout(flowOf([], []));
    expect(vazio.boxes.size).toBe(0);
    expect(vazio.width).toBe(0);
    expect(vazio.height).toBe(0);
  });
});

// Fase 58: posição salva, grade e ordem do fluxo.
describe('posição salva da visão de negócio', () => {
  // WEB-BIZ-020
  it('posicaoSalva_venceAAutomaticaEOPassoSemPosicaoFicaOndeEstava', () => {
    const automatic = businessLayout(flow());

    const moved = withSavedPositions(automatic, new Map([['c', { x: 500, y: 300 }]]));

    expect(moved.boxes.get('c')).toEqual({ ...automatic.boxes.get('c')!, x: 500, y: 300 });
    expect(moved.boxes.get('a')).toEqual(automatic.boxes.get('a')!);
    expect(moved.width).toBeGreaterThanOrEqual(500 + automatic.boxes.get('c')!.width);
    expect(moved.height).toBeGreaterThanOrEqual(300 + automatic.boxes.get('c')!.height);
    expect(withSavedPositions(automatic, new Map())).toBe(automatic);
  });

  // WEB-BIZ-021
  it('grade_encaixaNoMultiploDeDezMaisProximo', () => {
    expect(snapToGrid(123)).toBe(120);
    expect(snapToGrid(125)).toBe(130);
    expect(snapToGrid(-14)).toBe(-10);
    expect(snapToGrid(0)).toBe(0);
  });

  // WEB-BIZ-022
  it('vizinhoNoFluxo_andaPelaListaEParaNasPontas', () => {
    const order = ['a', 'b', 'c'];

    expect(flowNeighbour(order, 'a', 1)).toBe('b');
    expect(flowNeighbour(order, 'c', -1)).toBe('b');
    expect(flowNeighbour(order, 'c', 1)).toBeNull();
    expect(flowNeighbour(order, 'a', -1)).toBeNull();
    expect(flowNeighbour(order, null, 1)).toBe('a');
    expect(flowNeighbour([], 'a', 1)).toBeNull();
  });

  // WEB-BIZ-023
  it('chaveDaLigacao_distingueSaidaEChegada', () => {
    expect(edgeKey('a', 'b')).not.toBe(edgeKey('b', 'a'));
    expect(edgeKey('a', 'b')).toBe(edgeKey('a', 'b'));
  });

  // WEB-BIZ-024
  it('rotuloDoRamo_ficaPertoDaChegada', () => {
    expect(BRANCH_LABEL_POSITION).toBeGreaterThan(0.5);
    expect(BRANCH_LABEL_POSITION).toBeLessThan(1);
  });
});
