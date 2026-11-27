import { ChangeKind, EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { addStep, connect, reassign, removeEdge, removeStep } from './draft-editor';
import { graphToDrawio } from './drawio';
import { drawioToDraft } from './drawio-draft';

// Fase 67: a mudança feita pelo draw.io dá o mesmo rascunho que a tela de edição. entry -> a -> b ; entry -> c ; b -> f (outro sistema).
const base = (): FlowGraph => ({
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: [
    { id: 'entry', kind: NodeKind.Entry, label: 'Run' },
    { id: 'a', kind: NodeKind.Method, label: 'A' },
    { id: 'b', kind: NodeKind.Method, label: 'B' },
    { id: 'c', kind: NodeKind.Method, label: 'C' },
    { id: 'f', kind: NodeKind.Method, label: 'Outro', system: 'api-pagamentos' },
  ],
  edges: [
    { from: 'entry', to: 'a', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'a', to: 'b', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'c', order: 2, confidence: EdgeConfidence.Extracted },
    { from: 'b', to: 'f', order: 1, confidence: EdgeConfidence.Extracted },
  ],
});

const boxes = new Map(base().nodes.map((n, i) => [n.id, { x: 100 + i * 250, y: 100, width: 200, height: 60 }]));
const generated = (): string => graphToDrawio(base(), boxes, 'repo', 'fluxo');
// Células do XML gerado: n0 entry, n1 a, n2 b, n3 c, n4 f.
const CELL: Record<string, string> = { entry: 'n0', a: 'n1', b: 'n2', c: 'n3', f: 'n4' };

function edit(xml: string, change: (doc: Document, root: Element) => void): string {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  change(doc, doc.getElementsByTagName('root')[0]);
  return new XMLSerializer().serializeToString(doc);
}

function addBox(label: string, id: string, style = 'rounded=1;whiteSpace=wrap;html=1;'): (doc: Document, root: Element) => void {
  return (doc, root) => {
    const cell = doc.createElement('mxCell');
    cell.setAttribute('id', id);
    cell.setAttribute('value', label);
    cell.setAttribute('style', style);
    cell.setAttribute('vertex', '1');
    cell.setAttribute('parent', '1');
    root.appendChild(cell);
  };
}

function addLine(id: string, source: string, target: string): (doc: Document, root: Element) => void {
  return (doc, root) => {
    const cell = doc.createElement('mxCell');
    cell.setAttribute('id', id);
    cell.setAttribute('style', 'edgeStyle=orthogonalEdgeStyle;');
    cell.setAttribute('edge', '1');
    cell.setAttribute('parent', '1');
    cell.setAttribute('source', source);
    cell.setAttribute('target', target);
    root.appendChild(cell);
  };
}

function lineOf(root: Element, from: string, to: string): Element {
  return Array.from(root.querySelectorAll('mxCell[edge="1"]')).find((e) => e.getAttribute('source') === CELL[from] && e.getAttribute('target') === CELL[to])!;
}

// O rascunho sem o id aleatório do passo novo (vira N1, N2... na ordem), para comparar dois caminhos.
function normalized(draft: FlowGraph): unknown {
  const fresh = draft.nodes.filter((n) => n.id.startsWith('new:')).map((n) => n.id);
  const name = (id: string): string => (fresh.includes(id) ? `N${fresh.indexOf(id) + 1}` : id);
  return {
    nodes: draft.nodes.map((n) => ({ id: name(n.id), kind: n.kind, label: n.label, change: n.change, intent: n.intent })),
    edges: draft.edges.map((e) => ({ from: name(e.from), to: name(e.to), order: e.order, label: e.label, change: e.change, confidence: e.confidence })),
  };
}

function viaDrawio(change: (doc: Document, root: Element) => void, draft = base()) {
  const loaded = generated();
  return drawioToDraft(draft, loaded, edit(loaded, change), new Map());
}

describe('drawioToDraft', () => {
  // CORE-DRIO-040
  it('balaoNovoLigadoAUmExistente_dahOMesmoRascunhoDaTelaDeEdicao', () => {
    const screen = addStep(base(), 'Validar', NodeKind.Method, '');
    const expected = connect(screen.draft, 'entry', screen.id);

    const result = viaDrawio((doc, root) => {
      addBox('Validar', 'x1')(doc, root);
      addLine('x2', CELL['entry'], 'x1')(doc, root);
    });

    expect(normalized(result.draft)).toEqual(normalized(expected));
    expect(result.created.length).toBe(1);
    expect(result.known.get('x1')).toBe(result.created[0]);
    expect(result.draft.nodes.find((n) => n.id === result.created[0])!.change).toBe(ChangeKind.Added);
    expect(result.draft.edges.find((e) => e.to === result.created[0])!.change).toBe(ChangeKind.Added);
    expect(result.refused).toBe(0);
  });

  // CORE-DRIO-041
  it('balaoApagado_dahOMesmoRascunhoDeTirarOPassoNaTela', () => {
    const expected = removeStep(base(), 'a');

    // O draw.io apaga junto as ligações do balão.
    const result = viaDrawio((doc, root) => {
      const lines = [lineOf(root, 'entry', 'a'), lineOf(root, 'a', 'b'), lineOf(root, 'a', 'c')];
      for (const line of lines) line.remove();
      doc.querySelector('UserObject[id="n1"]')!.remove();
    });

    expect(normalized(result.draft)).toEqual(normalized(expected));
    expect(result.draft.nodes.find((n) => n.id === 'a')!.change).toBe(ChangeKind.Removed);
    expect(result.refused).toBe(0);
  });

  // CORE-DRIO-042
  it('ligacaoMovida_dahOMesmoRascunhoDeArrastarAPontaNaTela', () => {
    const edge = base().edges[1];
    const expected = reassign(base(), edge, { from: 'entry' });

    const result = viaDrawio((_doc, root) => lineOf(root, 'a', 'b').setAttribute('source', CELL['entry']));

    expect(normalized(result.draft)).toEqual(normalized(expected));
  });

  // CORE-DRIO-043
  it('ligacaoApagada_marcaALigacaoDoCodigoComoRemovida_mesmoQuandoALinhaSaiDoPassoAnterior', () => {
    // O passo c é o segundo filho da entrada: a linha desenhada sai de a (o passo anterior), não da entrada.
    const result = viaDrawio((_doc, root) => lineOf(root, 'a', 'c').remove());

    expect(normalized(result.draft)).toEqual(normalized(removeEdge(base(), base().edges[2])));
    expect(result.draft.edges.find((e) => e.from === 'entry' && e.to === 'c')!.change).toBe(ChangeKind.Removed);
    expect(result.draft.nodes.every((n) => n.change === undefined)).toBeTrue();
  });

  // CORE-DRIO-044
  it('ligacaoNovaEntreDoisExistentes_viraChamadaNova', () => {
    const result = viaDrawio(addLine('x1', CELL['c'], CELL['b']));

    expect(normalized(result.draft)).toEqual(normalized(connect(base(), 'c', 'b')));
  });

  // CORE-DRIO-045
  it('balaoComLosango_viraDecisao_eSemLosangoViraMetodo_comOTextoSemHtml', () => {
    const result = viaDrawio((doc, root) => {
      addBox('Pedido <b>válido</b>?', 'x1', 'rhombus;whiteSpace=wrap;html=1;')(doc, root);
      addBox('Enviar', 'x2')(doc, root);
    });

    const [decision, step] = result.created.map((id) => result.draft.nodes.find((n) => n.id === id)!);
    expect(decision.kind).toBe(NodeKind.Decision);
    expect(decision.label).toBe('Pedido válido?');
    expect(step.kind).toBe(NodeKind.Method);
    expect(step.intent).toBe('');
  });

  // CORE-DRIO-046
  it('passoNovoLigadoAOutroPassoNovo_eLigacaoDoPassoNovoParaOExistente_entram', () => {
    const result = viaDrawio((doc, root) => {
      addBox('Um', 'x1')(doc, root);
      addBox('Dois', 'x2')(doc, root);
      addLine('x3', 'x1', 'x2')(doc, root);
      addLine('x4', 'x2', CELL['c'])(doc, root);
    });

    const [one, two] = result.created;
    expect(result.draft.edges.filter((e) => e.change === ChangeKind.Added).map((e) => `${e.from === one ? 'um' : e.from === two ? 'dois' : e.from}>${e.to === two ? 'dois' : e.to}`)).toEqual(['um>dois', 'dois>c']);
  });

  // CORE-DRIO-047
  it('apagarOBalaoDeEntrada_recusa_eOPassoNaoSai', () => {
    const result = viaDrawio((doc, root) => {
      lineOf(root, 'entry', 'a').remove();
      doc.querySelector('UserObject[id="n0"]')!.remove();
    });

    expect(result.refused).toBe(1);
    expect(result.entryRefused).toBe(1);
    expect(result.draft.nodes.every((n) => n.change === undefined)).toBeTrue();
    expect(result.draft.edges.every((e) => e.change === undefined)).toBeTrue();
  });

  // CORE-DRIO-048
  it('passoDeOutroSistema_balaoELigacao_saoRecusados', () => {
    const removed = viaDrawio((doc, root) => {
      lineOf(root, 'b', 'f').remove();
      doc.querySelector('UserObject[id="n4"]')!.remove();
    });
    expect(removed.foreignRefused).toBe(1);
    expect(removed.draft.nodes.find((n) => n.id === 'f')!.change).toBeUndefined();

    const linked = viaDrawio(addLine('x1', CELL['c'], CELL['f']));
    expect(linked.foreignRefused).toBe(1);
    expect(linked.draft.edges.length).toBe(base().edges.length);

    const unlinked = viaDrawio((_doc, root) => lineOf(root, 'b', 'f').remove());
    expect(unlinked.foreignRefused).toBe(1);
    expect(unlinked.draft.edges.find((e) => e.to === 'f')!.change).toBeUndefined();
  });

  // CORE-DRIO-049
  it('autosavesEmSequencia_reconhecemOBalaoCriadoAntes_eApagarOBalaoNovoOTiraDoRascunho', () => {
    const loaded = generated();
    const first = edit(loaded, addBox('Validar', 'x1'));
    const created = drawioToDraft(base(), loaded, first, new Map());
    expect(created.created.length).toBe(1);

    // O mesmo balão no autosave seguinte não vira outro passo; ligar e depois apagar o balão sai do rascunho.
    const linked = drawioToDraft(created.draft, first, edit(first, addLine('x2', CELL['a'], 'x1')), created.known);
    expect(linked.created.length).toBe(0);
    expect(linked.draft.edges.some((e) => e.to === created.created[0])).toBeTrue();

    const second = edit(first, addLine('x2', CELL['a'], 'x1'));
    const gone = drawioToDraft(linked.draft, second, edit(second, (_doc, root) => {
      root.querySelector('mxCell[id="x2"]')!.remove();
      root.querySelector('mxCell[id="x1"]')!.remove();
    }), linked.known);
    expect(gone.draft.nodes.some((n) => n.id === created.created[0])).toBeFalse();
    expect(gone.draft.edges.some((e) => e.to === created.created[0])).toBeFalse();
    expect(gone.known.size).toBe(0);
  });

  // CORE-DRIO-050
  it('xmlIgual_xmlSemRaiz_ouBalaoSemTexto_naoMudamONada', () => {
    const loaded = generated();
    const draft = base();

    expect(drawioToDraft(draft, loaded, loaded, new Map()).draft).toBe(draft);
    expect(drawioToDraft(draft, loaded, 'comprimido', new Map()).draft).toBe(draft);
    expect(viaDrawio(addBox('', 'x1')).created.length).toBe(0);
  });

  // Visto no navegador com o draw.io de verdade: a caixa nasce vazia (um autosave) e ganha o texto no seguinte; a seta
  // pode ser desenhada antes do texto. Os dois precisam entrar no rascunho.
  it('caixaQueNasceVaziaEGanhaTextoDepois_eSetaDesenhadaAntes_entramNoRascunho', () => {
    const loaded = generated();
    const empty = edit(loaded, (doc, root) => {
      addBox('', 'novo-1')(doc, root);
      addLine('linha-1', CELL['a'], 'novo-1')(doc, root);
    });
    const first = drawioToDraft(base(), loaded, empty, new Map());
    expect(first.created).toEqual([]);
    expect(first.draft.nodes.length).toBe(base().nodes.length);

    const labeled = empty.replace('id="novo-1" value=""', 'id="novo-1" value="Validar cadastro"');
    expect(labeled).not.toBe(empty);
    const second = drawioToDraft(first.draft, empty, labeled, first.known);
    expect(second.created.length).toBe(1);
    const created = second.draft.nodes.find((n) => n.id === second.created[0])!;
    expect(created.label).toBe('Validar cadastro');
    expect(created.change).toBe(ChangeKind.Added);
    expect(second.draft.edges.some((e) => e.from === 'a' && e.to === created.id && e.change === ChangeKind.Added)).toBeTrue();

    // O autosave seguinte, sem mudança, não cria de novo nem liga de novo.
    const third = drawioToDraft(second.draft, labeled, labeled, second.known);
    expect(third.created).toEqual([]);
    expect(third.draft).toEqual(second.draft);
  });
});
