import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { plural, shortCommit, shortDate } from '../../core/format';
import { AiStatusService } from '../../core/ai-status.service';
import { CopyFeedback } from '../../core/copy-feedback';
import { EditorLinks } from '../../core/editor-links';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { addStep, changeCount, connect, reassign, removeEdge, removeStep, restoreStep, setIntent } from '../../core/graph/draft-editor';
import { trailTo } from '../../core/graph/trail';
import { surveySystems } from '../../core/graph/timeline-layout';
import { focusOn } from '../../core/graph/focus';
import { searchNodes } from '../../core/graph/search';
import { collapseAfter, collapsedOwner } from '../../core/graph/collapse';
import { pathsBetween } from '../../core/graph/between';
import { compareGraphs } from '../../core/graph/compare';
import { isFrameworkCall, withoutFrameworkCalls } from '../../core/graph/visibility';
import {
  AiUse,
  AI_PROVIDER_LABEL,
  RefactorSuggestion,
  ChangeKind,
  ChangePrompt,
  EdgeDecision,
  FlowEdge,
  FlowGraph,
  NodeKind,
  CrossFlowFinding,
  SURVEY_STATUS_LABEL,
  aiCan,
  SURVEY_STATUS_SLUG,
  SurveyDetail,
  SurveySummary,
  SurveyStaleness,
  SurveyStatus,
  VALIDATION_VERDICT_LABEL,
  ValidationStatus,
  ValidationView,
  isFinished,
  isValidationFinished,
  CreateSurveyRequest,
} from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { FlowCanvasComponent } from './flow-canvas.component';
import { ConsolidatedViewComponent } from './consolidated-view.component';
import { LayoutDirection, readLayoutDirection, saveLayoutDirection } from '../../core/graph/layout';
import { FlowSummary } from '../../core/graph/summary';
import { NodePanelComponent } from './node-panel.component';
import { ValidationDialogComponent } from './validation-dialog.component';
import { C4DialogComponent } from './c4-dialog.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { DialogDirective } from '../../core/dialog.directive';
import { HttpErrorResponse } from '@angular/common/http';

const POLL_MS = 2000;
const SEARCH_FOCUS_DELAY_MS = 120;
const EXPORT_MESSAGE_MS = 4000;
const NOTE_MESSAGE_MS = 3000;
const NOTE_UNDO_MS = 8000;
const SHOW_FRAMEWORK_KEY = 'rastro.showFrameworkCalls';
// Painel da direita recolhido: mais espaço para o grafo; a escolha vale para os próximos levantamentos.
const PANEL_COLLAPSED_KEY = 'rastro.panelCollapsed';
// Falhas seguidas toleradas no polling antes de mostrar erro (rede piscando, API reiniciando).
const POLL_TOLERATED_FAILURES = 2;

@Component({
  selector: 'app-survey-page',
  imports: [RouterLink, FormsModule, FlowCanvasComponent, ConsolidatedViewComponent, NodePanelComponent, ValidationDialogComponent, C4DialogComponent, ConfirmDialogComponent, DialogDirective],
  templateUrl: './survey.page.html',
  styleUrl: './survey.page.scss',
  host: { '(document:keydown.escape)': 'onEscape()', '(document:keydown)': 'onKeydown($event)' },
})
export class SurveyPage {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly canvas = viewChild(FlowCanvasComponent);
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
  protected readonly error = signal<string | null>(null);
  // Erro depois que a tela já carregou: faixa no topo, a tela (e a edição) continuam.
  protected readonly softError = signal<string | null>(null);
  protected readonly confirmDiscard = signal(false);
  protected readonly confirmDelete = signal(false);
  protected readonly deleting = signal(false);
  // Menus da barra: "Exportar" (leitura) e "Mais" (edição).
  protected readonly exportOpen = signal(false);
  protected readonly moreOpen = signal(false);
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
  // Desenho consolidado (Fase 28): o quarto desenho do botão, no lugar do canvas. Na edição e na
  // comparação o canvas volta, porque só ele mostra o rascunho e as diferenças.
  protected readonly consolidated = signal(readLayoutDirection() === LayoutDirection.Consolidated);
  protected readonly showConsolidated = computed(() => this.consolidated() && !this.editing() && !this.comparison());
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
  protected readonly statusLabel = SURVEY_STATUS_LABEL;
  protected readonly statusSlug = SURVEY_STATUS_SLUG;
  protected readonly shortCommit = shortCommit;
  protected readonly shortDate = shortDate;
  protected readonly plural = plural;

  // Chamadas do .NET (LINQ, EF Core, cache...) escondidas por padrão; a escolha fica no navegador.
  protected readonly showFramework = signal(readShowFramework());
  protected readonly panelCollapsed = signal(readPanelCollapsed());

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
  protected readonly verdictLabel = VALIDATION_VERDICT_LABEL;
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
    // O desenho consolidado não recolhe passos: o painel precisa achar qualquer passo dele.
    if (this.showConsolidated()) return focused;
    return focused ? collapseAfter(focused, this.collapsed()) : focused;
  });

  protected readonly menuNode = computed(() => {
    const id = this.menu()?.nodeId;
    return id ? (this.graph()?.nodes.find((n) => n.id === id) ?? null) : null;
  });

  protected readonly menuEditorLink = computed(() => {
    const node = this.menuNode();
    const repository = this.detail()?.survey.repository;
    return node?.location && repository ? this.editor.link(repository, node.location.file, node.location.line) : null;
  });

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
    inject(DestroyRef).onDestroy(() => {
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
        if (this.panelCollapsed()) this.togglePanel();
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

  // Troca de desenho pelo botão do canvas ou do consolidado (Fase 28).
  protected onDirection(direction: LayoutDirection): void {
    saveLayoutDirection(direction);
    this.consolidated.set(direction === LayoutDirection.Consolidated);
  }

  // Clique num passo do desenho consolidado: o contexto é o painel do passo, então ele abre.
  protected pickConsolidated(id: string | null): void {
    if (id && this.panelCollapsed()) this.togglePanel();
    this.select(id);
  }

  // O botão "Visão consolidada" da barra liga e desliga o desenho consolidado.
  protected toggleConsolidated(): void {
    this.onDirection(this.consolidated() ? LayoutDirection.Horizontal : LayoutDirection.Consolidated);
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
    else if (this.c4Open()) this.c4Open.set(false);
    else if (this.validationOpen()) this.validationOpen.set(false);
    else if (this.prompt()) this.closePrompt();
    else if (this.menu()) this.closeMenu();
    else if (this.exportOpen() || this.moreOpen() || this.compareOpen()) this.closePopovers();
    else if (this.searchOpen()) this.closeSearch();
    else if (this.pathFrom()) this.clearPath();
    else if (this.selectedId()) this.closePanel(this.selectedId()!);
    else if (this.focusId()) this.showAll();
  }

  // UX-95: menus suspensos pelo teclado; o foco entra no 1º item e volta ao botão ao fechar.
  private popoverOpener: HTMLElement | null = null;

  protected togglePopover(which: 'export' | 'more' | 'compare'): void {
    const target = which === 'export' ? this.exportOpen : which === 'more' ? this.moreOpen : this.compareOpen;
    const opening = !target();
    this.exportOpen.set(false);
    this.moreOpen.set(false);
    this.compareOpen.set(false);
    if (!opening) return;
    this.popoverOpener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    target.set(true);
    if (which === 'compare' || which === 'export') this.loadSiblings();
    // Mesmo atraso da busca: o Foblex devolve o foco ao item de acessibilidade dele logo depois.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${which}-menu button`)?.focus(), SEARCH_FOCUS_DELAY_MS);
  }

  protected closePopovers(): void {
    this.exportOpen.set(false);
    this.moreOpen.set(false);
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
  protected nextHole(): void {
    this.canvas()?.nextHole();
  }

  // Exportar o recorte: imagem e Mermaid do que está na tela (inteiro, recortado ou recolhido).
  protected readonly exportMessage = signal<{ ok: boolean; text: string } | null>(null);

  private exportTimer?: ReturnType<typeof setTimeout>;

  private showExport(message: { ok: boolean; text: string }): void {
    this.exportMessage.set(message);
    clearTimeout(this.exportTimer);
    this.exportTimer = setTimeout(() => this.exportMessage.set(null), EXPORT_MESSAGE_MS);
  }

  protected exportImage(format: 'png' | 'svg'): void {
    this.closePopovers();
    const image = this.canvas()?.exportSvg();
    if (!image) {
      this.showExport({ ok: false, text: 'O grafo ainda não está desenhado. Espere ele aparecer e tente de novo.' });
      return;
    }
    const name = this.exportName();
    if (format === 'svg') {
      downloadBlob(new Blob([image.svg], { type: 'image/svg+xml' }), `${name}.svg`);
      this.showExport({ ok: true, text: `Download iniciado: ${name}.svg` });
      return;
    }
    svgToPng(image.svg, image.width, image.height).then(
      (blob) => {
        downloadBlob(blob, `${name}.png`);
        this.showExport({ ok: true, text: `Download iniciado: ${name}.png` });
      },
      (error: Error) => this.showExport({ ok: false, text: `Não exportou a imagem: ${error.message} Tente o SVG.` }),
    );
  }

  protected exportMermaid(download: boolean): void {
    this.closePopovers();
    const text = this.canvas()?.exportMermaid();
    if (!text) {
      this.showExport({ ok: false, text: 'O grafo ainda não está desenhado. Espere ele aparecer e tente de novo.' });
      return;
    }
    if (download) {
      downloadBlob(new Blob([text], { type: 'text/plain' }), `${this.exportName()}.mmd`);
      this.showExport({ ok: true, text: `Download iniciado: ${this.exportName()}.mmd` });
    } else {
      this.menuCopy.copy('```mermaid\n' + text + '```\n');
    }
  }

  private exportName(): string {
    const survey = this.detail()?.survey;
    const focus = this.focusId() ? '-recorte' : '';
    return `rastro-${fileSlug(survey?.entryPoint ?? 'fluxo')}${focus}`;
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
        this.softError.set(`Não excluiu: ${apiErrorMessage(error)}`);
      },
    });
  }

  protected discardFromMenu(): void {
    this.closePopovers();
    this.requestDiscard();
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
  protected fillHole(nodeId: string): void {
    this.filling.set(true);
    this.fillMessages.set([]);
    this.api.fillHole(this.id(), nodeId).subscribe({
      next: (response) => {
        this.detail.set(response.survey);
        this.fillMessages.set(response.result.messages);
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
        if (saved) this.draftMessage.set({ ok: true, text: 'Rascunho salvo carregado.' });
      },
      error: (error) => this.draftMessage.set({ ok: false, text: apiErrorMessage(error) }),
    });
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
    if (!this.newLabel.trim()) errors.label = 'Dê um nome ao passo.';
    if (!this.newIntent.trim()) errors.intent = 'Diga o que o passo deve fazer.';
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
        this.draftMessage.set({ ok: true, text: 'Rascunho salvo.' });
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
        this.draftMessage.set({ ok: false, text: 'Desenhe ao menos uma mudança para gerar o prompt.' });
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
      prompt: `${current.prompt}\n\nSugestão da IA (${this.providerLabel[suggestion.provider]}), para conferir antes de aplicar:\n${suggestion.summary}\n${steps}`,
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
    if (next < 0) return;
    event.preventDefault();
    items[next]?.focus();
  }

  protected focusOnNode(nodeId: string): void {
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
        this.noteMessage.set({ ok: true, text: saved ? 'Anotação salva.' : 'Anotação apagada.' });
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
    if (link) window.location.assign(link);
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
      this.draftMessage.set({ ok: false, text: 'Desenhe ao menos uma mudança para validar.' });
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
        else this.draftMessage.set({ ok: false, text: `Não descartou: ${apiErrorMessage(error)}` });
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
    this.draftMessage.set({ ok: true, text: 'Rascunho descartado.' });
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

function readPanelCollapsed(): boolean {
  try {
    return localStorage.getItem(PANEL_COLLAPSED_KEY) === 'true';
  } catch {
    return false;
  }
}
