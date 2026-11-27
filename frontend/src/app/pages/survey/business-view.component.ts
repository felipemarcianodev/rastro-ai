import { plainBranch } from '../../core/graph/plain';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { EFMarkerType, FCanvasComponent, FConnectionWaypointsChangedEvent, FFlowModule, FTriggerEvent, middleButtonEventTrigger, primaryButtonEventTrigger, provideFFlow, withControlScheme } from '@foblex/flow';
import { DesktopBridge } from '../../core/desktop-bridge.service';
import { shortCommit } from '../../core/format';
import { LanguageService } from '../../core/i18n/i18n';
import { flowSummaryOf } from '../../core/graph/consolidated';
import { AiStatusService } from '../../core/ai-status.service';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_LABEL_KEY, BUSINESS_KIND_ORDER, BUSINESS_KIND_SLUG, LABEL_ORIGIN_LABEL_KEY, pathTo } from '../../core/graph/business';
import { BRANCH_LABEL_POSITION, BUSINESS_BOX, BusinessGeometry, LayoutPoint, businessLayout, businessTextLines, edgeKey, flowNeighbour, snapToGrid, withSavedPositions } from '../../core/graph/business-layout';
import { BusinessExportData, businessToMermaid, businessToSvg, readBusinessColors } from '../../core/graph/business-export';
import { businessToDrawio } from '../../core/graph/drawio';
import { HostLinkOf, fillLinkTemplate } from '../../core/graph/link-template';
import { LayoutDirection } from '../../core/graph/layout';
import { DrawingSwitcherComponent } from './drawing-switcher.component';
import { FlowSummary, readingLabel } from '../../core/graph/summary';
import { AiUse, BusinessFlow, BusinessKind, BusinessLayout, BusinessStep, CodeRef, LabelOrigin, LabelState, RepositoryHostKind, RepositoryLinks, SurveyDetail, aiCan } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { Observable } from 'rxjs';
import { ConfirmDialogComponent } from './confirm-dialog.component';

// Zoom da visão de negócio: mesmos limites e passo do canvas do grafo.
export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 1.4;
const ZOOM_STEP = 1.2;
const FIT_PADDING = 40;
// O zoom feito por código só chega ao Foblex depois: a escala é lida em seguida.
const SYNC_SCALE_DELAY_MS = 50;
// Posição salva (Fase 58): espera a pessoa parar de mexer antes de gravar, e grava tudo num lote só.
export const SAVE_DEBOUNCE_MS = 500;
// Até onde o ponteiro anda entre apertar e soltar para ainda contar como clique no balão (abre o painel).
export const CLICK_TOLERANCE_PX = 4;
// Folga do balão em relação à área visível: dentro dela ele já está à vista.
const REVEAL_MARGIN_PX = 8;
const NO_BENDS: LayoutPoint[] = [];

// Teclas do desenho (com o foco nele): zoom e limpar a escolha.
enum StageKey {
  ZoomIn = 0,
  ZoomOut = 1,
  ZoomActual = 2,
  Clear = 3,
}

const STAGE_KEY: Record<string, StageKey> = {
  '+': StageKey.ZoomIn,
  '=': StageKey.ZoomIn,
  '-': StageKey.ZoomOut,
  _: StageKey.ZoomOut,
  '0': StageKey.ZoomActual,
  Escape: StageKey.Clear,
};

// Avisos do desenho que ficam até a pessoa dispensar ou a situação passar: a chave da tradução, nunca texto fixo.
enum Notice {
  SaveFailed = 0,
  ResetFailed = 1,
  LoadFailed = 2,
}

const NOTICE_KEY: Record<Notice, string> = {
  [Notice.SaveFailed]: 'business.layout.saveFailed',
  [Notice.ResetFailed]: 'business.layout.resetFailed',
  [Notice.LoadFailed]: 'business.layout.loadFailed',
};
// Texto de negócio (Fase 59): o mesmo limite da API, uma linha só.
export const LABEL_MAX_LENGTH = 120;
// Quanto tempo o retorno curto de uma ação fica na tela (e a chance de desfazer).
export const FEEDBACK_MS = 4000;
// Quanto tempo o "Posição salva" discreto fica à vista.
export const SAVED_MS = 2000;
// Valor da opção "Detectado" na lista de tipos (volta ao tipo que a regra achou): fora dos valores de BusinessKind.
export const KIND_DETECTED = -1;

// Onde o texto está sendo editado: no painel do passo ou no próprio balão.
export enum EditPlace {
  Panel = 0,
  Node = 1,
}

// Rótulo do ramo perto da chegada (0 = origem, 1 = destino); ver BRANCH_LABEL_POSITION em business-layout.ts.
export { BRANCH_LABEL_POSITION };

// Selo curto da origem do texto no balão: só quando não é a regra (agente, IA ou edição do usuário).
export const BUSINESS_ORIGIN_BADGE_KEY: Record<LabelOrigin, string | null> = {
  [LabelOrigin.Rule]: null,
  [LabelOrigin.Ai]: 'business.originBadge.ai',
  [LabelOrigin.Agent]: 'business.originBadge.agent',
  [LabelOrigin.User]: 'business.originBadge.user',
};

// O aviso único do desenho (retorno de ação, falha de gravação, "Abrindo…"). Os que vêm de um Notice ficam até
// serem dispensados ou resolvidos; os demais somem sozinhos. Pode levar uma ação de desfazer.
interface Toast {
  text: string;
  error: boolean;
  source: Notice | null;
  undo: (() => void) | null;
}

// Cada desenho tem a própria ajuda das teclas (o quadro põe várias faixas na mesma tela).
let keysHelpSequence = 0;

// Nome do host no botão "Abrir no ...": rótulo por tipo, nunca comparação com texto.
export const BUSINESS_HOST_LABEL_KEY: Record<RepositoryHostKind, string> = {
  [RepositoryHostKind.Unknown]: 'business.host.unknown',
  [RepositoryHostKind.AzureDevOps]: 'business.host.azure',
  [RepositoryHostKind.GitHub]: 'business.host.github',
  [RepositoryHostKind.GitLab]: 'business.host.gitlab',
  [RepositoryHostKind.Bitbucket]: 'business.host.bitbucket',
};

// Estado do texto do balão: "Proposto" ou "Confirmado", com quem escreveu.
export const BUSINESS_STATE_LABEL_KEY: Record<LabelState, string> = {
  [LabelState.Proposed]: 'business.text.proposed',
  [LabelState.Confirmed]: 'business.text.confirmed',
};

interface PlacedStep {
  step: BusinessStep;
  x: number;
  y: number;
  width: number;
  height: number;
  // No balão, só o nome do arquivo; o caminho completo fica no title e no painel.
  where: string | null;
  whereFull: string | null;
}

interface PlacedEdge {
  index: number;
  from: string;
  to: string;
  label: string | null;
  // Posição do rótulo ao longo da ligação (ligações irmãs não se sobrepõem).
  labelAt: number;
  inferred: boolean;
  // Pontos de dobra que a pessoa deixou na ligação (vazio: o traço automático).
  bends: LayoutPoint[];
}

// A visão de negócio do fluxo (Fase 57): só os passos-chave (entrada, decisões, notificações, recusas,
// chamadas entre sistemas e fim), em texto de negócio e com a referência ao código. Fica no lugar do canvas
// do grafo, como o mapa, mas desenha com o próprio Foblex: um balão por passo-chave, ligações em segmentos
// ortogonais e o rótulo do ramo ("Sim", "Não") sobre o fundo da tela. Tela livre (Fase 58): o balão se arrasta
// (grade de 10 px) e a posição fica guardada, com os pontos de dobra das ligações; "Reorganizar" volta ao
// automático. Roda do mouse rola a tela, Ctrl ou Cmd com a roda aproxima, Espaço com arrastar move a tela.
@Component({
  selector: 'app-business-view',
  imports: [FFlowModule, TranslatePipe, ConfirmDialogComponent, DrawingSwitcherComponent],
  templateUrl: './business-view.component.html',
  styleUrl: './business-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // A roda sem tecla rola a tela; com Ctrl ou Cmd ela aproxima em volta do ponteiro.
  providers: [provideFFlow(withControlScheme({ scrollPan: true }))],
  host: { '(document:keydown.space)': 'onSpace($event, true)', '(document:keyup.space)': 'onSpace($event, false)', '(window:blur)': 'panning.set(false)' },
})
export class BusinessViewComponent {
  private readonly api = inject(RastroApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly i18n = inject(LanguageService);
  private readonly bridge = inject(DesktopBridge);
  private readonly canvas = viewChild(FCanvasComponent);
  private readonly ai = inject(AiStatusService);

  readonly detail = input.required<SurveyDetail>();
  // Faixa de um quadro (Fase 60): o fluxo e a posição vêm do quadro, a posição é gravada no escopo do quadro, e a
  // tela do levantamento (direção do desenho, "Sugerir textos", "Ver no grafo") não se aplica.
  readonly boardId = input<string | null>(null);
  readonly boardFlow = input<BusinessFlow | null>(null);
  readonly boardLayout = input<BusinessLayout | null>(null);
  readonly selectedId = input<string | null>(null);
  // Notas dos passos do grafo (por id do nó), as mesmas que o painel do passo mostra.
  readonly notes = input<ReadonlyMap<string, string>>(new Map());
  readonly selectNode = output<string | null>();
  readonly directionChange = output<LayoutDirection>();
  // Resumo do fluxo para a visão geral do painel: sem o canvas, é o desenho que o monta.
  readonly summaryChange = output<FlowSummary>();
  // "Ver só este trecho": o recorte do fluxo pelo nó do passo.
  readonly focusFlow = output<string>();
  // "Ver no grafo": volta ao grafo com o nó escolhido.
  readonly showInGraph = output<string>();

  protected readonly flow = signal<BusinessFlow | null>(null);
  protected readonly error = signal<string | null>(null);
  // Espera o fluxo e a posição salva: o desenho abre já no lugar, sem os balões pularem depois.
  protected readonly loading = computed(() => !this.error() && (!this.flow() || !this.layoutReady()));
  // Só a entrada (ou nada): sem decisão, notificação ou chamada para mostrar.
  protected readonly empty = computed(() => !!this.flow() && this.flow()!.steps.length <= 1);
  private readonly reload = signal(0);
  private readonly links = signal<readonly RepositoryLinks[]>([]);
  protected readonly stepId = signal<string | null>(null);
  // A rota de links falhou: o painel avisa em vez de os botões de abrir sumirem em silêncio.
  protected readonly linksFailed = signal(false);
  protected readonly scale = signal(1);
  protected readonly percent = computed(() => Math.round(this.scale() * 100));
  private fitted = false;
  private pickedHere = false;

  // Posição salva (Fase 58): a do balão e os pontos de dobra de cada ligação, por (de, para).
  private readonly saved = signal<ReadonlyMap<string, LayoutPoint>>(new Map());
  private readonly bends = signal<ReadonlyMap<string, LayoutPoint[]>>(new Map());
  private readonly layoutReady = signal(false);
  protected readonly hasLayout = computed(() => this.saved().size > 0 || this.bends().size > 0);
  protected readonly toast = signal<Toast | null>(null);
  // "Posição salva" discreto depois de gravar (região de status sempre presente, sem tirar o foco de ninguém).
  protected readonly positionSaved = signal(false);
  protected readonly keysHelpId = `biz-keys-help-${++keysHelpSequence}`;
  protected readonly confirmingReset = signal(false);
  // Espaço segurado: arrastar move a tela, mesmo sobre um balão.
  protected readonly panning = signal(false);
  // O balão com o foco (ou o último que teve): é a única parada de Tab, e o Tab anda na ordem do fluxo.
  protected readonly focusedId = signal<string | null>(null);
  private surveyId = '';
  private pendingSteps = new Map<string, { stepId: string; x: number; y: number }>();
  private pendingEdges = new Map<string, { from: string; to: string; points: LayoutPoint[] }>();
  private saveTimer: ReturnType<typeof setTimeout> | undefined;
  private savedTimer: ReturnType<typeof setTimeout> | undefined;
  private scaleTimer: ReturnType<typeof setTimeout> | undefined;
  private destroyed = false;
  private pointerStart: LayoutPoint | null = null;

  protected readonly zoomMin = ZOOM_MIN;
  protected readonly zoomMax = ZOOM_MAX;
  protected readonly grid = BUSINESS_BOX.grid;
  // Mover o balão: botão principal, e não com o Espaço segurado (aí é a tela que se move).
  protected readonly nodeMoveTrigger = (event: FTriggerEvent): boolean => !this.panning() && primaryButtonEventTrigger(event);
  protected readonly canvasMoveTrigger = (event: FTriggerEvent): boolean => primaryButtonEventTrigger(event) || middleButtonEventTrigger(event);

  protected readonly markerEnd = EFMarkerType.END_ALL_STATES;
  protected readonly BusinessKind = BusinessKind;
  protected readonly LabelState = LabelState;
  protected readonly LayoutDirection = LayoutDirection;
  // Rótulo do ramo em texto simples (then e else viram "Se sim" e "Se não"), como no grafo e nas ligações.
  protected readonly branchText = plainBranch;
  protected readonly slug = BUSINESS_KIND_SLUG;
  protected readonly glyph = BUSINESS_KIND_GLYPH;
  protected readonly kindLabel = BUSINESS_KIND_LABEL_KEY;
  protected readonly originLabel = LABEL_ORIGIN_LABEL_KEY;
  protected readonly originBadge = BUSINESS_ORIGIN_BADGE_KEY;
  protected readonly stateLabel = BUSINESS_STATE_LABEL_KEY;
  protected readonly hostLabel = BUSINESS_HOST_LABEL_KEY;
  protected readonly kindOrder = BUSINESS_KIND_ORDER;
  protected readonly kindDetected = KIND_DETECTED;
  protected readonly labelMax = LABEL_MAX_LENGTH;
  protected readonly EditPlace = EditPlace;
  protected readonly LabelOrigin = LabelOrigin;

  // Texto de negócio (Fase 59): edição em curso (um campo por vez), rascunho, ação rodando e retorno curto.
  protected readonly editing = signal<{ stepId: string; place: EditPlace } | null>(null);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly suggesting = signal(false);
  private toastTimer: ReturnType<typeof setTimeout> | undefined;
  // "Sugerir textos" só existe com a IA ligada e o uso "Sugestões" habilitado: nunca botão morto. Na faixa de um
  // quadro o pedido é do quadro inteiro (com o glossário), e o botão fica na tela dele.
  protected readonly inBoard = computed(() => this.boardId() !== null);
  protected readonly canSuggest = computed(() => !this.inBoard() && aiCan(this.ai.status(), AiUse.Suggestions));
  // Último fluxo e última posição que vieram do quadro (em texto): o quadro rele os dados de tempos em tempos, e só
  // o que mudou de fato refaz o desenho.
  private boardFlowKey = '';
  private boardLayoutKey = '';

  private readonly nodes = computed(() => new Map((this.detail().graph?.nodes ?? []).map((node) => [node.id, node])));

  // O desenho automático, só do fluxo; a posição salva vem por cima dele.
  private readonly automatic = computed<BusinessGeometry | null>(() => {
    const flow = this.flow();
    return flow && flow.steps.length > 1 ? businessLayout(flow) : null;
  });

  protected readonly geometry = computed<BusinessGeometry | null>(() => {
    const automatic = this.automatic();
    return automatic ? withSavedPositions(automatic, this.saved()) : null;
  });

  // Ordem do fluxo (a da lista de passos): é nela que o Tab anda, e não na ordem dos nós no DOM do Foblex.
  private readonly stepOrder = computed(() => this.placed().map((p) => p.step.id));

  protected readonly tabStop = computed(() => {
    const order = this.stepOrder();
    const wanted = this.focusedId();
    return wanted && order.includes(wanted) ? wanted : (order[0] ?? null);
  });

  protected readonly placed = computed<PlacedStep[]>(() => {
    const flow = this.flow();
    const geometry = this.geometry();
    if (!flow || !geometry) return [];
    return flow.steps.flatMap((step) => {
      const box = geometry.boxes.get(step.id);
      return box ? [{ step, ...box, where: step.ref ? this.whereOf(step.ref) : null, whereFull: step.ref ? this.whereOf(step.ref, true) : null }] : [];
    });
  });

  protected readonly edges = computed<PlacedEdge[]>(() => {
    const geometry = this.geometry();
    if (!geometry) return [];
    const bends = this.bends();
    const drawn = (this.flow()?.edges ?? []).flatMap((edge, index) => (geometry.boxes.has(edge.from) && geometry.boxes.has(edge.to) && edge.from !== edge.to ? [{ edge, index }] : []));
    return drawn.map(({ edge, index }) => ({
      index,
      from: edge.from,
      to: edge.to,
      label: edge.label ? plainBranch(edge.label) : null,
      labelAt: BRANCH_LABEL_POSITION,
      inferred: edge.inferred,
      bends: bends.get(edgeKey(edge.from, edge.to)) ?? NO_BENDS,
    }));
  });

  protected readonly current = computed(() => this.flow()?.steps.find((s) => s.id === this.stepId()) ?? null);

  // O que o painel do passo mostra (seção 10.5 da proposta).
  protected readonly panel = computed(() => {
    const flow = this.flow();
    const step = this.current();
    if (!flow || !step) return null;
    const nodes = this.nodes();
    const links = step.ref ? this.linksOf(step.ref) : null;
    const grouped = step.sourceNodeIds
      .filter((id) => id !== step.nodeId)
      .flatMap((id) => {
        const node = nodes.get(id);
        return node ? [{ id, text: readingLabel(node), where: node.location ? `${node.location.file}:${node.location.line}` : null }] : [];
      });
    return {
      step,
      ref: step.ref,
      commit: shortCommit(step.ref?.commitHash ?? undefined),
      hostUrl: links?.hostUrl ?? null,
      editorUrl: links?.editorUrl ?? null,
      host: links?.host ?? RepositoryHostKind.Unknown,
      path: pathTo(flow, step.id),
      grouped,
      note: this.notes().get(step.nodeId) ?? null,
    };
  });

  constructor() {
    let loadedId: string | null = null;
    // Posição ainda não gravada vai embora junto com a tela (ou com a troca de levantamento).
    inject(DestroyRef).onDestroy(() => {
      this.flushSave();
      this.destroyed = true;
      clearTimeout(this.toastTimer);
      clearTimeout(this.savedTimer);
      clearTimeout(this.scaleTimer);
    });
    effect((onCleanup) => {
      const detail = this.detail();
      this.reload();
      untracked(() => {
        // Outro levantamento começa limpo; o mesmo, com o grafo novo, mantém o passo escolhido.
        if (detail.survey.id !== loadedId) {
          this.flushSave();
          loadedId = detail.survey.id;
          this.surveyId = detail.survey.id;
          this.flow.set(null);
          this.stepId.set(null);
          this.links.set([]);
          this.saved.set(new Map());
          this.bends.set(new Map());
          this.layoutReady.set(false);
          this.focusedId.set(null);
          this.editing.set(null);
          this.boardFlowKey = '';
          this.boardLayoutKey = '';
        }
        // Tentar de novo depois de um erro busca tudo outra vez, inclusive a posição salva.
        if (this.error() !== null) this.layoutReady.set(false);
        this.linksFailed.set(false);
        this.error.set(null);
        if (this.inBoard()) this.syncBoard();
      });
      // Na faixa do quadro o fluxo e a posição já vieram com o quadro (syncBoard): só os links se buscam aqui.
      const sub = this.inBoard()
        ? undefined
        : this.api.business(detail.survey.id).subscribe({
            next: (flow) =>
              untracked(() => {
                this.flow.set(flow);
                this.fitted = false;
                if (!flow.steps.some((s) => s.id === this.stepId())) this.stepId.set(null);
              }),
            error: (error) => untracked(() => this.error.set(apiErrorMessage(error))),
          });
      // A posição salva é um enfeite do desenho: se não vier, vale a organização automática, com um aviso.
      const layoutSub = this.inBoard()
        ? undefined
        : this.api.businessLayout(detail.survey.id).subscribe({
            next: (layout) =>
              untracked(() => {
                this.applyLayout(layout);
                this.layoutReady.set(true);
              }),
            error: () =>
              untracked(() => {
                this.showNotice(Notice.LoadFailed);
                this.layoutReady.set(true);
              }),
          });
      // Os modelos de link só enfeitam o painel: sem eles os botões de abrir não aparecem.
      const linksSub = this.api.links(detail.survey.id).subscribe({
        next: (links) => untracked(() => this.links.set(links)),
        error: () =>
          untracked(() => {
            this.links.set([]);
            this.linksFailed.set(true);
          }),
      });
      onCleanup(() => {
        sub?.unsubscribe();
        linksSub.unsubscribe();
        layoutSub?.unsubscribe();
      });
    });

    // O quadro releu os dados: o fluxo e a posição novos entram sem mexer no que não mudou.
    effect(() => {
      this.boardFlow();
      this.boardLayout();
      untracked(() => {
        if (this.inBoard()) this.syncBoard();
      });
    });

    effect(() => {
      const graph = this.detail().graph;
      if (graph) this.summaryChange.emit(flowSummaryOf(graph));
    });

    // Seleção de fora (painel do passo, busca, setas): o balão que resume o nó acende.
    effect(() => {
      const selected = this.selectedId();
      const flow = this.flow();
      untracked(() => {
        if (!selected) {
          this.stepId.set(null);
          return;
        }
        if (!flow) return;
        const current = this.current();
        if (current && (current.nodeId === selected || current.sourceNodeIds.includes(selected))) return;
        const found = flow.steps.find((s) => s.nodeId === selected) ?? flow.steps.find((s) => s.sourceNodeIds.includes(selected));
        this.stepId.set(found?.id ?? null);
        if (found) this.focusedId.set(found.id);
        if (found && !this.pickedHere) afterNextRender(() => this.canvas()?.centerGroupOrNode(found.id, true), { injector: this.injector });
      });
    });
  }

  // Faixa de um quadro: pega o fluxo e a posição que o quadro entregou, só se mudaram desde a última vez.
  private syncBoard(): void {
    const flow = this.boardFlow();
    const flowKey = JSON.stringify(flow);
    if (flow && flowKey !== this.boardFlowKey) {
      this.boardFlowKey = flowKey;
      this.flow.set(flow);
      this.fitted = false;
      if (!flow.steps.some((s) => s.id === this.stepId())) this.stepId.set(null);
    }
    const layout = this.boardLayout();
    const layoutKey = JSON.stringify(layout);
    if (layout && layoutKey !== this.boardLayoutKey) {
      this.boardLayoutKey = layoutKey;
      this.applyLayout(layout);
    }
    if (layout) this.layoutReady.set(true);
  }

  private whereOf(ref: CodeRef, full = false): string {
    const file = full ? ref.file : (ref.file.split(/[\\/]/).pop() ?? ref.file);
    return `${ref.repository} · ${file}:${ref.line}`;
  }

  // Texto do balão em pedaços que terminam no ponto: o <wbr> depois de cada um deixa a quebra cair ali,
  // sem mudar o texto do modelo nem o que o leitor de tela lê.
  protected parts(text: string): string[] {
    return text.split(/(?<=\.)/);
  }

  // Links do código no host e no editor, a partir dos modelos que a API devolveu para o repositório.
  private linksOf(ref: CodeRef): { host: RepositoryHostKind; hostUrl: string | null; editorUrl: string | null } | null {
    const entry = this.links().find((l) => l.repository.toLowerCase() === ref.repository.toLowerCase());
    if (!entry) return null;
    return {
      host: entry.host,
      hostUrl: entry.commitTemplate ? fillLinkTemplate(entry.commitTemplate, ref.file, ref.line) : null,
      editorUrl: entry.editorTemplate ? fillLinkTemplate(entry.editorTemplate, ref.file, ref.line) : null,
    };
  }

  protected zoomIn(): void {
    const canvas = this.canvas();
    if (canvas) canvas.setScale(Math.min(canvas.getScale() * ZOOM_STEP, ZOOM_MAX));
    this.syncScale();
  }

  protected zoomOut(): void {
    const canvas = this.canvas();
    if (canvas) canvas.setScale(Math.max(canvas.getScale() / ZOOM_STEP, ZOOM_MIN));
    this.syncScale();
  }

  protected actualSize(): void {
    this.canvas()?.setScale(1);
    this.syncScale();
  }

  // Enquadra o quadro inteiro, sem passar de 100%.
  protected fit(): void {
    this.fitClamped(true);
  }

  // Abre já enquadrado.
  protected onFullRendered(): void {
    if (this.fitted) return;
    this.fitted = true;
    this.fitClamped(false);
  }

  private fitClamped(animated: boolean): void {
    this.canvas()?.fitToScreen({ x: FIT_PADDING, y: FIT_PADDING }, animated, false, 1);
    this.syncScale();
  }

  // O enquadramento do Foblex não respeita o fZoomMinimum: numa área pequena (360 px, com o painel de baixo aberto)
  // chegava a 2% e o desenho virava um ponto (visto no passo de UX da release 0.11.0). Ele se aplica um quadro
  // depois da chamada, então a correção fica aqui, na mudança do canvas: abaixo do mínimo, volta para o mínimo e a
  // pessoa move a tela.
  protected onCanvasChange(scale: number): void {
    const canvas = this.canvas();
    if (canvas && scale < ZOOM_MIN) {
      canvas.setScale(ZOOM_MIN);
      this.scale.set(ZOOM_MIN);
      return;
    }
    this.scale.set(scale);
  }

  private syncScale(): void {
    clearTimeout(this.scaleTimer);
    this.scaleTimer = setTimeout(() => {
      const canvas = this.canvas();
      // O enquadramento não avisa pelo fCanvasChange: o mínimo se confere também aqui (ver onCanvasChange).
      if (canvas) this.onCanvasChange(canvas.getScale());
    }, SYNC_SCALE_DELAY_MS);
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected seeGraph(): void {
    this.directionChange.emit(LayoutDirection.Horizontal);
  }

  protected pick(step: BusinessStep): void {
    this.pickedHere = true;
    this.stepId.set(step.id);
    this.focusedId.set(step.id);
    this.selectNode.emit(step.nodeId);
    this.pickedHere = false;
    // O painel abre embaixo e encolhe a área do desenho: o balão escolhido não pode ficar atrás dele.
    this.revealAfterRender(step.id);
  }

  protected pointerDown(event: PointerEvent): void {
    this.pointerStart = { x: event.clientX, y: event.clientY };
  }

  // Clique sem arrastar abre o painel; arrastar o balão só o move.
  protected click(event: MouseEvent, step: BusinessStep): void {
    const start = this.pointerStart;
    this.pointerStart = null;
    if (start && Math.hypot(event.clientX - start.x, event.clientY - start.y) >= CLICK_TOLERANCE_PX) return;
    this.pick(step);
  }

  protected pickGrouped(nodeId: string): void {
    this.selectNode.emit(nodeId);
  }

  protected pickPath(step: BusinessStep): void {
    this.pick(step);
    this.focusAfterRender(step.id);
  }

  protected close(): void {
    const step = this.current();
    this.clearChoice();
    if (step) this.focusAfterRender(step.id);
  }

  private clearChoice(): void {
    this.stepId.set(null);
    this.selectNode.emit(null);
  }

  protected focusSection(): void {
    const step = this.current();
    if (step) this.focusFlow.emit(step.nodeId);
  }

  protected viewInGraph(): void {
    const step = this.current();
    if (step) this.showInGraph.emit(step.nodeId);
  }

  // Abre o link e avisa que está abrindo. A janela só abre host reconhecido e editor configurado e responde se
  // abriu; a recusa dela (ou o sistema sem programa para o link) troca o aviso pelo de erro.
  protected open(url: string | null): void {
    if (!url) return;
    this.say('business.opening');
    this.bridge.openExternal(url).then((opened) => {
      if (!opened) this.showOpenFailed();
    }, () => this.showOpenFailed());
  }

  // A ponte não diz o motivo (nem deve: a URL recusada não volta); o aviso é o retorno que a pessoa precisa.
  private showOpenFailed(): void {
    this.show({ text: this.i18n.t('business.openFailed'), error: true, source: null, undo: null });
  }

  private nodeElement(id: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[data-step-id="${CSS.escape(id)}"]`);
  }

  // Foco sem rolar: o contêiner do Foblex tem overflow escondido e rolaria a imagem para longe do que ele calcula.
  private focusNow(id: string): void {
    this.nodeElement(id)?.focus({ preventScroll: true });
  }

  private focusAfterRender(id: string): void {
    afterNextRender(() => this.focusNow(id), { injector: this.injector });
  }

  // Traz o balão para a área visível do desenho; se já está à vista, não mexe na tela.
  private reveal(id: string): void {
    const node = this.nodeElement(id)?.getBoundingClientRect();
    const stage = this.host.nativeElement.querySelector('.biz-stage')?.getBoundingClientRect();
    if (!node || !stage || stage.width === 0 || stage.height === 0) return;
    const inside = node.left >= stage.left + REVEAL_MARGIN_PX && node.right <= stage.right - REVEAL_MARGIN_PX && node.top >= stage.top + REVEAL_MARGIN_PX && node.bottom <= stage.bottom - REVEAL_MARGIN_PX;
    if (!inside) this.canvas()?.centerGroupOrNode(id, true);
  }

  private revealAfterRender(id: string): void {
    afterNextRender(() => this.reveal(id), { injector: this.injector });
  }

  // Tab percorre os balões na ordem do fluxo, e não na ordem dos nós no DOM (o Foblex leva o balão clicado para o
  // fim). Nas pontas o Tab segue o caminho normal e sai do desenho.
  protected walk(event: Event, id: string, direction: 1 | -1): void {
    const next = flowNeighbour(this.stepOrder(), id, direction);
    if (!next) return;
    event.preventDefault();
    this.focusNow(next);
    this.reveal(next);
  }

  // Setas movem o balão escolhido um passo da grade e já guardam a posição.
  protected nudge(event: Event, step: BusinessStep, dx: number, dy: number): void {
    event.preventDefault();
    const box = this.geometry()?.boxes.get(step.id);
    if (!box) return;
    this.place(step.id, { x: box.x + dx * this.grid, y: box.y + dy * this.grid });
    this.revealAfterRender(step.id);
  }

  // Teclas com o foco no desenho: + - 0 para o zoom e Esc para limpar a escolha.
  protected onStageKey(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    const key = STAGE_KEY[event.key];
    if (key === undefined) return;
    event.preventDefault();
    if (key === StageKey.ZoomIn) this.zoomIn();
    else if (key === StageKey.ZoomOut) this.zoomOut();
    else if (key === StageKey.ZoomActual) this.actualSize();
    else if (this.stepId() !== null) this.clearChoice();
  }

  // Espaço segurado vira a "mão": arrastar move a tela, mesmo sobre um balão (por isso o Espaço não escolhe o
  // balão: quem escolhe é o Enter). Em campo de texto e em botão a tecla segue o comportamento normal.
  protected onSpace(event: Event, down: boolean): void {
    if (!down) {
      this.panning.set(false);
      return;
    }
    const target = event.target;
    if (!this.geometry() || (target instanceof Element && target.closest('input, textarea, select, button, a, summary, [contenteditable]'))) return;
    event.preventDefault();
    this.panning.set(true);
  }

  // ---- Posição salva (Fase 58) ----

  private applyLayout(layout: BusinessLayout): void {
    this.saved.set(new Map(layout.steps.map((s) => [s.stepId, { x: s.x, y: s.y }])));
    this.bends.set(new Map(layout.edges.filter((e) => e.points.length > 0).map((e) => [edgeKey(e.from, e.to), e.points.map((p) => ({ x: p.x, y: p.y }))])));
  }

  // O Foblex avisa a posição nova do balão (já na grade); só o que mudou de fato é guardado.
  protected moved(step: BusinessStep, point: LayoutPoint): void {
    const at = { x: snapToGrid(point.x), y: snapToGrid(point.y) };
    const box = this.geometry()?.boxes.get(step.id);
    if (!box || (box.x === at.x && box.y === at.y)) return;
    this.place(step.id, at);
  }

  private place(id: string, at: LayoutPoint): void {
    this.saved.update((map) => new Map(map).set(id, at));
    this.pendingSteps.set(id, { stepId: id, x: at.x, y: at.y });
    this.scheduleSave();
  }

  // Pontos de dobra da ligação (arrastar para criar ou mover, botão direito para tirar): guardados com os balões.
  protected bent(event: FConnectionWaypointsChangedEvent): void {
    const edge = this.edges().find((e) => `e${e.index}` === event.connectionId);
    if (!edge) return;
    const points = event.waypoints.map((p) => ({ x: snapToGrid(p.x), y: snapToGrid(p.y) }));
    const key = edgeKey(edge.from, edge.to);
    this.bends.update((map) => {
      const next = new Map(map);
      if (points.length === 0) next.delete(key);
      else next.set(key, points);
      return next;
    });
    this.pendingEdges.set(key, { from: edge.from, to: edge.to, points });
    this.scheduleSave();
  }

  private scheduleSave(): void {
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.flushSave(), SAVE_DEBOUNCE_MS);
  }

  // Grava o que está pendente num lote só. Se falhar, o movimento fica na tela, o aviso aparece e o que não
  // foi gravado volta para a fila (a próxima mexida tenta de novo).
  private flushSave(): void {
    clearTimeout(this.saveTimer);
    this.saveTimer = undefined;
    const surveyId = this.surveyId;
    if (!surveyId || (this.pendingSteps.size === 0 && this.pendingEdges.size === 0)) return;
    const steps = this.pendingSteps;
    const edges = this.pendingEdges;
    this.pendingSteps = new Map();
    this.pendingEdges = new Map();
    const boardId = this.boardId();
    const batch: BusinessLayout = { steps: [...steps.values()], edges: [...edges.values()] };
    const request = boardId
      ? this.api.saveBoardLayout(boardId, { steps: batch.steps.map((s) => ({ laneSurveyId: surveyId, ...s })), edges: batch.edges.map((e) => ({ laneSurveyId: surveyId, ...e })) })
      : this.api.saveBusinessLayout(surveyId, batch);
    request.subscribe({
      next: () =>
        untracked(() => {
          if (this.destroyed) return;
          if (this.toast()?.source === Notice.SaveFailed) this.toast.set(null);
          this.announceSaved();
        }),
      error: () =>
        untracked(() => {
          if (surveyId !== this.surveyId) return;
          for (const [id, step] of steps) if (!this.pendingSteps.has(id)) this.pendingSteps.set(id, step);
          for (const [key, edge] of edges) if (!this.pendingEdges.has(key)) this.pendingEdges.set(key, edge);
          this.showNotice(Notice.SaveFailed);
        }),
    });
  }

  // Retorno discreto de que a posição foi gravada: some sozinho e não toma o lugar do aviso.
  private announceSaved(): void {
    this.positionSaved.set(true);
    clearTimeout(this.savedTimer);
    this.savedTimer = setTimeout(() => this.positionSaved.set(false), SAVED_MS);
  }

  protected askReset(): void {
    this.confirmingReset.set(true);
  }

  protected cancelReset(): void {
    this.confirmingReset.set(false);
  }

  // Reorganizar: apaga as posições e os pontos de dobra e volta ao desenho automático.
  protected confirmReset(): void {
    this.confirmingReset.set(false);
    clearTimeout(this.saveTimer);
    this.pendingSteps = new Map();
    this.pendingEdges = new Map();
    const boardId = this.boardId();
    (boardId ? this.api.resetBoardLayout(boardId, this.surveyId) : this.api.resetBusinessLayout(this.surveyId)).subscribe({
      next: () =>
        untracked(() => {
          this.saved.set(new Map());
          this.bends.set(new Map());
          this.toast.set(null);
          afterNextRender(() => this.fit(), { injector: this.injector });
        }),
      error: () => untracked(() => this.showNotice(Notice.ResetFailed)),
    });
  }

  // Rótulo do balão para o leitor de tela: tipo e texto (sem repetir o tipo quando o texto já começa por ele) e o
  // estado ("proposto", "inferido") quando valer.
  protected stepAria(step: BusinessStep): string {
    const kind = this.i18n.t(BUSINESS_KIND_LABEL_KEY[step.kind]);
    const repeats = step.label.toLowerCase().startsWith(kind.toLowerCase());
    const text = repeats ? step.label : this.i18n.t('business.stepAria', { kind, label: step.label });
    const origin = this.originBadge[step.labelOrigin] ? this.i18n.t('business.stepAriaOrigin', { origin: this.i18n.t(LABEL_ORIGIN_LABEL_KEY[step.labelOrigin]) }) : null;
    const states = [this.isProposed(step) ? this.i18n.t('business.proposed') : null, step.inferred ? this.i18n.t('business.inferred') : null, origin].filter((x) => x !== null);
    return states.length > 0 ? this.i18n.t('business.stepAriaStates', { text, states: states.join(', ') }) : text;
  }

  // Linhas do texto no balão (a mesma estimativa que dá a altura do balão no layout).
  protected textLines(step: BusinessStep): number {
    return businessTextLines(step);
  }

  // Dica do selo "proposto": quem propôs e onde confirmar.
  protected proposedHint(step: BusinessStep): string {
    return this.i18n.t('business.proposedHint', { origin: this.i18n.t(LABEL_ORIGIN_LABEL_KEY[step.labelOrigin]) });
  }

  protected isProposed(step: BusinessStep): boolean {
    return step.labelState === LabelState.Proposed;
  }

  // ---- Texto e tipo do passo (Fase 59) ----

  protected isEditing(stepId: string, place: EditPlace): boolean {
    const edit = this.editing();
    return edit !== null && edit.stepId === stepId && edit.place === place;
  }

  // Abre o campo (no painel ou no balão) com o texto atual e leva o foco para ele.
  protected startEdit(step: BusinessStep, place: EditPlace): void {
    this.editing.set({ stepId: step.id, place });
    this.draft.set(step.label);
    afterNextRender(
      () => {
        const input = this.host.nativeElement.querySelector<HTMLInputElement>(`input[data-edit="${place}"]`);
        input?.focus({ preventScroll: true });
        input?.select();
      },
      { injector: this.injector },
    );
  }

  protected onDraft(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value);
  }

  // Enter salva e Esc cancela. As teclas do campo não sobem para o balão (setas movem, Enter abre o painel).
  protected editKey(event: KeyboardEvent): void {
    event.stopPropagation();
    if (event.key === 'Enter') {
      event.preventDefault();
      this.commitEdit(true);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelEdit();
    }
  }

  protected cancelEdit(): void {
    const edit = this.editing();
    this.editing.set(null);
    if (edit) this.focusAfterRender(edit.stepId);
  }

  // Salva o texto (vazio volta ao da regra). Sem mudança, só fecha o campo.
  protected commitEdit(refocus = false): void {
    const edit = this.editing();
    if (!edit) return;
    this.editing.set(null);
    const step = this.flow()?.steps.find((s) => s.id === edit.stepId);
    if (!step) return;
    if (refocus) this.focusAfterRender(step.id);
    const text = this.draft().replace(/\s+/g, ' ').trim();
    if (text === step.label) return;
    this.saveLabel(step, text || null);
  }

  protected useRuleText(step: BusinessStep): void {
    this.saveLabel(step, null);
  }

  // Grava o texto e oferece "Desfazer" por alguns segundos: o anterior volta pela mesma rota (texto da regra volta
  // como nulo; qualquer outro, como o texto que estava). Texto proposto pela IA ou pelo agente e ainda não confirmado
  // não ganha "Desfazer": a rota grava como texto do usuário, e isso confirmaria uma proposta que ninguém confirmou
  // (ADR-001).
  private saveLabel(step: BusinessStep, text: string | null): void {
    const before = step.labelOrigin === LabelOrigin.Rule ? null : step.label;
    const proposedByAi = step.labelOrigin === LabelOrigin.Ai || step.labelOrigin === LabelOrigin.Agent;
    const unconfirmedProposal = proposedByAi && step.labelState === LabelState.Proposed;
    this.act(this.api.saveBusinessLabel(this.surveyId, step.id, text), (updated) => {
      this.replaceStep(updated);
      const undo = unconfirmedProposal ? undefined : () => this.restoreLabel(updated.id, before);
      this.say(text === null ? 'business.feedback.labelRule' : 'business.feedback.labelSaved', undefined, undo);
    });
  }

  private restoreLabel(stepId: string, text: string | null): void {
    this.act(this.api.saveBusinessLabel(this.surveyId, stepId, text), (updated) => {
      this.replaceStep(updated);
      this.say('business.feedback.labelRestored');
    });
  }

  protected confirmLabel(step: BusinessStep): void {
    this.act(this.api.confirmBusinessLabel(this.surveyId, step.id), (updated) => {
      this.replaceStep(updated);
      this.say('business.feedback.labelConfirmed');
    });
  }

  // Lista de tipos: "Detectado" tira o tipo escolhido e volta ao que a regra achou.
  protected onKindChange(step: BusinessStep, event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    const kind = value === KIND_DETECTED ? null : (value as BusinessKind);
    this.act(this.api.setBusinessKind(this.surveyId, step.id, kind), (flow) => {
      this.applyFlow(flow);
      this.say(kind === null ? 'business.feedback.kindDetected' : 'business.feedback.kindSaved');
    });
  }

  protected confirmKind(step: BusinessStep): void {
    this.act(this.api.confirmBusinessKind(this.surveyId, step.id), (flow) => {
      this.applyFlow(flow);
      this.say('business.feedback.kindConfirmed');
    });
  }

  protected dismissKind(step: BusinessStep): void {
    this.act(this.api.dismissBusinessKind(this.surveyId, step.id), (flow) => {
      this.applyFlow(flow);
      this.say('business.feedback.kindDismissed');
    });
  }

  // Pede ao provedor os textos que faltam: eles voltam como propostas, que só o usuário confirma.
  protected suggest(): void {
    if (this.suggesting()) return;
    const surveyId = this.surveyId;
    this.suggesting.set(true);
    this.api.suggestBusinessLabels(surveyId).subscribe({
      next: (result) =>
        untracked(() => {
          this.suggesting.set(false);
          if (surveyId !== this.surveyId) return;
          // O resultado só é anunciado depois de o fluxo recarregado entrar no desenho: o aviso fala do que já está na tela.
          this.api.business(surveyId).subscribe({
            next: (flow) =>
              untracked(() => {
                if (surveyId !== this.surveyId) return;
                this.applyFlow(flow);
                this.say(result.proposed > 0 ? 'business.feedback.suggested' : 'business.feedback.suggestedNone', { count: result.proposed, skipped: result.skipped });
              }),
            error: (error) => untracked(() => this.sayError(error)),
          });
        }),
      error: (error) =>
        untracked(() => {
          this.suggesting.set(false);
          this.sayError(error);
        }),
    });
  }

  // Roda uma ação de gravação: um clique por vez, o desenho se atualiza com a resposta e o erro vira aviso.
  private act<T>(request: Observable<T>, done: (result: T) => void): void {
    const surveyId = this.surveyId;
    this.busy.set(true);
    request.subscribe({
      next: (result) =>
        untracked(() => {
          this.busy.set(false);
          if (surveyId === this.surveyId) done(result);
        }),
      error: (error) =>
        untracked(() => {
          this.busy.set(false);
          this.sayError(error);
        }),
    });
  }

  private replaceStep(updated: BusinessStep): void {
    this.flow.update((flow) => (flow ? { ...flow, steps: flow.steps.map((s) => (s.id === updated.id ? updated : s)) } : flow));
  }

  // O fluxo inteiro volta da API quando o tipo muda (o passo pode ganhar ou perder ligações).
  private applyFlow(flow: BusinessFlow): void {
    this.flow.set(flow);
    if (!flow.steps.some((s) => s.id === this.stepId())) this.stepId.set(null);
  }

  private say(key: string, params?: Record<string, unknown>, undo?: () => void): void {
    this.show({ text: this.i18n.t(key, params), error: false, source: null, undo: undo ?? null });
  }

  private sayError(error: unknown): void {
    this.show({ text: apiErrorMessage(error), error: true, source: null, undo: null });
  }

  // Aviso que fica até ser dispensado ou resolvido (falha de gravação, de reorganizar, de carregar a posição).
  private showNotice(notice: Notice): void {
    this.show({ text: this.i18n.t(NOTICE_KEY[notice]), error: true, source: notice, undo: null });
  }

  private show(toast: Toast): void {
    this.toast.set(toast);
    clearTimeout(this.toastTimer);
    if (toast.source === null) this.toastTimer = setTimeout(() => this.toast.set(null), FEEDBACK_MS);
  }

  protected dismissToast(): void {
    clearTimeout(this.toastTimer);
    this.toast.set(null);
  }

  // "Desfazer" do aviso: roda a ação uma vez e tira o aviso (o retorno da própria ação o substitui).
  protected undoToast(): void {
    const undo = this.toast()?.undo;
    this.dismissToast();
    undo?.();
  }

  // O que a exportação lê (Fase 61): o fluxo, a geometria com as posições salvas (inclusive as arrastadas e ainda não
  // gravadas) e os pontos de dobra. O quadro monta o arquivo dele com os dados de cada faixa.
  exportData(): BusinessExportData | null {
    const flow = this.flow();
    const geometry = this.geometry();
    return flow && geometry ? { surveyId: this.detail().survey.id, flow, geometry, bends: this.bends() } : null;
  }

  // draw.io (Fases 57 e 61): a visão de negócio como está na tela, com o link de cada balão para o código e os pontos de dobra.
  exportDrawio(name: string, linkOf?: HostLinkOf): string | null {
    const data = this.exportData();
    return data ? businessToDrawio(data.flow, data.geometry, name, linkOf, data.bends) : null;
  }

  // SVG (e, a partir dele, o PNG): as posições da tela e as cores por tipo da paleta que está em uso.
  exportSvg(): { svg: string; width: number; height: number } | null {
    const data = this.exportData();
    if (!data) return null;
    const style = getComputedStyle(this.host.nativeElement);
    return businessToSvg([{ title: '', data }], readBusinessColors((name) => style.getPropertyValue(name)), false);
  }

  exportMermaid(): string | null {
    const flow = this.flow();
    return flow && flow.steps.length > 0 ? businessToMermaid([{ title: '', flow }], false) : null;
  }
}
