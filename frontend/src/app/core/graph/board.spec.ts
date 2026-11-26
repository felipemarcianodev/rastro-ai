import { BoardDetail, BoardLane, BoardLayout, BusinessFlow, BusinessKind, BusinessStep, LabelOrigin, LabelState, SurveyStatus } from '../models';
import { glossaryEntries, laneLayout, laneRunning, laneUpdates, legendKinds, moveLane, orderedLanes } from './board';

const step = (id: string, kind: BusinessKind): BusinessStep => ({
  id,
  nodeId: `n-${id}`,
  kind,
  label: id,
  labelOrigin: LabelOrigin.Rule,
  labelState: LabelState.Confirmed,
  ref: null,
  sourceNodeIds: [],
  inferred: false,
  codeChanged: false,
});

const flow = (kinds: BusinessKind[]): BusinessFlow => ({ surveyId: 's', schemaVersion: 1, collapsedDecisions: 0, steps: kinds.map((k, i) => step(`p${i}`, k)), edges: [] });

const lane = (surveyId: string, order: number, extra: Partial<BoardLane> = {}): BoardLane => ({
  surveyId,
  order,
  title: `Faixa ${surveyId}`,
  entryPoint: 'A.B',
  repositories: ['loja'],
  status: SurveyStatus.Completed,
  stale: false,
  flow: null,
  ...extra,
});

describe('board (Fase 60)', () => {
  // WEB-BOARD-001
  it('laneLayout_separaAPosicaoPorFaixaESemOIdDoLevantamento', () => {
    const layout: BoardLayout = {
      steps: [
        { laneSurveyId: 'a', stepId: 'p0', x: 10, y: 20 },
        { laneSurveyId: 'b', stepId: 'p0', x: 30, y: 40 },
      ],
      edges: [
        { laneSurveyId: 'a', from: 'p0', to: 'p1', points: [{ x: 1, y: 2 }] },
        { laneSurveyId: 'b', from: 'p0', to: 'p1', points: [] },
      ],
    };

    expect(laneLayout(layout, 'a')).toEqual({ steps: [{ stepId: 'p0', x: 10, y: 20 }], edges: [{ from: 'p0', to: 'p1', points: [{ x: 1, y: 2 }] }] });
    expect(laneLayout(layout, 'z')).toEqual({ steps: [], edges: [] });
  });

  // WEB-BOARD-002
  it('orderedLanes_ordenaPelaOrdemDoQuadro', () => {
    const board = { lanes: [lane('b', 1), lane('a', 0), lane('c', 2)] } as BoardDetail;

    expect(orderedLanes(board).map((l) => l.surveyId)).toEqual(['a', 'b', 'c']);
  });

  // WEB-BOARD-003
  it('moveLane_andaUmaCasaENasPontasNaoMuda', () => {
    const list = ['a', 'b', 'c'];

    expect(moveLane(list, 0, 1)).toEqual(['b', 'a', 'c']);
    expect(moveLane(list, 2, -1)).toEqual(['a', 'c', 'b']);
    expect(moveLane(list, 0, -1)).toEqual(list);
    expect(moveLane(list, 2, 1)).toEqual(list);
    expect(moveLane(list, 0, 2)).toEqual(['b', 'c', 'a']);
  });

  // WEB-BOARD-004
  it('laneUpdates_numeraAsFaixasNaOrdemRecebida', () => {
    expect(laneUpdates([lane('b', 5), lane('a', 9)])).toEqual([
      { surveyId: 'b', order: 0, title: 'Faixa b' },
      { surveyId: 'a', order: 1, title: 'Faixa a' },
    ]);
  });

  // WEB-BOARD-005
  it('legendKinds_listaSoOsTiposQueAparecemNaOrdemFixa', () => {
    const lanes = [lane('a', 0, { flow: flow([BusinessKind.End, BusinessKind.Start]) }), lane('b', 1, { flow: flow([BusinessKind.Decision, BusinessKind.End]) }), lane('c', 2)];

    expect(legendKinds(lanes)).toEqual([BusinessKind.Start, BusinessKind.Decision, BusinessKind.End]);
    expect(legendKinds([])).toEqual([]);
  });

  // WEB-BOARD-006
  it('laneRunning_soNaFilaOuAnalisando', () => {
    expect(laneRunning(lane('a', 0, { status: SurveyStatus.Pending }))).toBeTrue();
    expect(laneRunning(lane('a', 0, { status: SurveyStatus.Running }))).toBeTrue();
    expect(laneRunning(lane('a', 0, { status: SurveyStatus.Completed }))).toBeFalse();
    expect(laneRunning(lane('a', 0, { status: SurveyStatus.Failed }))).toBeFalse();
  });

  // WEB-BOARD-007
  it('glossaryEntries_contaSoAsLinhasNoFormatoTermoSignificado', () => {
    const text = 'Pedido: compra do cliente\nsem formato\n: sem termo\nSKU:\nCarrinho: itens: reservados';

    expect(glossaryEntries(text)).toEqual([
      { term: 'Pedido', meaning: 'compra do cliente' },
      { term: 'Carrinho', meaning: 'itens: reservados' },
    ]);
  });
});
