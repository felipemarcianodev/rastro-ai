import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import {
  EFMarkerType,
  F_CONNECTION_BUILDERS,
  FCanvasChangeEvent,
  FCanvasComponent,
  FCreateConnectionEvent,
  FFlowModule,
  FReassignConnectionEvent,
  FSelectionChangeEvent,
  FTriggerEvent,
  isOnFlowBackground,
  middleButtonEventTrigger,
  primaryButtonEventTrigger,
} from '@foblex/flow';
import { Latest } from '../../core/graph/latest';
import { StepMove, stepFrom } from '../../core/graph/keyboard';
import { COLLAPSED_PREFIX, collapsedOwner } from '../../core/graph/collapse';
import { SvgColors, httpLabel, toMermaid, toSvg } from '../../core/graph/export';
import {
  LAYOUT_DIRECTION_GLYPH,
  LAYOUT_DIRECTION_LABEL,
  LAYOUT_DIRECTION_SLUG,
  readLayoutDirection,
  saveLayoutDirection,
  LayoutDirection,
  NEXT_LAYOUT_DIRECTION,
  NodeBox,
} from '../../core/graph/layout';
import { FOLD_ACTION, FOLD_GLYPH, FoldState, foldStates } from '../../core/graph/branches';
import { NodeText, findGuards, outcomeText, withGuards } from '../../core/graph/guards';
import { plainLabel } from '../../core/graph/plain';
import {
  BLOCK_KIND_SLUG,
  FlowAxis,
  FlowBlock,
  OutlineItem,
  Port,
  SystemInfo,
  TimelineHead,
  TimelineRepeat,
  flowDrawing,
  timelineRoutes,
} from '../../core/graph/timeline-layout';
import { NODE_EFFECT_HINT, NODE_EFFECT_LABEL, NODE_EFFECT_SLUG, NodeEffect, effectOf, entryCodeOnly, humanName, splitLabel } from '../../core/graph/reading';
import { FlowSummary, SummaryItem, summarySentence } from '../../core/graph/summary';
import { TREE_CONNECTION, TreeConnectionBuilder } from '../../core/graph/tree-connection';
import { Trail } from '../../core/graph/trail';
import {
  FINDING_SEVERITY_LABEL,
  FINDING_SEVERITY_SLUG,
  FindingSeverity,
  CHANGE_KIND_LABEL,
  CHANGE_KIND_SLUG,
  EDGE_CONFIDENCE_SLUG,
  EdgeConfidence,
  FlowEdge,
  FlowGraph,
  FlowNode,
  NODE_KIND_GLYPH,
  NODE_KIND_LABEL,
  NODE_KIND_SLUG,
  NodeKind,
} from '../../core/models';

const MIN_READABLE_SCALE = 0.6;
const READABLE_SCALE = 0.85;
const FIT_PADDING = 80;
// Até quanto o ponteiro pode andar entre apertar e soltar para ainda contar como clique.
const CLICK_TOLERANCE_PX = 5;
const POSITIONS_KEY = 'rastro.positions.';
// Abaixo desta escala os rótulos das ligações viram ruído: somem.
const FAR_SCALE = 0.45;
// Passo do teclado (Alt+setas) ao mover um nó.
const KEY_STEP = 24;
const UNDO_RESET_MS = 8000;

const WALK_END: Record<StepMove, string> = {
  [StepMove.Forward]: 'Fim do caminho: este passo não chama nenhum outro.',
  [StepMove.Back]: 'Este é o começo do caminho.',
  [StepMove.Next]: 'Este passo não tem irmãos.',
  [StepMove.Previous]: 'Este passo não tem irmãos.',
};
// Espera a pessoa parar de digitar antes de mover a câmera até o resultado.
const MATCH_CENTER_DELAY_MS = 250;
const LEGEND_KEY = 'rastro.legendOpen';
const BRIEF_KEY = 'rastro.brief';
// Largura da etiqueta "banco", "mensagem" (Fase 23) no passo.
const EFFECT_PX = 76;
// Profundidade na cadeia de chamadas -> classe do passo: quanto mais fundo, mais leve (Fase 23).
const DEPTH_CLASS = ['', ' is-depth-1', ' is-depth-2'];

type Point = { x: number; y: number };

interface PlacedNode {
  node: FlowNode;
  box: NodeBox;
  // Rótulo em duas partes: o dono (que pode encolher com reticências) e o nome do método,
  // que nunca é cortado.
  owner: string;
  name: string;
  // Validação num passo só (Fase 19): o desfecho (throw, return, break) na ponta do passo.
  outcome?: { label: string; slug: string };
  // Linha do tempo (Fase 20): o passo aparece de novo aqui; este é o id do original.
  repeatOf?: string;
  // Fase 23: número de leitura (1, 2, 2.1), profundidade na cadeia de chamadas e o que o passo
  // faz no mundo.
  number?: string;
  depth: number;
  effect?: NodeEffect;
}

interface IndexedEdge {
  edge: FlowEdge;
  // Posição no array original (é o que a trilha usa).
  index: number;
}

@Component({
  selector: 'app-flow-canvas',
  imports: [FFlowModule],
  templateUrl: './flow-canvas.component.html',
  styleUrl: './flow-canvas.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: F_CONNECTION_BUILDERS, useValue: { [TREE_CONNECTION]: new TreeConnectionBuilder() } }],
  host: { '(document:keydown.space)': 'onSpace($event, true)', '(document:keyup.space)': 'onSpace($event, false)', '(window:blur)': 'panning.set(false)' },
})
export class FlowCanvasComponent {
  readonly graph = input.required<FlowGraph>();
  // Repositório do levantamento: nomeia o primeiro sistema no Mermaid exportado (Fase 25).
  readonly repository = input<string | undefined>(undefined);
  // Sistemas do levantamento (Fase 26): dão a linguagem de cada moldura.
  readonly systems = input<readonly SystemInfo[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly trail = input<Trail | null>(null);
  readonly selectNode = output<string | null>();
  // "Próximo não resolvido": seleção que não é escolha de destino de caminho.
  readonly jumpTo = output<string>();
  // Árvore (Fase 18): abrir ou fechar o detalhe de um método (o recolher da página).
  readonly toggleFold = output<string>();
  readonly frameworkCount = input(0);
  readonly showFramework = input(false);
  readonly toggleFramework = output<void>();
  // Modo de edição (Fase 7): portas aparecem, ligar arrastando e mover a ponta de uma ligação.
  readonly editing = input(false);
  readonly createEdge = output<{ from: string; to: string }>();
  readonly reassignEdge = output<{ edge: FlowEdge; from?: string; to?: string }>();
  // Menu de contexto do passo (botão direito, tecla de menu ou Shift+F10), em coordenadas da janela.
  readonly nodeMenu = output<{ nodeId: string; x: number; y: number }>();
  // Passo em foco ("ver somente este fluxo"): ao trocar, o recorte é enquadrado de novo.
  readonly focusId = input<string | null>(null);
  // Chave das posições arrastadas pelo usuário (levantamento e recorte): cada visão guarda a sua.
  readonly layoutKey = input<string | null>(null);
  // Busca no grafo: passos que batem e o atual (Enter na busca).
  readonly matchIds = input<Set<string> | null>(null);
  readonly currentMatch = input<string | null>(null);
  readonly openSearch = output<void>();
  // Passos com anotação do usuário ganham a marca ✎.
  readonly noteIds = input<ReadonlyMap<string, string>>(new Map());
  // Comparando dois levantamentos: o que entrou e o que saiu ganham cor própria.
  readonly comparing = input(false);
  // Resumo do fluxo (Fase 23), montado junto com o desenho, para a visão geral.
  readonly summaryChange = output<FlowSummary>();
  // Troca de desenho pelo botão: a página troca o canvas pelo desenho consolidado (Fase 28).
  readonly directionChange = output<LayoutDirection>();
  private readonly moved = signal<Record<string, Point>>({});
  private autoBoxes = new Map<string, NodeBox>();
  protected readonly hasMoved = computed(() => Object.keys(this.moved()).length > 0);
  // "Reorganizar" pode ser desfeito por alguns segundos.
  protected readonly undoReset = signal<Record<string, Point> | null>(null);
  private undoTimer?: ReturnType<typeof setTimeout>;
  protected readonly scale = signal(1);
  // Aviso ao leitor de tela quando a seta não tem para onde ir.
  protected readonly walkMessage = signal('');
  protected readonly legendOpen = signal(readLegendOpen());
  protected readonly direction = signal(canvasDirection(readLayoutDirection()));
  // Fase 21: a linha do tempo é a regra das três visões. A horizontal segue para a direita, com
  // os galhos descendo; a vertical e a árvore descem, com os galhos à direita. A árvore difere da
  // vertical só pela ligação: reta em ângulo, em vez de curva.
  protected readonly horizontal = computed(() => this.direction() === LayoutDirection.Horizontal);
  protected readonly tree = computed(() => this.direction() === LayoutDirection.Tree);
  private readonly axis = computed(() => (this.horizontal() ? FlowAxis.Right : FlowAxis.Down));
  protected readonly connectionType = computed(() => (this.tree() ? TREE_CONNECTION : 'bezier'));
  // Os comandos aparecem em português (Fases 20 e 21); o código fica no title e no painel do passo.
  // Fase 23: o método aparece pelo nome legível ("Get pending alerts"); a classe vai para baixo.
  private readonly text: NodeText = (node) =>
    plainLabel(node) ?? (node.kind === NodeKind.Method || node.kind === NodeKind.Entry ? humanName(splitLabel(node).name) : node.label);
  // Modo "Resumo" (Fase 23): só o código do método de entrada, com os métodos chamados fechados.
  protected readonly brief = signal(readBrief());
  protected readonly effectLabel = NODE_EFFECT_LABEL;
  protected readonly effectHint = NODE_EFFECT_HINT;
  protected readonly effectSlug = NODE_EFFECT_SLUG;
  protected readonly blockSlug = BLOCK_KIND_SLUG;
  private readonly effects = computed(
    () => new Map(this.graph().nodes.flatMap((node) => (effectOf(node) === undefined ? [] : [[node.id, effectOf(node)!] as const]))),
  );
  private readonly outcomeOf: NodeText = (node) => lowerFirst(plainLabel(node) ?? outcomeText(node));
  private readonly downward = computed(() => !this.horizontal());
  // Na edição e na comparação o consolidado não entra: só o canvas mostra o rascunho e as diferenças.
  protected readonly nextDirection = computed(() => {
    const next = NEXT_LAYOUT_DIRECTION[this.direction()];
    return next === LayoutDirection.Consolidated && (this.editing() || this.comparing()) ? NEXT_LAYOUT_DIRECTION[next] : next;
  });
  protected readonly directionLabel = LAYOUT_DIRECTION_LABEL;
  protected readonly directionGlyph = LAYOUT_DIRECTION_GLYPH;
  // Cada direção guarda o seu arranjo feito à mão.
  private readonly positionsKey = computed(() => {
    const key = this.layoutKey();
    const direction = this.direction();
    return key && direction !== LayoutDirection.Horizontal ? `${key}|${LAYOUT_DIRECTION_SLUG[direction]}` : key;
  });
  private lastDirection = this.direction();
  private lastBrief = readBrief();
  protected readonly far = computed(() => this.scale() < FAR_SCALE);
  private readonly layouts = new Latest();
  // Passo a centralizar quando o próximo layout chegar (recolher e expandir mudam as posições).
  private pendingCenter: string | null = null;
  private matchTimer?: ReturnType<typeof setTimeout>;

  // Marca no nó com achados de qualidade: quantos e a pior severidade.
  protected readonly findingBadges = computed(() => {
    const badges = new Map<string, { count: number; slug: string; title: string }>();
    const worst = new Map<string, FindingSeverity>();
    const counts = new Map<string, number>();
    for (const finding of this.graph().findings ?? []) {
      counts.set(finding.nodeId, (counts.get(finding.nodeId) ?? 0) + 1);
      worst.set(finding.nodeId, Math.max(worst.get(finding.nodeId) ?? 0, finding.severity));
    }
    for (const [id, count] of counts) {
      const severity = worst.get(id)!;
      badges.set(id, { count, slug: FINDING_SEVERITY_SLUG[severity], title: `${count} achado(s) de qualidade; o pior: ${FINDING_SEVERITY_LABEL[severity].toLowerCase()}` });
    }
    return badges;
  });
  private lastFocus: string | null = null;

  private readonly canvas = viewChild(FCanvasComponent);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private pickedInCanvas: string | null = null;
  // Seleção múltipla: arrastar no fundo desenha o laço; Ctrl+clique marca e desmarca (padrão do
  // Foblex). Os passos marcados se movem juntos. Mover a visão: Espaço+arrastar ou botão do meio.
  protected readonly panning = signal(false);
  protected readonly groupSize = signal(0);
  protected readonly selectionTrigger = (event: FTriggerEvent) => isSelectionGesture(event, this.panning());
  protected readonly canvasMoveTrigger = (event: FTriggerEvent) => isPanGesture(event, this.panning());
  private pointerStart: { x: number; y: number } | null = null;

  protected readonly placed = signal<PlacedNode[]>([]);
  protected readonly laidOut = signal(false);
  protected readonly markerEnd = EFMarkerType.END_ALL_STATES;
  protected readonly glyph = NODE_KIND_GLYPH;
  protected readonly kindLabel = NODE_KIND_LABEL;
  protected readonly changeLabel = CHANGE_KIND_LABEL;
  protected readonly EdgeConfidence = EdgeConfidence;
  protected readonly httpLabel = httpLabel;
  protected readonly collapsedPrefix = COLLAPSED_PREFIX;
  protected readonly NodeKind = NodeKind;
  private fitted = false;

  // Validação num passo só (Fase 19), fora do modo de edição (lá é preciso ligar no throw).
  // O desfecho fica separado quando tem marca própria: mudança, achado, anotação ou regra.
  protected readonly guards = computed(() => {
    if (this.editing()) return new Map<string, FlowNode>();
    const findings = new Set((this.graph().findings ?? []).map((f) => f.nodeId));
    const notes = this.noteIds();
    return findGuards(
      this.graph(),
      (node) => node.change !== undefined || !!node.rule || findings.has(node.id) || notes.has(node.id) || !!collapsedOwner(node.id),
    );
  });
  // Desfecho engolido -> a validação que o mostra.
  private readonly guardOf = computed(() => new Map([...this.guards()].map(([decision, outcome]) => [outcome.id, decision])));
  // O grafo como é desenhado (layout e exportação).
  private readonly display = computed(() => withGuards(this.graph(), this.guards(), this.text, this.outcomeOf));
  // O que vai para o desenho: tudo, ou só o código da entrada no modo "Resumo".
  private readonly drawn = computed(() => (this.brief() ? entryCodeOnly(this.display()) : this.display()));
  private readonly depths = signal<Map<string, number>>(new Map());
  // Cabeçalhos dos galhos ("Se sim", "Se não", "Caso ...").
  private readonly heads = signal<TimelineHead[]>([]);
  protected readonly shownHeads = computed(() => (this.hasMoved() ? [] : this.heads()));
  // Por onde cada ligação passa: "depois" (do passo anterior) ou "chama" (pela lateral).
  private readonly repeats = signal<Map<string, TimelineRepeat>>(new Map());
  private readonly repeatOf = computed(() => new Map([...this.repeats().values()].map((r) => [r.id, r.of])));
  private readonly routes = computed(() => timelineRoutes(this.graph(), this.repeats()));
  // Blocos do "try". Somem quando algum passo foi arrastado à mão: o fundo não acompanha o
  // arraste e ficaria fora do lugar.
  private readonly blocks = signal<FlowBlock[]>([]);
  protected readonly shownBlocks = computed(() => (this.hasMoved() ? [] : this.blocks()));

  // Passo que está na tela por este id: o desfecho engolido aparece na validação.
  private shown(id: string): string {
    return this.guardOf().get(id) ?? id;
  }

  // Só as ligações que entraram no desenho: sem as do desfecho engolido pela validação e, no modo
  // "Resumo", sem as que saem de um método fechado (o grafo desenhado guarda os mesmos objetos).
  protected readonly edges = computed<IndexedEdge[]>(() => {
    const drawn = new Set(this.drawn().edges);
    return this.graph()
      .edges.map((edge, index) => ({ edge, index }))
      .filter(({ edge }) => edge.from !== edge.to && drawn.has(edge));
  });

  protected readonly foldGlyph = FOLD_GLYPH;
  protected readonly foldAction = FOLD_ACTION;
  // Abrir e fechar o detalhe do método vale nos três desenhos.
  // No modo "Resumo" os métodos já estão fechados: o ▾/▸ some.
  protected readonly folds = computed(() => (this.brief() ? new Map<string, FoldState>() : foldStates(this.graph(), COLLAPSED_PREFIX)));

  // Clique no ▾/▸ do passo: não escolhe o passo nem começa arraste.
  protected onFold(event: Event, nodeId: string): void {
    event.stopPropagation();
    this.toggleFold.emit(nodeId);
  }

  // Teclado no passo: + abre e − fecha o detalhe do método.
  protected onFoldKey(event: KeyboardEvent, nodeId: string): void {
    const state = this.folds().get(nodeId);
    if (state === undefined) return;
    const wants = event.key === '+' ? FoldState.Closed : event.key === '-' ? FoldState.Open : null;
    if (wants === null || wants !== state) return;
    event.preventDefault();
    this.toggleFold.emit(nodeId);
  }

  // O rótulo do ramo vira cabeçalho do galho ou da seção do bloco: nenhuma etiqueta na ligação.
  protected readonly quietLabels = computed(() => new Set(this.graph().edges.flatMap((edge, index) => (edge.label ? [index] : []))));

  protected readonly holes = computed(() => this.graph().nodes.filter((n) => n.kind === NodeKind.Unresolved));

  constructor() {
    // Novo layout a cada mudança do grafo. Depois do primeiro, o canvas fica de pé e só os nós
    // mudam de lugar (editar não pode fazer a visão pular nem piscar).
    effect(() => {
      const graph = this.drawn();
      const guards = this.guards();
      const effects = this.effects();
      const text = this.text;
      const outcomeOf = this.outcomeOf;
      const originals = new Map(this.graph().nodes.map((n) => [n.id, n]));
      // Sem rastrear: o layout grava "placed" ao terminar, e ler o sinal aqui fazia o efeito
      // rodar de novo sem fim (a tela travava).
      if (untracked(() => this.placed().length) === 0) this.laidOut.set(false);
      const key = this.positionsKey();
      const direction = this.direction();
      const brief = this.brief();
      const drawing = flowDrawing(graph, this.axis(), (node) => (effects.has(node.id) ? EFFECT_PX : 0), this.systems());
      this.layouts.run(Promise.resolve(drawing.boxes), (boxes) => {
        this.autoBoxes = boxes;
        this.blocks.set(drawing.blocks);
        this.heads.set(drawing.heads);
        const repeats = drawing.repeats;
        this.repeats.set(repeats);
        this.depths.set(drawing.depths);
        this.summaryChange.emit(this.summaryOf(drawing.outline, graph, originals));
        const moved = readPositions(key);
        this.moved.set(moved);
        this.placed.set(
          graph.nodes
            .filter((n) => boxes.has(n.id))
            .map((shown): PlacedNode => {
              // A tela mostra o nó original; o do desenho só traz o texto completo para a largura.
              const node = originals.get(shown.id) ?? shown;
              const outcome = guards.get(node.id);
              const label = text(node);
              return {
                node,
                box: withPosition(boxes.get(node.id)!, moved[node.id]),
                ...(label === node.label ? splitLabel(node) : { owner: '', name: label }),
                outcome: outcome && { label: outcomeOf(outcome), slug: NODE_KIND_SLUG[outcome.kind] },
                number: drawing.numbers.get(node.id),
                depth: drawing.depths.get(node.id) ?? 0,
                effect: effects.get(node.id),
              };
            })
            .concat(
              [...repeats.values()]
                .filter((repeat) => boxes.has(repeat.id) && originals.has(repeat.of))
                .map((repeat) => {
                  const original = originals.get(repeat.of)!;
                  const label = text(original);
                  return {
                    node: { ...original, id: repeat.id },
                    box: withPosition(boxes.get(repeat.id)!, moved[repeat.id]),
                    ...(label === original.label ? splitLabel(original) : { owner: '', name: label }),
                    repeatOf: repeat.of,
                    number: drawing.numbers.get(repeat.id),
                    depth: drawing.depths.get(repeat.id) ?? 0,
                    effect: effects.get(repeat.of),
                  };
                }),
            ),
        );
        this.laidOut.set(true);
        if (this.pendingCenter) {
          const id = this.pendingCenter;
          this.pendingCenter = null;
          setTimeout(() => this.canvas()?.centerGroupOrNode(id, true), 50);
        }
        if (this.focusId() !== this.lastFocus || direction !== this.lastDirection || brief !== this.lastBrief) {
          this.lastFocus = this.focusId();
          this.lastDirection = direction;
          this.lastBrief = brief;
          // Espera o Foblex desenhar os nós novos antes de enquadrar.
          setTimeout(() => this.frame(), 50);
        }
      });
    });

    effect(() => {
      const id = this.currentMatch();
      if (!id || !this.laidOut()) return;
      clearTimeout(this.matchTimer);
      this.matchTimer = setTimeout(() => this.canvas()?.centerGroupOrNode(this.shown(id), true), MATCH_CENTER_DELAY_MS);
    });

    // Escolha feita fora do canvas (lista do painel, caminho, botão de buracos): traz o nó
    // para o centro. Clique no próprio canvas não mexe na visão.
    effect(() => {
      const id = this.selectedId();
      if (id && id !== this.pickedInCanvas && this.laidOut()) this.canvas()?.centerGroupOrNode(this.shown(id), true);
    });
  }

  // Classes calculadas uma vez por mudança de grafo, seleção ou trilha (não a cada ciclo do template).
  protected readonly nodeClasses = computed(() => {
    const trail = this.trail();
    const guards = this.guards();
    const picked = this.selectedId();
    const selected = picked && this.shown(picked);
    const found = this.matchIds();
    const matches = found && new Set([...found].map((id) => this.shown(id)));
    const now = this.currentMatch();
    const current = now && this.shown(now);
    const map = new Map<string, string>();
    for (const node of this.graph().nodes) {
      const classes = ['flow-node', `kind-${NODE_KIND_SLUG[node.kind]}`];
      if (guards.has(node.id)) classes.push('is-guard');
      if (matches?.has(node.id)) classes.push(node.id === current ? 'is-match is-match-current' : 'is-match');
      if (collapsedOwner(node.id)) classes.push('is-collapsed');
      if (node.change !== undefined) classes.push(`change-${CHANGE_KIND_SLUG[node.change]}`);
      if (node.id === selected) classes.push('is-selected');
      // O "+N passos" nunca apaga: é a porta para o que foi recolhido.
      if (trail && !collapsedOwner(node.id) && !matches?.has(node.id)) classes.push(trail.nodeIds.has(node.id) ? 'is-on-trail' : 'is-dim');
      map.set(node.id, classes.join(' '));
    }
    return map;
  });

  protected readonly edgeClasses = computed(() => {
    const trail = this.trail();
    const routes = this.routes();
    const depths = this.depths();
    const kinds = new Map(this.graph().nodes.map((n) => [n.id, n.kind]));
    return new Map(
      this.edges().map((item) => {
        const classes = [`edge-${EDGE_CONFIDENCE_SLUG[item.edge.confidence]}`];
        // Fase 23: a linha do código da entrada fica mais grossa; a chegada a um throw, vermelha.
        const route = routes.get(item.index);
        const target = route?.target.slice(route.target.indexOf(':') + 1);
        if (route && !route.source.startsWith(Port.Side) && target && depths.get(target) === 0) classes.push('edge-main');
        if (kinds.get(item.edge.to) === NodeKind.Throw) classes.push('edge-exit');
        if (item.edge.http) classes.push('is-http');
        if (item.edge.change !== undefined) classes.push(`edge-change-${CHANGE_KIND_SLUG[item.edge.change]}`);
        if (trail) classes.push(trail.edgeIndexes.has(item.index) ? 'edge-trail' : 'edge-dim');
        return [item.index, classes.join(' ')] as const;
      }),
    );
  });

  // Minimapa: não resolvidos e a trilha do passo escolhido, para achar o caminho de longe.
  protected readonly minimapClasses = computed(() => {
    const trail = this.trail();
    const matches = this.matchIds();
    const map = new Map<string, string>();
    for (const node of this.graph().nodes) {
      if (matches?.has(node.id)) map.set(node.id, 'is-match');
      else if (trail?.nodeIds.has(node.id)) map.set(node.id, 'is-on-trail');
      else if (node.kind === NodeKind.Unresolved) map.set(node.id, 'is-unresolved');
    }
    return map;
  });

  protected onCanvasChange(event: FCanvasChangeEvent): void {
    this.scale.set(event.scale);
  }

  // Uma parada de Tab no grafo (roving tabindex): o passo escolhido, ou a entrada.
  protected readonly tabStop = computed(() => {
    const picked = this.selectedId();
    const selected = picked && this.shown(picked);
    return selected && this.graph().nodes.some((n) => n.id === selected) ? selected : this.graph().entryNodeId;
  });

  // Setas andam pelo grafo seguindo as ligações; na vertical as direções giram junto.
  protected walk(event: Event, nodeId: string, key: 'left' | 'right' | 'up' | 'down'): void {
    const moves: Record<'left' | 'right' | 'up' | 'down', StepMove> = this.downward()
      ? { down: StepMove.Forward, up: StepMove.Back, right: StepMove.Next, left: StepMove.Previous }
      : { right: StepMove.Forward, left: StepMove.Back, down: StepMove.Next, up: StepMove.Previous };
    const move = moves[key];
    const step = stepFrom(this.graph(), nodeId, move, this.trail()?.path ?? []);
    // O desfecho engolido pela validação não está na tela: a seta não sai do lugar por ele.
    const target = step && this.shown(step) !== nodeId ? this.shown(step) : null;
    event.preventDefault();
    if (!target) {
      this.walkMessage.set(WALK_END[move]);
      return;
    }
    this.walkMessage.set('');
    this.pickedInCanvas = null;
    // Andar com as setas não é escolher: não vira o destino de "Caminhos a partir daqui".
    this.jumpTo.emit(target);
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`.flow-node[data-node-id="${CSS.escape(target)}"]`)?.focus({ preventScroll: true }));
  }

  // Alt+setas move o passo focado (quem não usa mouse também reorganiza).
  protected moveByKey(event: Event, item: PlacedNode, dx: number, dy: number): void {
    event.preventDefault();
    this.onNodeMoved(item.node.id, { x: item.box.x + dx * KEY_STEP, y: item.box.y + dy * KEY_STEP });
  }

  protected nodeAria(node: FlowNode): string {
    if (collapsedOwner(node.id)) return `Recolhido: ${node.label}. Enter para expandir`;
    if (this.repeatOf().has(node.id)) return `${this.kindLabel[node.kind]} de novo: ${node.label}. Enter vai até o original`;
    const outcome = this.guards().get(node.id);
    const parts = [`${this.kindLabel[node.kind]}: ${node.label}${outcome ? `, então ${outcome.label}` : ''}`];
    const effect = this.effects().get(node.id);
    if (effect !== undefined) parts.push(this.effectHint[effect]);
    if (node.change !== undefined) parts.push(this.changeLabel[node.change]);
    const badge = this.findingBadges().get(node.id);
    if (badge) parts.push(badge.title);
    if (this.noteIds().has(node.id)) parts.push('com anotação sua');
    if (this.matchIds()?.has(node.id)) parts.push('resultado da busca');
    const fold = this.folds().get(node.id);
    if (fold !== undefined) parts.push(fold === FoldState.Open ? 'detalhe aberto, tecla − fecha' : 'detalhe fechado, tecla + abre');
    return parts.join('. ');
  }

  // Legenda mostra os modos ligados agora (comparação, busca, recolhidos, anotações).
  protected readonly hasHttp = computed(() => this.graph().edges.some((e) => !!e.http));
  protected readonly hasCollapsed = computed(() => this.graph().nodes.some((n) => !!collapsedOwner(n.id)));

  protected meta(node: FlowNode): string {
    if (collapsedOwner(node.id)) return 'Clique para expandir';
    if (this.repeatOf().has(node.id)) return 'Já aparece acima: clique para ir';
    if (node.location) {
      const file = node.location.file.split(/[\\/]/).pop() ?? node.location.file;
      const where = `${file}:${node.location.line}`;
      // O método aparece pelo nome legível: a classe vem aqui, quando o arquivo não a diz.
      const owner = node.kind === NodeKind.Method || node.kind === NodeKind.Entry ? splitLabel(node).owner.replace(/\.$/, '') : '';
      const ownerClass = owner.split('.').pop() ?? '';
      return ownerClass && `${ownerClass}.cs` !== file ? `${ownerClass}, ${where}` : where;
    }
    return this.kindLabel[node.kind];
  }

  // O Foblex consome o clique do mouse (arraste do canvas): a seleção de nó chega por este evento.
  // Seleção vazia é ignorada: o Foblex também a dispara quando o usuário só arrasta o fundo para
  // mover a visão, e isso não pode apagar a trilha. Limpar fica com o clique de verdade no fundo.
  // Com vários passos marcados o painel fica no que já estava: o grupo serve para mover junto.
  protected onSelection(event: FSelectionChangeEvent): void {
    this.groupSize.set(event.nodeIds.length);
    const id = pickedFromSelection(event.nodeIds);
    if (!id) return;
    // Passo repetido da linha do tempo: escolhe o original e leva a visão até ele.
    const original = this.repeatOf().get(id);
    this.pickedInCanvas = original ? null : id;
    this.selectNode.emit(original ?? id);
  }

  // Classes do passo repetido: as do original, mais a marca de repetido.
  protected classesOf(item: PlacedNode): string {
    const classes = (this.nodeClasses().get(item.repeatOf ?? item.node.id) ?? '') + DEPTH_CLASS[Math.min(item.depth, DEPTH_CLASS.length - 1)];
    return item.repeatOf ? classes.replace(' is-selected', '') + ' is-repeat' : classes;
  }

  // Resumo do fluxo (Fase 23): a frase e a lista numerada, com o texto que a tela mostra.
  private summaryOf(outline: readonly OutlineItem[], drawn: FlowGraph, originals: ReadonlyMap<string, FlowNode>): FlowSummary {
    const shown = new Map(drawn.nodes.map((n) => [n.id, n]));
    const effects = this.effects();
    const textOf = (item: OutlineItem) => {
      const node = originals.get(item.of);
      return { text: shown.get(item.of)?.label ?? node?.label ?? item.of, kind: node?.kind ?? NodeKind.Method };
    };
    const toItem = (item: OutlineItem): SummaryItem => {
      const node = originals.get(item.of);
      const method = node && (node.kind === NodeKind.Method || node.kind === NodeKind.Entry);
      const effect = effects.get(item.of);
      return {
        id: item.of,
        number: item.number,
        text: textOf(item).text,
        ...(method ? { detail: splitLabel(node).owner.replace(/\.$/, '') } : {}),
        ...(effect !== undefined ? { effect: NODE_EFFECT_LABEL[effect] } : {}),
        ...(item.head ? { head: item.head } : {}),
        repeat: item.id !== item.of,
        children: item.children.map(toItem),
      };
    };
    return { sentence: summarySentence(outline, textOf), items: outline.map(toItem) };
  }

  protected toggleBrief(): void {
    const next = !this.brief();
    this.brief.set(next);
    try {
      localStorage.setItem(BRIEF_KEY, String(next));
    } catch {
      // Armazenamento bloqueado: a escolha vale só nesta visita.
    }
  }

  // Ligar arrastando de uma porta de saída até a porta de entrada de outro passo.
  protected onCreateConnection(event: FCreateConnectionEvent): void {
    if (!event.targetId) return;
    this.createEdge.emit({ from: event.sourceId.slice('out:'.length), to: event.targetId.slice('in:'.length) });
  }

  // Mover a ponta de uma ligação: o passo passa a ser chamado de outro lugar (ou a chamar outro).
  protected onReassignConnection(event: FReassignConnectionEvent): void {
    const item = this.edges().find((e) => `e${e.index}` === event.connectionId);
    if (!item) return;
    if (event.endpoint === 'source' && event.nextSourceId) this.reassignEdge.emit({ edge: item.edge, from: event.nextSourceId.slice('out:'.length) });
    if (event.endpoint === 'target' && event.nextTargetId) this.reassignEdge.emit({ edge: item.edge, to: event.nextTargetId.slice('in:'.length) });
  }

  // Focar um passo fora da tela (Tab, devolver o foco) faz o navegador rolar o f-flow, que tem
  // overflow escondido: a imagem descola do que o Foblex calcula. O grafo só se move pelo Foblex.
  protected keepUnscrolled(event: Event): void {
    const element = event.target as HTMLElement;
    if (element.scrollLeft !== 0 || element.scrollTop !== 0) element.scrollTo(0, 0);
  }

  protected onPointerDown(event: PointerEvent): void {
    this.pointerStart = { x: event.clientX, y: event.clientY };
  }

  // Clique sem arraste no fundo (fora de nó e de controle) limpa a seleção.
  protected onPointerUp(event: PointerEvent): void {
    const start = this.pointerStart;
    this.pointerStart = null;
    if (!start || Math.hypot(event.clientX - start.x, event.clientY - start.y) > CLICK_TOLERANCE_PX) return;
    if ((event.target as Element | null)?.closest('.flow-node, button, .f-minimap')) return;
    if (this.selectedId()) this.selectNode.emit(null);
  }

  // Espaço segurado vira a "mão": arrastar move a visão em vez de desenhar o laço. Em campo de
  // texto e no próprio passo (Espaço escolhe) a tecla segue o comportamento normal.
  protected onSpace(event: Event, down: boolean): void {
    if (!down) {
      this.panning.set(false);
      return;
    }
    const target = event.target;
    if (target instanceof Element && target.closest('input, textarea, select, button, .flow-node, [contenteditable]')) return;
    event.preventDefault();
    this.panning.set(true);
  }

  // Percorre os não resolvidos em ordem, a partir do selecionado (também pela contagem da barra).
  nextHole(): void {
    const holes = this.holes();
    if (holes.length === 0) return;
    const current = holes.findIndex((h) => h.id === this.selectedId());
    this.pickedInCanvas = null;
    this.jumpTo.emit(holes[(current + 1) % holes.length].id);
  }

  // Abre com o grafo inteiro quando ele cabe legível; se não, legível e centrado na entrada.
  // A escala de encaixe é calculada aqui (o getScale() logo após o fitToScreen ainda devolve a antiga).
  protected onFullRendered(): void {
    if (this.fitted) return;
    this.fitted = true;
    this.frame();
  }

  private frame(): void {
    const canvas = this.canvas();
    if (!canvas) return;
    if (this.fitScale() >= MIN_READABLE_SCALE) {
      canvas.fitToScreen({ x: FIT_PADDING, y: FIT_PADDING }, false, false, 1);
      this.syncScale();
      return;
    }
    canvas.setScale(READABLE_SCALE);
    const focus = this.focusId();
    if (focus) canvas.centerGroupOrNode(focus, false, false);
    else this.placeEntryAtStart(canvas, READABLE_SCALE);
    this.syncScale();
  }

  // Grafo grande abre legível com a entrada no canto onde a leitura começa (esquerda na
  // horizontal, topo na vertical), não no meio da tela.
  private placeEntryAtStart(canvas: FCanvasComponent, scale: number): void {
    const entry = this.placed().find((p) => p.node.id === this.graph().entryNodeId)?.box;
    if (!entry) {
      canvas.centerGroupOrNode(this.graph().entryNodeId, false, false);
      return;
    }
    canvas._setPosition({ x: FIT_PADDING - entry.x * scale, y: FIT_PADDING - entry.y * scale });
    canvas.redraw();
  }

  private fitScale(): number {
    const boxes = this.placed().map((p) => p.box);
    if (boxes.length === 0) return 1;
    const width = Math.max(...boxes.map((b) => b.x + b.width)) - Math.min(...boxes.map((b) => b.x));
    const height = Math.max(...boxes.map((b) => b.y + b.height)) - Math.min(...boxes.map((b) => b.y));
    const host = this.host.nativeElement;
    return Math.min((host.clientWidth - 2 * FIT_PADDING) / width, (host.clientHeight - 2 * FIT_PADDING) / height, 1);
  }

  // O usuário arrasta o passo: as ligações acompanham (o Foblex redesenha) e a posição fica guardada.
  protected onNodeMoved(id: string, point: Point): void {
    const moved = { ...this.moved(), [id]: { x: Math.round(point.x), y: Math.round(point.y) } };
    this.moved.set(moved);
    this.placed.update((items) => items.map((item) => (item.node.id === id ? { ...item, box: withPosition(item.box, moved[id]) } : item)));
    writePositions(this.positionsKey(), moved);
  }

  protected resetLayout(): void {
    this.undoReset.set(this.moved());
    clearTimeout(this.undoTimer);
    this.undoTimer = setTimeout(() => this.undoReset.set(null), UNDO_RESET_MS);
    this.applyPositions({});
  }

  protected undoResetLayout(): void {
    const previous = this.undoReset();
    clearTimeout(this.undoTimer);
    this.undoReset.set(null);
    if (previous) this.applyPositions(previous);
  }

  private applyPositions(positions: Record<string, Point>): void {
    this.moved.set(positions);
    writePositions(this.positionsKey(), positions);
    this.placed.update((items) =>
      items.map((item) => {
        const auto = this.autoBoxes.get(item.node.id) ?? item.box;
        return { ...item, box: withPosition(auto, positions[item.node.id]) };
      }),
    );
  }

  // Teclado (tecla de menu, Shift+F10) dispara contextmenu sem posição do ponteiro: o menu abre no nó.
  protected openMenu(event: MouseEvent, nodeId: string): void {
    event.preventDefault();
    let { clientX: x, clientY: y } = event;
    if (x === 0 && y === 0 && event.currentTarget instanceof Element) {
      const rect = event.currentTarget.getBoundingClientRect();
      x = rect.left + 16;
      y = rect.bottom;
    }
    this.pickedInCanvas = nodeId;
    this.nodeMenu.emit({ nodeId, x, y });
  }

  // Exportar o recorte: o que está na tela, com as posições atuais e as cores da paleta.
  exportSvg(): { svg: string; width: number; height: number } | null {
    const boxes = new Map(this.placed().map((p) => [p.node.id, p.box]));
    const style = getComputedStyle(this.host.nativeElement);
    const color = (name: string) => style.getPropertyValue(name).trim();
    const colors: SvgColors = {
      paper: color('--paper'),
      surface: color('--surface'),
      ink: color('--ink'),
      inkSoft: color('--ink-soft'),
      line: color('--line-strong'),
      edge: color('--edge'),
      inferred: color('--inferred'),
      confirmed: color('--confirmed'),
      unresolved: color('--unresolved'),
      http: color('--kind-decision'),
    };
    const svg = toSvg(this.display(), boxes, colors, this.direction(), { repeats: this.repeats(), heads: this.shownHeads() });
    if (!svg) return null;
    const size = /width="([\d.]+)" height="([\d.]+)"/.exec(svg);
    return { svg, width: Number(size?.[1] ?? 0), height: Number(size?.[2] ?? 0) };
  }

  exportMermaid(): string {
    return toMermaid(this.display(), this.direction(), this.repository());
  }

  centerAfterLayout(id: string): void {
    this.pendingCenter = id;
  }

  // O passo seguinte recebe a linha do anterior ("depois"); o primeiro passo de um galho recebe a
  // seta pela lateral de quem o abre ("chama" ou "entra"). Ver timelineRoutes.
  protected sourceId(item: IndexedEdge): string {
    return this.routes().get(item.index)?.source ?? Port.Side + item.edge.from;
  }

  protected targetId(item: IndexedEdge): string {
    return this.routes().get(item.index)?.target ?? Port.In + item.edge.to;
  }

  protected toggleDirection(): void {
    const next = this.nextDirection();
    saveLayoutDirection(next);
    // O consolidado é desenhado pela página, no lugar do canvas.
    if (next !== LayoutDirection.Consolidated) this.direction.set(next);
    this.directionChange.emit(next);
  }

  protected toggleLegend(): void {
    const next = !this.legendOpen();
    this.legendOpen.set(next);
    try {
      localStorage.setItem(LEGEND_KEY, String(next));
    } catch {
      // Armazenamento bloqueado: a escolha vale só nesta visita.
    }
  }

  protected zoomIn(): void {
    const canvas = this.canvas();
    if (canvas) canvas.setScale(Math.min(canvas.getScale() * 1.2, 2));
    this.syncScale();
  }

  protected zoomOut(): void {
    const canvas = this.canvas();
    if (canvas) canvas.setScale(Math.max(canvas.getScale() / 1.2, 0.1));
    this.syncScale();
  }

  protected fit(): void {
    this.canvas()?.fitToScreen({ x: 80, y: 80 }, true, false, 1);
    this.syncScale();
  }

  // Zoom feito por código não dispara fCanvasChange: lê a escala depois que o Foblex aplica.
  private syncScale(): void {
    setTimeout(() => {
      const canvas = this.canvas();
      if (canvas) this.scale.set(canvas.getScale());
    }, 50);
  }
}

// Laço de seleção: Shift+arrastar no fundo do grafo. Arrastar sem Shift move a visão, como
// antes da seleção múltipla: o dono sentiu falta de arrastar até um ponto para dar zoom.
export function isSelectionGesture(event: FTriggerEvent, panning: boolean): boolean {
  return !panning && primaryButtonEventTrigger(event) && withShift(event) && isOnFlowBackground(event);
}

// Mover a visão: botão principal sem Shift, botão do meio, ou o principal com o Espaço segurado.
export function isPanGesture(event: FTriggerEvent, panning: boolean): boolean {
  return middleButtonEventTrigger(event) || (primaryButtonEventTrigger(event) && (panning || !withShift(event)));
}

function withShift(event: FTriggerEvent): boolean {
  return 'shiftKey' in event && event.shiftKey === true;
}

// Um passo marcado vira o escolhido do painel; nenhum ou vários não mexem nele.
export function pickedFromSelection(nodeIds: readonly string[]): string | null {
  return nodeIds.length === 1 ? nodeIds[0] : null;
}

// Mora em core/graph/reading (a visão consolidada também usa); reexportada para quem já importa daqui.
export { splitLabel };

// "Para com erro X" na ponta da validação vira "para com erro X" (segue a condição).
function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1);
}

function withPosition(box: NodeBox, point: Point | undefined): NodeBox {
  return point ? { ...box, x: point.x, y: point.y } : box;
}

// Posições ficam só neste navegador (conveniência de leitura, não dado do levantamento).
function readPositions(key: string | null): Record<string, Point> {
  if (!key) return {};
  try {
    return JSON.parse(localStorage.getItem(POSITIONS_KEY + key) ?? '{}') as Record<string, Point>;
  } catch {
    return {};
  }
}

function writePositions(key: string | null, positions: Record<string, Point>): void {
  if (!key) return;
  try {
    if (Object.keys(positions).length === 0) localStorage.removeItem(POSITIONS_KEY + key);
    else localStorage.setItem(POSITIONS_KEY + key, JSON.stringify(positions));
  } catch {
    // Armazenamento bloqueado: as posições valem só nesta visita.
  }
}

function readLegendOpen(): boolean {
  try {
    return localStorage.getItem(LEGEND_KEY) !== 'false';
  } catch {
    return true;
  }
}

function readBrief(): boolean {
  try {
    return localStorage.getItem(BRIEF_KEY) === 'true';
  } catch {
    return false;
  }
}

// O canvas desenha três direções; o consolidado é outro componente (Fase 28) e, quando o canvas
// aparece com ele guardado (edição, comparação), começa no horizontal.
function canvasDirection(direction: LayoutDirection): LayoutDirection {
  return direction === LayoutDirection.Consolidated ? LayoutDirection.Horizontal : direction;
}
