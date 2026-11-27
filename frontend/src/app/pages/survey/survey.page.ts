import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { DB_SURVEY_ORIGIN_PARAM } from '../../core/database/query-analysis';
import { refusalText, surveyFailureText } from '../../core/failure-text';
import { shortCommit, shortDate } from '../../core/format';
import { LanguageService } from '../../core/i18n/i18n';
import { AiDisclosureComponent } from '../../core/ai-disclosure.component';
import { AiStatusService } from '../../core/ai-status.service';
import { CopyFeedback } from '../../core/copy-feedback';
import { EditorLinks, isVsCodeLink } from '../../core/editor-links';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { addStep, changeCount, connect, reassign, removeEdge, removeStep, restoreStep, setIntent } from '../../core/graph/draft-editor';
import { trailTo } from '../../core/graph/trail';
import { surveySystems } from '../../core/graph/timeline-layout';
import { focusOn } from '../../core/graph/focus';
import { searchNodes } from '../../core/graph/search';
import { splitLabel } from '../../core/graph/reading';
import { collapseAfter, collapsedOwner } from '../../core/graph/collapse';
import { pathsBetween } from '../../core/graph/between';
import { compareGraphs } from '../../core/graph/compare';
import { isFrameworkCall, withoutFrameworkCalls } from '../../core/graph/visibility';
import {
  AiUse,
  AI_PROVIDER_LABEL,
  BusinessKind,
  RefactorSuggestion,
  ChangeKind,
  ChangePrompt,
  EdgeDecision,
  FillHoleResult,
  FlowEdge,
  FlowGraph,
  NodeKind,
  CrossFlowFinding,
  SURVEY_STATUS_LABEL_KEY,
  aiCan,
  SURVEY_STATUS_SLUG,
  SurveyDetail,
  SurveySummary,
  SurveyStaleness,
  SurveyStatus,
  VALIDATION_VERDICT_LABEL_KEY,
  ValidationStatus,
  ValidationView,
  isFinished,
  isValidationFinished,
  CreateSurveyRequest,
} from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { SurveyPageTitle } from '../../core/page-title.strategy';
import { FlowCanvasComponent } from './flow-canvas.component';
import { ConsolidatedViewComponent } from './consolidated-view.component';
import { DRAWIO_MIME } from '../../core/graph/drawio';
import { HostLinkOf, hostLinkResolver } from '../../core/graph/link-template';
import { MERMAID_MIME, SVG_MIME, mermaidFenced } from '../../core/graph/export';
import { FlowMapViewComponent, MapTab } from './flow-map-view.component';
import { BusinessViewComponent } from './business-view.component';
import { AddToBoardDialogComponent } from '../boards/add-to-board-dialog.component';
import { LayoutDirection, readLayoutDirection, saveLayoutDirection } from '../../core/graph/layout';
import { FlowSummary } from '../../core/graph/summary';
import { NodePanelComponent } from './node-panel.component';
import { ValidationDialogComponent } from './validation-dialog.component';
import { C4DialogComponent } from './c4-dialog.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { DialogDirective } from '../../core/dialog.directive';
import { HttpErrorResponse } from '@angular/common/http';

const POLL_MS = 2000;
// As quatro visões do seletor da barra (F09, Fase 57). Rótulo e dica são chaves de tradução, nunca comparação com texto.
const DRAWING_VIEWS: readonly { direction: LayoutDirection; label: string; title: string }[] = [
  { direction: LayoutDirection.Horizontal, label: 'survey.view.graph.label', title: 'survey.view.graph.title' },
  { direction: LayoutDirection.Map, label: 'survey.view.map.label', title: 'survey.view.map.title' },
  { direction: LayoutDirection.Consolidated, label: 'survey.view.consolidated.label', title: 'survey.view.consolidated.title' },
  { direction: LayoutDirection.Business, label: 'survey.view.business.label', title: 'survey.view.business.title' },
];
const SEARCH_FOCUS_DELAY_MS = 120;
// Lista de ocorrências da busca (UX-G-F22) e espera até o canvas voltar (UX-G-F03).
const SEARCH_LIST_LIMIT = 8;
const CANVAS_BACK_DELAY_MS = 150;
const EXPORT_MESSAGE_MS = 4000;
// Aviso do menu de exportar e do menu do nó; a ação opcional (ex.: "Ver") fecha o aviso ao ser usada.
interface ExportMessage {
  ok: boolean;
  text: string;
  action?: { label: string; run: () => void };
}
const NOTE_MESSAGE_MS = 3000;
const NOTE_UNDO_MS = 8000;
const SHOW_FRAMEWORK_KEY = 'rastro.showFrameworkCalls';
// Painel da direita recolhido: mais espaço para o grafo; a escolha vale para os próximos levantamentos.
const PANEL_COLLAPSED_KEY = 'rastro.panelCollapsed';
// Mesmo ponto de quebra do survey.page.scss, abaixo do qual o painel do nó desce para baixo do desenho.
const NARROW_QUERY = '(max-width: 1000px)';
// UX-G-F09: a dica "Como funciona" da edição abre sozinha só na primeira vez.
export const EDIT_HINT_SEEN_KEY = 'rastro.editHintSeen';
// Falhas seguidas toleradas no polling antes de mostrar erro (rede piscando, API reiniciando).
const POLL_TOLERATED_FAILURES = 2;

@Component({
  selector: 'app-survey-page',
  imports: [RouterLink, TranslatePipe, FormsModule, FlowCanvasComponent, ConsolidatedViewComponent, FlowMapViewComponent, BusinessViewComponent, NodePanelComponent, ValidationDialogComponent, C4DialogComponent, ConfirmDialogComponent, AddToBoardDialogComponent, DialogDirective, AiDisclosureComponent],
  templateUrl: './survey.page.html',
  styleUrl: './survey.page.scss',
  host: { '(document:keydown.escape)': 'onEscape()', '(document:keydown)': 'onKeydown($event)' },
})
export class SurveyPage {
  // O "←" da tela do banco volta ao levantamento de onde se saiu.
  protected dbOriginQuery(surveyId: string): Record<string, string> {
    return { [DB_SURVEY_ORIGIN_PARAM]: surveyId };
  }

  private readonly api = inject(RastroApi);
  private readonly i18n = inject(LanguageService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly canvas = viewChild(FlowCanvasComponent);
  private readonly mapView = viewChild(FlowMapViewComponent);
  private readonly businessView = viewChild(BusinessViewComponent);
  private pollTimer?: ReturnType<typeof setTimeout>;
  private failures = 0;

  // Vem da rota (surveys/:id).
  readonly id = input.required<string>();
  // ?comparar=<id>: levantamento antigo desenhado por cima deste (Fase 12).
  readonly comparar = input<string | undefined>();
  // ?passo=<id do nó>: abre o fluxo com o passo escolhido (o "Ver no fluxo" da visão consolidada).
  readonly passo = input<string | undefined>();
  private passoApplied: string | null = null;

  protected readonly detail = signal<SurveyDetail | null>(null);
  // UX-G-L23: título da aba com o ponto de entrada, assim que o levantamento chega.
  private readonly pageTitle = inject(SurveyPageTitle);
  private readonly titleSync = effect(() => this.pageTitle.set('route.survey', this.detail()?.survey.entryPoint));
  protected readonly error = signal<string | null>(null);
  // Erro depois que a tela já carregou: faixa no topo, a tela (e a edição) continuam.
  protected readonly softError = signal<string | null>(null);
  protected readonly confirmDiscard = signal(false);
  protected readonly confirmDelete = signal(false);
  protected readonly addBoardOpen = signal(false);
  protected readonly deleting = signal(false);
  // Menus da barra: "Comparar" e "Exportar".
  protected readonly exportOpen = signal(false);
  protected readonly hintOpen = signal(false);
  // Mudanças no rascunho salvo: com elas, dá para validar sem entrar na edição (UX-71).
  protected readonly savedChanges = signal(0);
  // O prompt desta versão do rascunho já foi gerado: o próximo passo lógico é validar (UX-72).
  protected readonly promptReady = signal(false);
  protected readonly aiActive = computed(() => this.ai.active());
  protected readonly staleness = signal<SurveyStaleness | null>(null);
  // Sistemas que mudaram desde o levantamento (Fase 25): o repositório principal e os trechos.
  // Só diz os nomes quando o fluxo passa por mais de um sistema.
  protected readonly staleSystems = computed(() => {
    const stale = this.staleness();
    const survey = this.detail()?.survey;
    if (!stale?.stale || !survey || (stale.segments ?? []).length === 0) return [];
    const main = stale.surveyCommit !== stale.currentCommit ? [survey.repository] : [];
    return [...main, ...stale.segments.filter((s) => s.stale).map((s) => s.repository)];
  });
  // Sistemas do fluxo com a linguagem de cada um, para a moldura e o painel (Fase 26).
  protected readonly systems = computed(() => {
    const survey = this.detail()?.survey;
    return survey ? surveySystems(survey) : [];
  });
  protected readonly selectedId = signal<string | null>(null);
  // Desenho consolidado (Fase 28) e mapa (Fase 31): o quarto e o quinto desenho do botão, no lugar
  // do canvas. Na edição e na comparação o canvas volta, porque só ele mostra o rascunho e as diferenças.
  private readonly drawing = signal(readLayoutDirection());
  protected readonly consolidated = computed(() => this.drawing() === LayoutDirection.Consolidated);
  protected readonly mapped = computed(() => this.drawing() === LayoutDirection.Map);
  // Visão de negócio (Fase 57): o sexto desenho, também no lugar do canvas.
  protected readonly business = computed(() => this.drawing() === LayoutDirection.Business);
  protected readonly showConsolidated = computed(() => this.consolidated() && !this.editing() && !this.comparison());
  protected readonly showMap = computed(() => this.mapped() && !this.editing() && !this.comparison());
  protected readonly showBusiness = computed(() => this.business() && !this.editing() && !this.comparison());
  // Resumo do fluxo (Fase 23): vem do canvas, que o monta junto com o desenho.
  protected readonly summary = signal<FlowSummary | null>(null);
  protected readonly resurveying = signal(false);
  protected readonly deciding = signal(false);
  protected readonly filling = signal(false);
  protected readonly fillMessages = signal<string[]>([]);
  private readonly ai = inject(AiStatusService);
  protected readonly canFillHoles = computed(() => aiCan(this.ai.status(), AiUse.FillHoles));
  protected readonly canDescribe = computed(() => aiCan(this.ai.status(), AiUse.DescribeRules));
  protected readonly canSuggest = computed(() => aiCan(this.ai.status(), AiUse.Suggestions));
  // Sugestão da IA para o achado do prompt de refatoração aberto.
  private refactorIndex: number | null = null;
  protected readonly suggestion = signal<RefactorSuggestion | null>(null);
  protected readonly suggesting = signal(false);
  protected readonly suggestionError = signal<string | null>(null);
  protected readonly suggestionAppended = signal(false);
  protected readonly providerLabel = AI_PROVIDER_LABEL;
  protected readonly describing = signal(false);
  protected readonly describeMessage = signal<string | null>(null);
  protected readonly decisionError = signal<string | null>(null);

  protected readonly SurveyStatus = SurveyStatus;
  protected readonly statusKey = SURVEY_STATUS_LABEL_KEY;
  protected readonly statusSlug = SURVEY_STATUS_SLUG;
  protected readonly shortCommit = shortCommit;
  protected readonly shortDate = shortDate;
  protected readonly failureText = surveyFailureText;

  // Chamadas do .NET (LINQ, EF Core, cache...) escondidas por padrão; a escolha fica no navegador.
  protected readonly showFramework = signal(readShowFramework());
  protected readonly panelCollapsed = signal(readPanelCollapsed());
  // Tela estreita com a visão de negócio: o painel do nó desce e ocupa 45% da altura, e a visão tem o painel do passo
  // dela, também embaixo; os dois juntos deixavam o desenho com poucos pixels (passo de UX da release 0.11.0). Ali o
  // painel do nó começa recolhido, sem mudar a preferência gravada; a aba "Painel" abre quando a pessoa pede.
  private readonly narrow = signal(matchNarrow());
  private readonly panelOpenedInBusiness = signal(false);
  private readonly autoCollapse = computed(() => this.narrow() && this.showBusiness() && !this.panelOpenedInBusiness());
  protected readonly panelHidden = computed(() => this.panelCollapsed() || this.autoCollapse());

  protected readonly frameworkCount = computed(() => (this.detail()?.graph?.nodes ?? []).filter(isFrameworkCall).length);

  // Edição (Fase 7): o rascunho é separado do grafo extraído; desfazer é uma pilha de rascunhos.
  protected readonly editing = signal(false);
  protected readonly draft = signal<FlowGraph | null>(null);
  private readonly undoStack = signal<FlowGraph[]>([]);
  private readonly redoStack = signal<FlowGraph[]>([]);
  protected readonly dirty = signal(false);
  protected readonly savingDraft = signal(false);
  protected readonly draftMessage = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly canUndo = computed(() => this.undoStack().length > 0);
  protected readonly canRedo = computed(() => this.redoStack().length > 0);
  protected readonly changes = computed(() => (this.draft() ? changeCount(this.draft()!) : 0));
  protected readonly prompt = signal<ChangePrompt | null>(null);
  // O diálogo de prompt serve à mudança desenhada (Fase 8) e à refatoração de um achado (Fase 10).
  protected readonly promptKind = signal<'change' | 'refactor'>('change');
  // Edição usa o mesmo arranjo da leitura: entrar para editar não desfaz o que foi arrastado.
  protected readonly layoutKey = computed(() => this.id() + (this.focusId() ? '|' + this.focusId() : ''));
  protected readonly promptLoading = signal(false);
  protected readonly promptCopy = new CopyFeedback();
  protected readonly menuCopy = new CopyFeedback();
  private readonly editor = inject(EditorLinks);
  // Erro de gerar ou regerar com o diálogo aberto: aparece dentro dele, não atrás.
  protected readonly promptError = signal<string | null>(null);
  // Validação da mudança (Fase 9): a última do levantamento e o diálogo com o resultado.
  protected readonly validation = signal<ValidationView | null>(null);
  protected readonly validationOpen = signal(false);
  protected readonly validationBusy = signal(false);
  protected readonly validationMessage = signal<string | null>(null);
  protected readonly validationDocument = signal<string | null>(null);
  protected readonly verdictKey = VALIDATION_VERDICT_LABEL_KEY;
  protected readonly ValidationStatus = ValidationStatus;
  private validationTimer?: ReturnType<typeof setTimeout>;
  protected showNewStep = false;
  protected readonly newStepErrors = signal<{ label?: string; intent?: string }>({});
  protected newLabel = '';
  protected newKind: NodeKind.Method | NodeKind.Decision = NodeKind.Method;
  protected newIntent = '';
  protected readonly NodeKind = NodeKind;

  // Busca no grafo (Ctrl+F): os passos que batem acendem; Enter pula para o próximo.
  protected readonly searchOpen = signal(false);
  protected readonly searchQuery = signal('');
  protected readonly searchIndex = signal(0);
  protected readonly matches = computed(() => {
    const graph = this.graph();
    return graph ? searchNodes(graph, this.searchQuery()) : [];
  });
  // Até SEARCH_LIST_LIMIT ocorrências, com o nome do passo e o dono (UX-G-F22).
  protected readonly searchList = computed(() => {
    const graph = this.graph();
    if (!graph || !this.searchOpen()) return [];
    const byId = new Map(graph.nodes.map((node) => [node.id, node]));
    return this.matches()
      .slice(0, SEARCH_LIST_LIMIT)
      .flatMap((id, index) => {
        const node = byId.get(id);
        if (!node) return [];
        const { owner, name } = splitLabel(node);
        return [{ id, index, owner: owner.replace(/\.$/, ''), name }];
      });
  });

  protected pickSearchResult(id: string, index: number): void {
    this.searchIndex.set(index);
    this.select(id, false);
  }

  protected readonly matchIds = computed(() => (this.searchOpen() && this.matches().length > 0 ? new Set(this.matches()) : null));
  protected readonly currentMatch = computed(() => {
    const matches = this.matches();
    return this.searchOpen() && matches.length > 0 ? matches[Math.min(this.searchIndex(), matches.length - 1)] : null;
  });

  // "Ver somente este fluxo": recorte no passo escolhido pelo menu de contexto.
  protected readonly focusId = signal<string | null>(null);
  // Passos com o que vem depois recolhido num nó "+N passos" (leitura por camadas).
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());
  // Anotações dos passos (por id do passo).
  protected readonly notes = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly crossFlow = signal<CrossFlowFinding[]>([]);
  // Comparar antes e depois: o outro levantamento e os do mesmo método que dá para escolher.
  protected readonly compareDetail = signal<SurveyDetail | null>(null);
  protected readonly compareError = signal<string | null>(null);
  protected readonly compareOpen = signal(false);
  protected readonly siblings = signal<SurveySummary[]>([]);
  protected readonly comparison = computed(() => {
    const before = this.compareDetail()?.graph;
    const after = this.detail()?.graph;
    return before && after && this.comparar() ? compareGraphs(before, after) : null;
  });
  protected readonly savingNote = signal(false);
  protected readonly noteMessage = signal<{ ok: boolean; text: string } | null>(null);
  // Anotação apagada que ainda dá para trazer de volta (por alguns segundos).
  protected readonly noteUndo = signal<{ nodeId: string; text: string } | null>(null);
  private noteTimer?: ReturnType<typeof setTimeout>;
  // Caminho entre dois passos: A pelo menu, B pelo próximo clique (ou pelo menu).
  protected readonly pathFrom = signal<string | null>(null);
  protected readonly pathTo = signal<string | null>(null);
  protected readonly between = computed(() => {
    const graph = this.graph();
    const from = this.pathFrom();
    const to = this.pathTo();
    return graph && from && to ? pathsBetween(graph, from, to) : null;
  });
  protected readonly c4Open = signal(false);
  protected readonly menu = signal<{ nodeId: string; x: number; y: number } | null>(null);
  // "Mostrar na visão de negócio" só aparece para o nó que ainda não é passo-chave: falso ou nulo (ainda sem
  // resposta, ou a visão não veio) esconde o item; só o "ainda não é passo-chave" confirmado o mostra.
  protected readonly menuNotKeyStep = signal(false);
  // Passos-chave da visão de negócio por levantamento: o menu do nó não consulta a rota a cada abertura.
  private readonly businessNodeIds = new Map<string, ReadonlySet<string>>();

  // Contagem da comparação pelo que está na tela (chamadas do .NET escondidas não contam).
  protected readonly visibleChanges = computed(() => {
    const nodes = this.comparison() ? (this.graph()?.nodes ?? []) : [];
    return {
      added: nodes.filter((n) => n.change === ChangeKind.Added).length,
      removed: nodes.filter((n) => n.change === ChangeKind.Removed).length,
    };
  });

  protected readonly graph = computed(() => {
    const source = this.editing() ? this.draft() : (this.comparison()?.graph ?? this.detail()?.graph ?? null);
    const graph = source && !this.showFramework() ? withoutFrameworkCalls(source) : source;
    const focus = this.focusId();
    const focused = graph && focus ? focusOn(graph, focus) : graph;
    // O consolidado, o mapa e a visão de negócio não recolhem passos: o painel precisa achar qualquer passo deles.
    if (this.showConsolidated() || this.showMap() || this.showBusiness()) return focused;
    return focused ? collapseAfter(focused, this.collapsed()) : focused;
  });

  protected readonly menuNode = computed(() => {
    const id = this.menu()?.nodeId;
    return id ? (this.graph()?.nodes.find((n) => n.id === id) ?? null) : null;
  });

  protected readonly menuEditorLink = computed(() => {
    const node = this.menuNode();
    // O passo de outro sistema abre no repositório dele (FlowNode.System), como no painel do nó.
    const repository = node?.system ?? this.detail()?.survey.repository;
    return node?.location && repository ? this.editor.link(repository, node.location.file, node.location.line) : null;
  });

  protected readonly menuEditorIsVsCode = computed(() => isVsCodeLink(this.menuEditorLink()));

  protected readonly menuCollapsed = computed(() => {
    const id = this.menu()?.nodeId;
    return !!id && this.collapsed().has(id);
  });

  protected readonly focusLabel = computed(() => {
    const id = this.focusId();
    return id ? (this.detail()?.graph?.nodes.find((n) => n.id === id)?.label ?? id) : null;
  });

  protected readonly selectedLabel = computed(() => {
    const id = this.selectedId();
    return id ? (this.graph()?.nodes.find((n) => n.id === id)?.label ?? null) : null;
  });

  protected readonly menuLabel = computed(() => {
    const id = this.menu()?.nodeId;
    return id ? (this.graph()?.nodes.find((n) => n.id === id)?.label ?? '') : '';
  });

  protected readonly trail = computed(() => {
    const between = this.between();
    if (between) return between;
    const graph = this.graph();
    const id = this.selectedId();
    if (!graph || !id) return null;
    const trail = trailTo(graph, id);
    // No passo em foco, o recorte inteiro é o fluxo dele: nada fica apagado.
    if (id !== this.focusId()) return trail;
    return { ...trail, nodeIds: new Set(graph.nodes.map((n) => n.id)), edgeIndexes: new Set(graph.edges.map((_, i) => i)) };
  });

  constructor() {
    this.editor.ensureLoaded();
    const narrowQuery = matchNarrowQuery();
    const onNarrow = (event: MediaQueryListEvent) => this.narrow.set(event.matches);
    narrowQuery?.addEventListener('change', onNarrow);
    inject(DestroyRef).onDestroy(() => {
      narrowQuery?.removeEventListener('change', onNarrow);
      // Os modelos do editor valem só para este levantamento.
      this.editor.clearSurveyLinks();
      clearTimeout(this.pollTimer);
      clearTimeout(this.validationTimer);
    });

    // Levantamento para comparar: carrega quando a rota traz ?comparar=.
    effect(() => {
      const other = this.comparar();
      untracked(() => {
        this.compareDetail.set(null);
        this.compareError.set(null);
        if (!other) return;
        this.api.survey(other).subscribe({
          next: (detail) => this.compareDetail.set(detail),
          error: (error) => this.compareError.set(apiErrorMessage(error)),
        });
      });
    });

    // ?passo=: escolhe o passo quando o fluxo carrega (uma vez por passo pedido).
    effect(() => {
      const passo = this.passo();
      const graph = this.graph();
      if (!passo || !graph || this.passoApplied === passo || !graph.nodes.some((n) => n.id === passo)) return;
      this.passoApplied = passo;
      untracked(() => {
        if (this.panelHidden()) this.togglePanel();
        this.select(passo, false);
      });
    });

    // A rota reaproveita o componente ao trocar de levantamento: recarrega a cada id.
    effect(() => {
      this.id();
      untracked(() => {
        this.detail.set(null);
        this.staleness.set(null);
        this.selectedId.set(null);
        this.validation.set(null);
        this.validationOpen.set(false);
        this.focusId.set(null);
        this.collapsed.set(new Set());
        this.notes.set(new Map());
        this.clearPath();
        this.menu.set(null);
        this.retry();
      });
    });
  }

  // Troca de desenho pelo botão do canvas, do consolidado ou do mapa (Fases 28 e 31).
  protected onDirection(direction: LayoutDirection): void {
    // A visão de negócio pode ter mudado enquanto outro desenho estava na tela: o menu do nó consulta de novo.
    this.businessNodeIds.clear();
    // O painel aberto pela aba na visão de negócio em tela estreita vale só enquanto ela está na tela.
    this.panelOpenedInBusiness.set(false);
    saveLayoutDirection(direction);
    this.drawing.set(direction);
  }

  // Clique num passo do consolidado ou num bloco do mapa: o contexto é o painel do passo, então ele abre.
  protected pickInDrawing(id: string | null): void {
    if (id && this.panelHidden()) this.togglePanel();
    this.select(id);
  }

  protected readonly drawingViews = DRAWING_VIEWS;

  // Seletor da barra: o Grafo é tudo o que não é mapa nem consolidado.
  protected drawingPressed(direction: LayoutDirection): boolean {
    if (direction === LayoutDirection.Map) return this.mapped();
    if (direction === LayoutDirection.Consolidated) return this.consolidated();
    if (direction === LayoutDirection.Business) return this.business();
    return !this.mapped() && !this.consolidated() && !this.business();
  }

  // Texto lido pelo leitor de tela ao trocar de desenho (a troca não move o foco).
  protected readonly drawingAnnouncement = signal('');

  protected showDrawing(direction: LayoutDirection): void {
    if (this.drawingPressed(direction)) return;
    this.onDirection(direction);
    const view = DRAWING_VIEWS.find((v) => v.direction === direction);
    if (view) this.drawingAnnouncement.set(this.i18n.t('survey.view.announce', { name: this.i18n.t(view.label) }));
  }

  // Mapa por sistemas e consolidado não têm o desenho do grafo: PNG, SVG e Mermaid só valem no grafo e na
  // visão de negócio. O draw.io vale também no mapa, mas não no consolidado.
  protected readonly exportNeedsGraph = computed(() => this.showMap() || this.showConsolidated());
  protected readonly drawioNeedsGraph = computed(() => this.showConsolidated());

  // Atalho do painel: liga o mapa do fluxo e abre a aba Perguntas.
  protected readonly mapTabRequest = signal<{ tab: MapTab; seq: number } | null>(null);

  protected openQuestions(): void {
    if (!this.mapped()) this.onDirection(LayoutDirection.Map);
    this.mapTabRequest.update((current) => ({ tab: MapTab.Questions, seq: (current?.seq ?? 0) + 1 }));
  }

  protected toggleFramework(): void {
    const next = !this.showFramework();
    this.showFramework.set(next);
    if (!next && this.selectedId() && !this.graph()?.nodes.some((n) => n.id === this.selectedId())) this.selectedId.set(null);
    try {
      localStorage.setItem(SHOW_FRAMEWORK_KEY, String(next));
    } catch {
      // Armazenamento bloqueado: a escolha vale só nesta visita.
    }
  }

  protected togglePanel(): void {
    if (this.autoCollapse()) {
      // Abrir pela aba na tela estreita com a visão de negócio vale até sair dela.
      this.panelOpenedInBusiness.set(true);
      if (!this.panelCollapsed()) return;
    } else if (this.narrow() && this.showBusiness() && !this.panelCollapsed()) {
      this.panelOpenedInBusiness.set(false);
      return;
    }
    const next = !this.panelCollapsed();
    this.panelCollapsed.set(next);
    try {
      localStorage.setItem(PANEL_COLLAPSED_KEY, String(next));
    } catch {
      // Armazenamento bloqueado: a escolha vale só nesta visita.
    }
  }

  // Esc fecha o que está por cima: diálogos, menus, depois busca e caminho, por fim seleção e recorte.
  protected onEscape(): void {
    if (this.confirmDiscard()) this.confirmDiscard.set(false);
    else if (this.confirmDelete()) this.confirmDelete.set(false);
    else if (this.addBoardOpen()) this.addBoardOpen.set(false);
    else if (this.c4Open()) this.c4Open.set(false);
    else if (this.validationOpen()) this.validationOpen.set(false);
    else if (this.prompt()) this.closePrompt();
    else if (this.menu()) this.closeMenu();
    else if (this.exportOpen() || this.compareOpen()) this.closePopovers();
    else if (this.searchOpen()) this.closeSearch();
    else if (this.pathFrom()) this.clearPath();
    else if (this.selectedId()) this.closePanel(this.selectedId()!);
    else if (this.focusId()) this.showAll();
  }

  // UX-95: menus suspensos pelo teclado; o foco entra no 1º item e volta ao botão ao fechar.
  private popoverOpener: HTMLElement | null = null;

  protected togglePopover(which: 'export' | 'compare'): void {
    const target = which === 'export' ? this.exportOpen : this.compareOpen;
    const opening = !target();
    this.exportOpen.set(false);
    this.compareOpen.set(false);
    if (!opening) return;
    this.popoverOpener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    target.set(true);
    if (which === 'compare') this.loadSiblings();
    // Mesmo atraso da busca: o Foblex devolve o foco ao item de acessibilidade dele logo depois.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${which}-menu button`)?.focus(), SEARCH_FOCUS_DELAY_MS);
  }

  protected closePopovers(): void {
    this.exportOpen.set(false);
    this.compareOpen.set(false);
    this.popoverOpener?.focus();
    this.popoverOpener = null;
  }

  // fromPick: o usuário escolheu o passo (clique, teclado no nó, painel). Busca e "próximo não
  // resolvido" não contam como escolha do destino de um caminho.
  protected select(id: string | null, fromPick = true): void {
    if (id !== this.selectedId()) this.noteMessage.set(null);
    // Clicar no "+N passos" expande e volta ao passo que foi recolhido.
    const owner = id ? collapsedOwner(id) : null;
    if (owner) {
      this.toggleCollapse(owner);
      id = owner;
    }
    // Escolhendo o destino do caminho: o próximo passo clicado é o B.
    if (fromPick && id && this.pathFrom() && !this.pathTo() && id !== this.pathFrom()) this.pathTo.set(id);
    if (id !== this.selectedId()) {
      this.fillMessages.set([]);
      this.describeMessage.set(null);
    }
    this.selectedId.set(id);
  }

  protected readonly hiddenMatches = computed(() => {
    const full = this.detail()?.graph;
    if (!this.searchOpen() || !full || !this.searchQuery().trim()) return 0;
    return Math.max(0, searchNodes(full, this.searchQuery()).length - this.matches().length);
  });

  // Mostra tudo (recorte, recolhidos e chamadas do .NET) para a busca achar em todo o fluxo.
  protected searchEverywhere(): void {
    this.showAll();
    this.expandAll();
    if (!this.showFramework()) this.toggleFramework();
  }

  protected openSearch(): void {
    this.searchOpen.set(true);
    // O Foblex devolve o foco ao item de acessibilidade dele logo depois: foca o campo em seguida.
    setTimeout(() => {
      const input = this.host.nativeElement.querySelector<HTMLInputElement>('#graph-search');
      input?.focus();
      input?.select();
    }, SEARCH_FOCUS_DELAY_MS);
  }

  protected closeSearch(): void {
    this.searchOpen.set(false);
    const current = this.selectedId();
    if (current) this.focusNodeElement(current);
  }

  // Digitar só destaca (o canvas centraliza o atual); Enter seleciona.
  protected onSearchInput(query: string): void {
    this.searchQuery.set(query);
    this.searchIndex.set(0);
  }

  // Enter vai para o próximo, Shift+Enter para o anterior (dá a volta nas pontas).
  // Enter no atual ainda não escolhido escolhe ele; depois anda (e dá a volta nas pontas).
  protected stepSearch(direction: 1 | -1): void {
    const matches = this.matches();
    if (matches.length === 0) return;
    const current = matches[Math.min(this.searchIndex(), matches.length - 1)];
    if (direction === 1 && this.selectedId() !== current) {
      this.select(current, false);
      return;
    }
    const next = (this.searchIndex() + direction + matches.length) % matches.length;
    this.searchIndex.set(next);
    this.select(matches[next], false);
  }

  // Fechar o painel devolve o foco ao passo no grafo.
  protected closePanel(nodeId: string): void {
    this.select(null);
    this.focusNodeElement(nodeId);
  }

  // Contagem da barra: leva ao próximo não resolvido, como o botão do canvas.
  // Fora do grafo (consolidado, mapa), volta ao grafo antes (UX-G-F03).
  protected nextHole(): void {
    const canvas = this.canvas();
    if (canvas) {
      canvas.nextHole();
      return;
    }
    this.onDirection(LayoutDirection.Horizontal);
    setTimeout(() => this.canvas()?.nextHole(), CANVAS_BACK_DELAY_MS);
  }

  // Exportar o recorte: imagem e Mermaid do que está na tela (inteiro, recortado ou recolhido).
  protected readonly exportMessage = signal<ExportMessage | null>(null);

  private exportTimer?: ReturnType<typeof setTimeout>;

  private showExport(message: ExportMessage): void {
    this.exportMessage.set(message);
    clearTimeout(this.exportTimer);
    // O erro fica até a pessoa fechar (quem lê devagar ou usa leitor de tela não o perde); o sucesso some sozinho.
    if (message.ok) this.exportTimer = setTimeout(() => this.exportMessage.set(null), EXPORT_MESSAGE_MS);
  }

  protected dismissExportMessage(): void {
    clearTimeout(this.exportTimer);
    this.exportMessage.set(null);
  }

  protected exportImage(format: 'png' | 'svg'): void {
    if (this.exportNeedsGraph()) return;
    this.closePopovers();
    const image = this.showBusiness() ? this.businessView()?.exportSvg() : this.canvas()?.exportSvg();
    if (!image) {
      this.showExport({ ok: false, text: this.notDrawnText() });
      return;
    }
    const name = this.exportName();
    if (format === 'svg') {
      downloadBlob(new Blob([image.svg], { type: SVG_MIME }), `${name}.svg`);
      this.showExport({ ok: true, text: this.i18n.t('survey.export.started', { file: `${name}.svg` }) });
      return;
    }
    svgToPng(image.svg, image.width, image.height).then(
      (blob) => {
        downloadBlob(blob, `${name}.png`);
        this.showExport({ ok: true, text: this.i18n.t('survey.export.started', { file: `${name}.png` }) });
      },
      (error: Error) => this.showExport({ ok: false, text: this.i18n.t('survey.export.imageFailed', { message: error.message }) }),
    );
  }

  protected exportMermaid(download: boolean): void {
    if (this.exportNeedsGraph()) return;
    this.closePopovers();
    const text = this.showBusiness() ? this.businessView()?.exportMermaid() : this.canvas()?.exportMermaid();
    if (!text) {
      this.showExport({ ok: false, text: this.notDrawnText() });
      return;
    }
    if (download) {
      downloadBlob(new Blob([text], { type: MERMAID_MIME }), `${this.exportName()}.mmd`);
      this.showExport({ ok: true, text: this.i18n.t('survey.export.started', { file: `${this.exportName()}.mmd` }) });
    } else {
      this.menuCopy.copy(mermaidFenced(text));
    }
  }

  // draw.io (Fase 54): o grafo ou o mapa por sistemas, o que estiver na tela. Cada balão leva o link para o código
  // no host (Fase 56), quando o remote do repositório é reconhecido.
  protected exportDrawio(): void {
    if (this.drawioNeedsGraph()) return;
    this.closePopovers();
    const id = this.detail()?.survey.id;
    // Sem nada desenhado, avisa já: não vale pedir os links à API.
    if (!id || !this.drawioView()) {
      this.showExport({ ok: false, text: this.notDrawnText() });
      return;
    }
    this.api.links(id).subscribe({
      next: (links) => this.downloadDrawio(hostLinkResolver(links)),
      // Sem os links (rota fora do ar), o arquivo sai do mesmo jeito, só sem o link de cada balão: o link é um
      // acréscimo e não deve impedir a exportação.
      error: () => this.downloadDrawio(undefined, true),
    });
  }

  // O componente que desenha o que está na tela (mapa, visão de negócio ou grafo), se já existe.
  private drawioView(): { exportDrawio(name: string, linkOf?: HostLinkOf): string | null } | undefined {
    return this.showMap() ? this.mapView() : this.showBusiness() ? this.businessView() : this.canvas();
  }

  private downloadDrawio(linkOf: HostLinkOf | undefined, withoutLinks = false): void {
    const name = this.exportName();
    const xml = this.drawioView()?.exportDrawio(name, linkOf);
    if (!xml) {
      this.showExport({ ok: false, text: this.notDrawnText() });
      return;
    }
    downloadBlob(new Blob([xml], { type: DRAWIO_MIME }), `${name}.drawio`);
    // Sem a rota de links, o arquivo sai sem o link de cada balão: o aviso diz isso em vez de parecer completo.
    const key = withoutLinks ? 'survey.export.startedNoLinks' : 'survey.export.started';
    this.showExport({ ok: true, text: this.i18n.t(key, { file: `${name}.drawio` }) });
  }

  // Aviso de "nada desenhado" com o nome do desenho ativo (mapa, visão de negócio ou grafo).
  private notDrawnText(): string {
    const key = this.showMap() ? 'survey.export.notDrawnMap' : this.showBusiness() ? 'survey.export.notDrawnBusiness' : 'survey.export.notDrawnGraph';
    return this.i18n.t(key);
  }

  private exportName(): string {
    const survey = this.detail()?.survey;
    const focus = this.focusId() ? this.i18n.t('survey.export.sliceSuffix') : '';
    return `${this.i18n.t('survey.export.filePrefix')}${fileSlug(survey?.entryPoint ?? this.i18n.t('survey.export.fileFallback'))}${focus}`;
  }

  protected runMessageAction(message: ExportMessage): void {
    clearTimeout(this.exportTimer);
    this.exportMessage.set(null);
    message.action?.run();
  }

  protected openC4(): void {
    this.closePopovers();
    this.c4Open.set(true);
  }

  protected requestDelete(): void {
    this.closePopovers();
    this.confirmDelete.set(true);
  }

  // Levantamento que falhou: volta ao formulário com o mesmo pedido, para corrigir o que deu errado.
  protected retryWithChanges(): void {
    const survey = this.detail()?.survey;
    if (!survey) return;
    const retry: CreateSurveyRequest = {
      repository: survey.repository,
      solutionPath: survey.solutionPath,
      entryPoint: survey.entryPoint,
      maxDepth: survey.maxDepth,
      ...(survey.crossRepositories?.length ? { crossRepositories: survey.crossRepositories } : {}),
    };
    this.router.navigate(['/'], { state: { retry } });
  }

  // Excluir o levantamento (Fase 13): some com rascunho, validações e propostas; volta à lista.
  protected deleteSurvey(): void {
    const entryPoint = this.detail()?.survey.entryPoint ?? '';
    this.deleting.set(true);
    this.api.deleteSurvey(this.id()).subscribe({
      next: () => {
        this.deleting.set(false);
        this.confirmDelete.set(false);
        this.router.navigate(['/'], { state: { deleted: entryPoint } });
      },
      error: (error) => {
        this.deleting.set(false);
        this.confirmDelete.set(false);
        this.softError.set(this.i18n.t('survey.deleteFailed', { message: apiErrorMessage(error) }));
      },
    });
  }

  protected toggleNewStep(): void {
    this.showNewStep = !this.showNewStep;
    this.newStepErrors.set({});
    if (this.showNewStep) setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('#new-label')?.focus());
  }

  // Confirmar ou descartar uma aresta proposta: a API devolve o grafo já atualizado.
  protected decide(decision: EdgeDecision): void {
    this.deciding.set(true);
    this.decisionError.set(null);
    this.api.decideEdge(this.id(), decision).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.deciding.set(false);
        if (!decision.confirm) this.selectedId.set(decision.from);
      },
      error: (error) => {
        this.decisionError.set(apiErrorMessage(error));
        this.deciding.set(false);
      },
    });
  }

  // Modo provedor: a IA propõe destinos para o buraco; o grafo volta atualizado.
  // Cada destino recusado vira o texto do motivo no idioma ativo; o resto (aceitos e avisos) segue como o servidor mandou.
  private fillTexts(result: FillHoleResult): string[] {
    const items = result.items ?? [];
    return result.messages.map((message, index) => {
      const item = items[index];
      if (!item || item.refusal === null || item.refusal === undefined) return message;
      return this.i18n.t('refusal.outcome', { target: item.target, reason: refusalText(item.refusal, item.refusalArgs, item.message) });
    });
  }

  protected fillHole(nodeId: string): void {
    this.filling.set(true);
    this.fillMessages.set([]);
    this.api.fillHole(this.id(), nodeId).subscribe({
      next: (response) => {
        this.detail.set(response.survey);
        this.fillMessages.set(this.fillTexts(response.result));
        this.filling.set(false);
        this.ai.refresh();
      },
      error: (error) => {
        this.fillMessages.set([apiErrorMessage(error)]);
        this.filling.set(false);
      },
    });
  }

  // Modo provedor: a IA escreve a regra do nó (ou as que faltam no fluxo inteiro).
  protected describe(request: { nodeId: string | null; overwrite: boolean }): void {
    this.describing.set(true);
    this.describeMessage.set(null);
    this.api.describeRules(this.id(), request.nodeId ? [request.nodeId] : null, request.overwrite).subscribe({
      next: (response) => {
        this.detail.set(response.survey);
        this.describeMessage.set(response.result.messages[0] ?? null);
        this.describing.set(false);
        this.ai.refresh();
      },
      error: (error) => {
        this.describeMessage.set(apiErrorMessage(error));
        this.describing.set(false);
      },
    });
  }

  protected startEditing(): void {
    const graph = this.detail()?.graph;
    if (!graph) return;
    this.draftMessage.set(null);
    this.api.draft(this.id()).subscribe({
      next: (saved) => {
        this.draft.set(saved ?? structuredClone(graph));
        this.undoStack.set([]);
        this.redoStack.set([]);
        this.dirty.set(false);
        this.editing.set(true);
        this.openHintFirstTime();
        if (saved) this.draftMessage.set({ ok: true, text: this.i18n.t('survey.draft.loaded') });
      },
      error: (error) => this.draftMessage.set({ ok: false, text: apiErrorMessage(error) }),
    });
  }

  // UX-G-F09: na primeira vez que se entra em "Editar fluxo", a dica abre sozinha e a escolha de ter visto fica guardada.
  private openHintFirstTime(): void {
    try {
      if (localStorage.getItem(EDIT_HINT_SEEN_KEY) === 'true') return;
      localStorage.setItem(EDIT_HINT_SEEN_KEY, 'true');
    } catch {
      // Armazenamento bloqueado: a dica abre só nesta visita.
    }
    this.hintOpen.set(true);
  }

  // Toda edição passa por aqui: empilha o estado anterior para o desfazer.
  protected apply(next: FlowGraph): void {
    const current = this.draft();
    if (!current || next === current) return;
    this.undoStack.update((stack) => [...stack.slice(-99), current]);
    this.redoStack.set([]);
    this.draft.set(next);
    this.dirty.set(true);
    this.promptReady.set(false);
    this.draftMessage.set(null);
  }

  protected undo(): void {
    const stack = this.undoStack();
    const current = this.draft();
    if (stack.length === 0 || !current) return;
    this.redoStack.update((redo) => [...redo, current]);
    this.undoStack.set(stack.slice(0, -1));
    this.draft.set(stack[stack.length - 1]);
    this.dirty.set(true);
  }

  protected redo(): void {
    const stack = this.redoStack();
    const current = this.draft();
    if (stack.length === 0 || !current) return;
    this.undoStack.update((undo) => [...undo, current]);
    this.redoStack.set(stack.slice(0, -1));
    this.draft.set(stack[stack.length - 1]);
    this.dirty.set(true);
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Ctrl+F abre a busca do grafo em vez da do navegador (que não enxerga os nós fora da tela).
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f' && this.graph()) {
      event.preventDefault();
      this.openSearch();
      return;
    }
    if (!this.editing() || !(event.ctrlKey || event.metaKey)) return;
    if (event.target instanceof Element && event.target.closest('input, textarea, select')) return;
    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) {
      event.preventDefault();
      this.undo();
    } else if (key === 'y' || (key === 'z' && event.shiftKey)) {
      event.preventDefault();
      this.redo();
    }
  }

  protected addNewStep(): void {
    const draft = this.draft();
    if (!draft) return;
    const errors: { label?: string; intent?: string } = {};
    if (!this.newLabel.trim()) errors.label = this.i18n.t('survey.newStep.errorName');
    if (!this.newIntent.trim()) errors.intent = this.i18n.t('survey.newStep.errorIntent');
    this.newStepErrors.set(errors);
    if (errors.label || errors.intent) {
      this.host.nativeElement.querySelector<HTMLElement>(errors.label ? '#new-label' : '#new-intent')?.focus();
      return;
    }
    const result = addStep(draft, this.newLabel, this.newKind, this.newIntent);
    const linked = this.selectedId() ? connect(result.draft, this.selectedId()!, result.id) : result.draft;
    this.apply(linked);
    this.selectedId.set(result.id);
    this.showNewStep = false;
    this.newLabel = '';
    this.newIntent = '';
  }

  protected onCreateEdge(link: { from: string; to: string }): void {
    if (collapsedOwner(link.from) || collapsedOwner(link.to)) return;
    if (this.draft()) this.apply(connect(this.draft()!, link.from, link.to));
  }

  protected onReassignEdge(change: { edge: FlowEdge; from?: string; to?: string }): void {
    if ([change.edge.from, change.edge.to, change.from, change.to].some((id) => id && collapsedOwner(id))) return;
    if (this.draft()) this.apply(reassign(this.draft()!, change.edge, { from: change.from, to: change.to }));
  }

  protected onRemoveEdge(edge: FlowEdge): void {
    if (this.draft()) this.apply(removeEdge(this.draft()!, edge));
  }

  protected onRemoveStep(nodeId: string): void {
    if (this.draft()) this.apply(removeStep(this.draft()!, nodeId));
  }

  protected onRestoreStep(nodeId: string): void {
    if (this.draft()) this.apply(restoreStep(this.draft()!, nodeId));
  }

  protected onSetIntent(change: { nodeId: string; intent: string }): void {
    if (this.draft()) this.apply(setIntent(this.draft()!, change.nodeId, change.intent));
  }

  protected onConnectTo(link: { from: string; to: string }): void {
    this.onCreateEdge(link);
  }

  protected saveDraft(then?: () => void): void {
    const draft = this.draft();
    if (!draft) return;
    this.savingDraft.set(true);
    this.api.saveDraft(this.id(), draft).subscribe({
      next: () => {
        this.savingDraft.set(false);
        this.dirty.set(false);
        this.savedChanges.set(changeCount(draft));
        this.draftMessage.set({ ok: true, text: this.i18n.t('survey.draft.saved') });
        then?.();
      },
      error: (error) => {
        this.savingDraft.set(false);
        this.draftMessage.set({ ok: false, text: apiErrorMessage(error) });
      },
    });
  }

  // Prompt da mudança: salva o rascunho pendente e pede o texto montado pelo Rastro.
  protected generatePrompt(): void {
    const load = () => {
      this.promptLoading.set(true);
      this.promptError.set(null);
      this.promptCopy.reset();
      this.api.changePrompt(this.id()).subscribe({
        next: (prompt) => {
          this.promptKind.set('change');
          this.prompt.set(prompt);
          this.promptLoading.set(false);
          this.promptReady.set(true);
        },
        error: (error) => {
          this.promptLoading.set(false);
          if (this.prompt()) this.promptError.set(apiErrorMessage(error));
          else this.draftMessage.set({ ok: false, text: apiErrorMessage(error) });
        },
      });
    };
    if (this.dirty() || this.changes() === 0) {
      if (this.changes() === 0) {
        this.draftMessage.set({ ok: false, text: this.i18n.t('survey.draft.needChangeForPrompt') });
        return;
      }
      this.saveDraft(load);
    } else {
      load();
    }
  }

  // O diálogo abre na hora, com "Montando o prompt…"; o texto chega em seguida.
  protected askSuggestion(): void {
    if (this.refactorIndex === null) return;
    this.suggesting.set(true);
    this.suggestionError.set(null);
    this.api.suggestRefactor(this.id(), this.refactorIndex).subscribe({
      next: (suggestion) => {
        this.suggesting.set(false);
        this.suggestion.set(suggestion);
        this.ai.refresh();
      },
      error: (error) => {
        this.suggesting.set(false);
        this.suggestionError.set(apiErrorMessage(error));
      },
    });
  }

  // Leva a sugestão para o fim do prompt, marcada para o agente conferir.
  protected appendSuggestion(): void {
    const suggestion = this.suggestion();
    const current = this.prompt();
    if (!suggestion || !current) return;
    const steps = suggestion.steps.map((step, i) => `${i + 1}. ${step}`).join('\n');
    this.prompt.set({
      ...current,
      prompt: `${current.prompt}\n\n${this.i18n.t('survey.prompt.appendedText', { provider: this.providerLabel[suggestion.provider] })}\n${suggestion.summary}\n${steps}`,
    });
    this.suggestion.set(null);
    this.suggestionAppended.set(true);
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('.prompt-text')?.focus());
  }

  protected openRefactorPrompt(index: number): void {
    this.refactorIndex = index;
    this.suggestionAppended.set(false);
    this.suggestion.set(null);
    this.suggestionError.set(null);
    this.promptKind.set('refactor');
    this.promptCopy.reset();
    this.promptError.set(null);
    this.promptLoading.set(true);
    this.prompt.set({ prompt: '', changes: 1 });
    this.api.refactorPrompt(this.id(), index).subscribe({
      next: (result) => {
        this.promptLoading.set(false);
        this.prompt.set({ prompt: result.prompt, changes: 1 });
      },
      error: (error) => {
        this.promptLoading.set(false);
        this.promptError.set(apiErrorMessage(error));
      },
    });
  }

  protected copyPrompt(): void {
    const text = this.prompt()?.prompt;
    if (text) this.promptCopy.copy(text);
  }

  protected openMenu(menu: { nodeId: string; x: number; y: number }): void {
    this.select(menu.nodeId);
    // Não deixa o menu sair da janela.
    this.menu.set(menu);
    this.menuNotKeyStep.set(false);
    const surveyId = this.detail()?.survey.id;
    if (surveyId) {
      const known = this.businessNodeIds.get(surveyId);
      if (known) {
        this.menuNotKeyStep.set(!known.has(menu.nodeId));
      } else {
        this.api.business(surveyId).subscribe({
          next: (flow) => {
            const ids = new Set(flow.steps.map((s) => s.nodeId));
            this.businessNodeIds.set(surveyId, ids);
            if (this.menu()?.nodeId === menu.nodeId) this.menuNotKeyStep.set(!ids.has(menu.nodeId));
          },
          // Sem a visão, o item some: o resto do menu segue igual.
          error: () => this.menuNotKeyStep.set(false),
        });
      }
    }
    setTimeout(() => {
      const element = this.host.nativeElement.querySelector<HTMLElement>('.context-menu');
      if (!element) return;
      const box = element.getBoundingClientRect();
      const margin = 8;
      const x = Math.max(margin, Math.min(menu.x, window.innerWidth - box.width - margin));
      const y = menu.y + box.height > window.innerHeight - margin ? Math.max(margin, menu.y - box.height) : menu.y;
      if (x !== menu.x || y !== menu.y) this.menu.set({ ...menu, x, y });
      element.querySelector<HTMLElement>('button')?.focus();
    });
  }

  // Fechar o menu devolve o foco ao passo de onde ele saiu.
  protected closeMenu(): void {
    const nodeId = this.menu()?.nodeId;
    this.menu.set(null);
    if (nodeId) this.focusNodeElement(nodeId);
  }

  // Setas, Home e End andam pelos itens do menu.
  protected onMenuKey(event: KeyboardEvent): void {
    const items = Array.from((event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('button'));
    const current = items.indexOf(document.activeElement as HTMLElement);
    const next =
      event.key === 'ArrowDown' ? (current + 1) % items.length
      : event.key === 'ArrowUp' ? (current - 1 + items.length) % items.length
      : event.key === 'Home' ? 0
      : event.key === 'End' ? items.length - 1
      : -1;
    // Tab sai do menu suspenso: fecha e o foco volta ao botão que o abriu (o Esc já faz isso pelo documento).
    if (event.key === 'Tab' && (this.exportOpen() || this.compareOpen())) {
      this.closePopovers();
      return;
    }
    if (next < 0) return;
    event.preventDefault();
    items[next]?.focus();
  }

  // "Mostrar na visão de negócio": grava o tipo Passo, confirmado, para o nó. A visão lê de novo ao abrir.
  protected showInBusinessView(): void {
    const nodeId = this.menu()?.nodeId;
    const surveyId = this.detail()?.survey.id;
    this.closeMenu();
    if (!nodeId || !surveyId) return;
    this.api.setBusinessKind(surveyId, nodeId, BusinessKind.Step).subscribe({
      next: () => {
        this.businessNodeIds.delete(surveyId);
        this.showExport({
          ok: true,
          text: this.i18n.t('survey.menu.businessShown'),
          action: { label: this.i18n.t('survey.menu.businessSee'), run: () => this.onDirection(LayoutDirection.Business) },
        });
      },
      error: (error) => this.showExport({ ok: false, text: apiErrorMessage(error) }),
    });
  }

  // "Ver no grafo" da visão de negócio: volta ao canvas com o nó escolhido (sem mudar o desenho guardado).
  protected showNodeInGraph(nodeId: string): void {
    this.businessNodeIds.clear();
    this.drawing.set(LayoutDirection.Horizontal);
    this.select(nodeId, false);
    this.focusNodeElement(nodeId);
  }

  protected focusOnNode(nodeId: string): void {
    // O recorte é do canvas: o mapa e a visão de negócio não o desenham, então cedem o lugar (sem mudar o desenho guardado).
    if (this.showMap() || this.showBusiness()) this.drawing.set(LayoutDirection.Horizontal);
    this.closeMenu();
    this.focusId.set(nodeId);
    this.select(nodeId);
  }

  private focusNodeElement(nodeId: string): void {
    // Sem rolar: o contêiner do grafo tem overflow escondido e rolaria a imagem para longe do Foblex.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`.flow-node[data-node-id="${CSS.escape(nodeId)}"]`)?.focus({ preventScroll: true }));
  }

  protected toggleCollapse(nodeId: string): void {
    this.canvas()?.centerAfterLayout(nodeId);
    this.collapsed.update((current) => {
      const next = new Set(current);
      if (next.has(nodeId)) next.delete(nodeId);
      else next.add(nodeId);
      return next;
    });
  }

  protected saveNote(change: { nodeId: string; text: string }): void {
    const previous = this.notes().get(change.nodeId);
    this.savingNote.set(true);
    this.noteMessage.set(null);
    this.noteUndo.set(null);
    clearTimeout(this.noteTimer);
    this.api.saveNote(this.id(), change.nodeId, change.text).subscribe({
      next: (saved) => {
        this.savingNote.set(false);
        if (!saved && previous) this.noteUndo.set({ nodeId: change.nodeId, text: previous });
        // O sucesso some sozinho; com "Desfazer", fica mais tempo.
        this.noteTimer = setTimeout(() => {
          this.noteMessage.set(null);
          this.noteUndo.set(null);
        }, saved ? NOTE_MESSAGE_MS : NOTE_UNDO_MS);
        this.notes.update((current) => {
          const next = new Map(current);
          if (saved) next.set(change.nodeId, saved.text);
          else next.delete(change.nodeId);
          return next;
        });
        this.noteMessage.set({ ok: true, text: this.i18n.t(saved ? 'survey.note.saved' : 'survey.note.deleted') });
      },
      error: (error) => {
        this.savingNote.set(false);
        this.noteMessage.set({ ok: false, text: apiErrorMessage(error) });
      },
    });
  }

  // "Comparar com…": os outros levantamentos prontos do mesmo método no mesmo repositório.
  protected readonly siblingsState = signal<'loading' | 'error' | 'ready'>('loading');

  protected loadSiblings(): void {
    const survey = this.detail()?.survey;
    this.siblingsState.set('loading');
    this.api.surveys().subscribe({
      next: (list) => {
        this.siblings.set(
          list.filter((s) => s.id !== survey?.id && s.repository === survey?.repository && s.entryPoint === survey?.entryPoint && s.status === SurveyStatus.Completed),
        );
        this.siblingsState.set('ready');
      },
      error: () => this.siblingsState.set('error'),
    });
  }

  protected compareWith(otherId: string | null): void {
    this.closePopovers();
    this.router.navigate(['/surveys', this.id()], { queryParams: otherId ? { comparar: otherId } : {} });
  }

  protected startPath(nodeId: string): void {
    this.pathFrom.set(nodeId);
    this.pathTo.set(null);
  }

  protected endPath(nodeId: string): void {
    this.pathTo.set(nodeId);
    this.select(nodeId);
  }

  protected clearPath(): void {
    this.pathFrom.set(null);
    this.pathTo.set(null);
  }

  private readonly labels = computed(() => new Map((this.detail()?.graph?.nodes ?? []).map((n) => [n.id, n.label])));

  protected nodeLabel(id: string | null): string {
    return id ? (this.labels().get(id) ?? id) : '';
  }

  protected openMenuInEditor(): void {
    const link = this.menuEditorLink();
    this.closeMenu();
    if (link) this.editor.open(link);
  }

  // "arquivo:linha", o mesmo que o "Copiar arquivo:linha" do painel copia.
  protected copyMenuLocation(): void {
    const node = this.menuNode();
    this.closeMenu();
    if (node?.location) this.menuCopy.copy(`${node.location.file}:${node.location.line}`);
  }

  protected expandAll(): void {
    this.collapsed.set(new Set());
  }

  protected showAll(): void {
    this.focusId.set(null);
  }

  // Valida a mudança: salva o rascunho pendente e pede a validação (roda na fila da API).
  protected validate(): void {
    if (this.editing() && this.changes() === 0) {
      this.draftMessage.set({ ok: false, text: this.i18n.t('survey.draft.needChangeForValidation') });
      return;
    }
    const request = () => {
      this.validationBusy.set(true);
      this.validationMessage.set(null);
      this.api.requestValidation(this.id()).subscribe({
        next: (validation) => {
          this.validationBusy.set(false);
          this.showValidation(validation);
        },
        error: (error) => {
          this.validationBusy.set(false);
          if (this.validationOpen()) this.validationMessage.set(apiErrorMessage(error));
          else this.draftMessage.set({ ok: false, text: apiErrorMessage(error) });
        },
      });
    };
    if (this.editing() && this.dirty()) this.saveDraft(request);
    else request();
  }

  protected openValidation(): void {
    const validation = this.validation();
    if (validation) this.showValidation(validation);
  }

  protected approveValidation(): void {
    const validation = this.validation();
    if (!validation) return;
    this.validationBusy.set(true);
    this.validationMessage.set(null);
    this.api.approveValidation(validation.id).subscribe({
      next: (approved) => {
        this.validationBusy.set(false);
        this.showValidation(approved);
      },
      error: (error) => {
        this.validationBusy.set(false);
        this.validationMessage.set(apiErrorMessage(error));
      },
    });
  }

  private showValidation(validation: ValidationView): void {
    this.validation.set(validation);
    this.validationOpen.set(true);
    this.validationDocument.set(null);
    if (validation.hasDocument) {
      this.api.validationDocument(validation.id).subscribe({
        next: (text) => this.validationDocument.set(text),
        error: () => this.validationDocument.set(null),
      });
    }
    this.pollValidation(validation);
  }

  private pollValidation(validation: ValidationView): void {
    clearTimeout(this.validationTimer);
    if (isValidationFinished(validation.status)) return;
    this.validationTimer = setTimeout(
      () =>
        this.api.validation(validation.id).subscribe({
          next: (next) => {
            if (this.validation()?.id !== next.id) return;
            this.validation.set(next);
            this.pollValidation(next);
          },
          // Rede piscando: tenta de novo no próximo ciclo.
          error: () => this.pollValidation(validation),
        }),
      POLL_MS,
    );
  }

  private loadLastValidation(): void {
    this.api.validations(this.id()).subscribe({
      next: (list) => {
        const last = list[0] ?? null;
        this.validation.set(last);
        if (last) this.pollValidation(last);
      },
      // Sem o histórico a tela segue; o botão de validar continua.
      error: () => this.validation.set(null),
    });
  }

  protected closePrompt(): void {
    this.prompt.set(null);
    this.promptError.set(null);
  }

  // Sair salva o que estiver pendente (não se perde edição por engano).
  protected exitEditing(): void {
    const leave = () => {
      this.editing.set(false);
      this.selectedId.set(null);
    };
    if (this.dirty()) this.saveDraft(leave);
    else leave();
  }

  // Descartar apaga no servidor e zera o desfazer: sempre pede confirmação com a contagem.
  protected requestDiscard(): void {
    this.confirmDiscard.set(true);
  }

  protected discardDraft(): void {
    this.confirmDiscard.set(false);
    const graph = this.detail()?.graph;
    if (!graph) return;
    this.api.deleteDraft(this.id()).subscribe({
      next: () => this.resetDraft(graph),
      error: (error) => {
        // 404: não havia rascunho salvo; descartar só zera o que está na tela.
        if (error instanceof HttpErrorResponse && error.status === 404) this.resetDraft(graph);
        else this.draftMessage.set({ ok: false, text: this.i18n.t('survey.draft.discardFailed', { message: apiErrorMessage(error) }) });
      },
    });
  }

  private resetDraft(graph: FlowGraph): void {
    this.draft.set(structuredClone(graph));
    this.undoStack.set([]);
    this.redoStack.set([]);
    this.dirty.set(false);
    this.savedChanges.set(0);
    this.promptReady.set(false);
    this.draftMessage.set({ ok: true, text: this.i18n.t('survey.draft.discarded') });
  }

  protected retry(): void {
    this.failures = 0;
    this.error.set(null);
    this.softError.set(null);
    this.load();
  }

  // Novo levantamento com os mesmos parâmetros, no commit atual.
  protected resurvey(): void {
    const survey = this.detail()?.survey;
    if (!survey) return;
    this.resurveying.set(true);
    this.api
      .createSurvey({
        repository: survey.repository,
        solutionPath: survey.solutionPath,
        entryPoint: survey.entryPoint,
        maxDepth: survey.maxDepth,
        ...(survey.crossRepositories?.length ? { crossRepositories: survey.crossRepositories } : {}),
      })
      .subscribe({
        next: (created) => {
          this.resurveying.set(false);
          this.router.navigate(['/surveys', created.id]);
        },
        error: (error) => {
          this.resurveying.set(false);
          this.softError.set(apiErrorMessage(error));
        },
      });
  }

  private load(): void {
    clearTimeout(this.pollTimer);
    this.api.survey(this.id()).subscribe({
      next: (detail) => {
        this.failures = 0;
        this.softError.set(null);
        this.detail.set(detail);
        if (!isFinished(detail.survey.status)) {
          this.pollTimer = setTimeout(() => this.load(), POLL_MS);
        } else if (detail.survey.status === SurveyStatus.Completed) {
          this.loadLastValidation();
          // Modelo do editor configurado (Rastro:Editor:UrlTemplate) para o menu e o painel do nó; sem a rota, fica o
          // link do VS Code pela pasta cadastrada.
          this.api.links(this.id()).subscribe({
            next: (links) => this.editor.useSurveyLinks(links),
            error: () => this.editor.clearSurveyLinks(),
          });
          this.api.crossFlow(this.id()).subscribe({
            next: (findings) => this.crossFlow.set(findings),
            // Sem a comparação entre fluxos a tela segue; a seção só não aparece.
            error: () => this.crossFlow.set([]),
          });
          this.api.notes(this.id()).subscribe({
            next: (notes) => this.notes.set(new Map(notes.map((n) => [n.nodeId, n.text]))),
            // Sem as anotações a tela segue; o painel só não mostra o texto salvo.
            error: () => this.notes.set(new Map()),
          });
          this.api.draft(this.id()).subscribe({
            next: (saved) => this.savedChanges.set(saved ? changeCount(saved) : 0),
            // Sem o rascunho a tela segue; validar continua dentro da edição.
            error: () => this.savedChanges.set(0),
          });
          this.api.staleness(this.id()).subscribe({
            next: (staleness) => this.staleness.set(staleness),
            // Sem git ou API fora: a tela segue sem o aviso de desatualizado.
            error: () => this.staleness.set(null),
          });
        }
      },
      error: (error) => {
        // Tela de erro só na carga inicial. Depois, a faixa avisa e o polling continua tentando.
        if (this.detail() === null) {
          this.error.set(apiErrorMessage(error));
          return;
        }
        if (++this.failures > POLL_TOLERATED_FAILURES) this.softError.set(apiErrorMessage(error));
        if (!isFinished(this.detail()!.survey.status)) this.pollTimer = setTimeout(() => this.load(), POLL_MS * 2);
      },
    });
  }
}

function readShowFramework(): boolean {
  try {
    return localStorage.getItem(SHOW_FRAMEWORK_KEY) === 'true';
  } catch {
    return false;
  }
}

function matchNarrowQuery(): MediaQueryList | null {
  try {
    return window.matchMedia(NARROW_QUERY);
  } catch {
    // Sem matchMedia (ambiente sem janela): vale a tela larga.
    return null;
  }
}

function matchNarrow(): boolean {
  return matchNarrowQuery()?.matches ?? false;
}

function readPanelCollapsed(): boolean {
  try {
    return localStorage.getItem(PANEL_COLLAPSED_KEY) === 'true';
  } catch {
    return false;
  }
}
