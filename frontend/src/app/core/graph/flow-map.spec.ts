import { FlowMap, MapBlock, MapBlockKind, MapLinkKind } from '../models';
import { MAP_BOX, blockTitle, callText, laneTitle, linkText, litLinks, mapGeometry, outcomeOfLink, pathForOutcome, pathLabel, pathNeighbors, placeRows } from './flow-map';
import { MAP_FIXTURE, MAP_FIXTURE_GRAPH } from './flow-map-fixture.spec';

const nodes = new Map(MAP_FIXTURE_GRAPH.nodes.map((n) => [n.id, n]));

describe('mapa do fluxo: posição (Fase 31)', () => {
  // WEB-MAP-001
  it('placeRows_umDepoisDoQueChegaEChamadaNaLinhaDeQuemChama', () => {
    const rows = placeRows(MAP_FIXTURE);

    expect(rows.get('b1')).toBe(0);
    expect(rows.get('b2')).toBe(1);
    expect(rows.get('b3')).toBe(2);
    // b4 é chamado por b3 (outra faixa): fica na linha dele. b5 é chamado por b4.
    expect(rows.get('b4')).toBe(2);
    expect(rows.get('b5')).toBe(2);
  });

  // WEB-MAP-002
  it('placeRows_doisBlocosNaMesmaFaixaNuncaDividemALinha', () => {
    const branches: FlowMap = {
      ...MAP_FIXTURE,
      blocks: [...MAP_FIXTURE.blocks.slice(0, 3), { id: 'b6', kind: MapBlockKind.Step, lane: ':principal', order: 6, title: 'Outro ramo', nodeId: 'n7', nodeIds: ['n7'] }],
      links: [...MAP_FIXTURE.links.slice(0, 2), { from: 'b2', to: 'b6', kind: MapLinkKind.Outcome, label: 'else' }],
    };
    const rows = placeRows(branches);

    expect(rows.get('b3')).toBe(2);
    expect(rows.get('b6')).toBe(3);
  });

  // WEB-MAP-003
  it('mapGeometry_blocoNaColunaDaFaixaEFaixasNaOrdemDoMapa', () => {
    const geometry = mapGeometry(MAP_FIXTURE, false);
    const column = (id: string) => geometry.blocks.find((p) => p.block.id === id)!.column;

    expect(geometry.lanes.map((l) => l.lane.id)).toEqual([':principal', 'api-pagamentos', ':fora']);
    expect([column('b1'), column('b3'), column('b4'), column('b5')]).toEqual([0, 0, 1, 2]);
    const b4 = geometry.blocks.find((p) => p.block.id === 'b4')!;
    expect(b4.x).toBe(MAP_BOX.pad + (MAP_BOX.width + MAP_BOX.gapX));
    expect(geometry.links.length).toBe(4);
    expect(geometry.links.every((l) => l.d.startsWith('M '))).toBeTrue();
  });

  // WEB-MAP-004
  it('mapGeometry_noCelularEUmaColunaSoUmBlocoPorLinha', () => {
    const geometry = mapGeometry(MAP_FIXTURE, true);

    expect(new Set(geometry.blocks.map((p) => p.column))).toEqual(new Set([0]));
    expect(new Set(geometry.blocks.map((p) => p.row)).size).toBe(MAP_FIXTURE.blocks.length);
    expect(geometry.lanes).toEqual([]);
    expect(geometry.width).toBeLessThan(mapGeometry(MAP_FIXTURE, false).width);
  });

  // WEB-MAP-005
  it('ligacao_chamadaTemVerboERotaEDesfechoTemOTextoDoRamo', () => {
    const geometry = mapGeometry(MAP_FIXTURE, false);

    expect(geometry.links.map((l) => l.text)).toEqual([null, 'Se sim', 'POST api/estornos', null]);
    expect(callText({ from: 'a', to: 'b', kind: MapLinkKind.Call, http: { method: 'GET', route: null } })).toBe('GET');
    expect(linkText(MAP_FIXTURE.links[1])).toBe('Se sim');
  });
});

describe('mapa do fluxo: textos (Fase 31)', () => {
  // WEB-MAP-006
  it('laneTitle_principalMostraORepositorioEForaMostraForaDoCodigo', () => {
    const [main, other, outside] = MAP_FIXTURE.lanes;

    expect(laneTitle(main, 'pedidos-web')).toBe('pedidos-web');
    expect(laneTitle(other, 'pedidos-web')).toBe('api-pagamentos');
    expect(laneTitle(outside, 'pedidos-web')).toBe('Fora do código');
  });

  // WEB-MAP-007
  it('blockTitle_usaONomeLegivelEACondicaoNaDecisaoESemNoUsaOTituloDaApi', () => {
    const byId = (id: string) => MAP_FIXTURE.blocks.find((b) => b.id === id)!;

    expect(blockTitle(byId('b1'), nodes)).toBe('Criar');
    expect(blockTitle(byId('b2'), nodes)).toBe('Se pagamento.Aprovado');
    expect(blockTitle(byId('b3'), new Map())).toBe('EstornoService.Estornar');
    const noNode: MapBlock = { ...byId('b2'), nodeId: 'fantasma' };
    expect(blockTitle(noNode, nodes)).toBe('Se pagamento.Aprovado');
  });
});

describe('mapa do fluxo: caminhos (Fase 31)', () => {
  // WEB-MAP-008
  it('litLinks_soAsLigacoesDoCaminhoEOramoEscolhido', () => {
    expect([...litLinks(MAP_FIXTURE, MAP_FIXTURE.paths[0])!]).toEqual([0, 1, 2, 3]);
    expect([...litLinks(MAP_FIXTURE, MAP_FIXTURE.paths[1])!]).toEqual([0]);
    expect(litLinks(MAP_FIXTURE, null)).toBeNull();
  });

  // WEB-MAP-009
  it('pathForOutcome_primeiroCaminhoQueFezAEscolha', () => {
    expect(pathForOutcome(MAP_FIXTURE, 'b2', 1)?.id).toBe('p2');
    expect(pathForOutcome(MAP_FIXTURE, 'b2', 0)?.id).toBe('p1');
    expect(pathForOutcome(MAP_FIXTURE, 'b2', 7)).toBeNull();
    expect(outcomeOfLink(MAP_FIXTURE, MAP_FIXTURE.links[1])).toBe(0);
    expect(outcomeOfLink(MAP_FIXTURE, MAP_FIXTURE.links[0])).toBe(-1);
  });

  // WEB-MAP-010
  it('pathLabel_textoDoDesfechoDeCadaDecisaoOuCaminhoN', () => {
    expect(pathLabel(MAP_FIXTURE, MAP_FIXTURE.paths[0], 0)).toBe('1. Se sim');
    expect(pathLabel(MAP_FIXTURE, MAP_FIXTURE.paths[1], 1)).toBe('2. Se não');
    expect(pathLabel(MAP_FIXTURE, { id: 'p3', blocks: ['b1'], choices: [], fails: false }, 2)).toBe('Caminho 3');
  });

  // WEB-MAP-011
  it('pathNeighbors_anteriorESeguinteNoCaminhoEscolhidoOuNoPrimeiroQuePassa', () => {
    expect(pathNeighbors(MAP_FIXTURE, MAP_FIXTURE.paths[0], 'b2')).toEqual({ path: MAP_FIXTURE.paths[0], previous: 'b1', next: 'b3' });
    // Sem caminho escolhido, o primeiro que passa pelo bloco.
    expect(pathNeighbors(MAP_FIXTURE, null, 'b3').previous).toBe('b2');
    // Caminho escolhido que não passa pelo bloco: sem vizinhos.
    expect(pathNeighbors(MAP_FIXTURE, MAP_FIXTURE.paths[1], 'b3')).toEqual({ path: null, previous: null, next: null });
    expect(pathNeighbors(MAP_FIXTURE, MAP_FIXTURE.paths[0], 'b5').next).toBeNull();
  });
});
