import { plainBranch } from '../../core/graph/plain';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { EFMarkerType, FCanvasComponent, FFlowModule } from '@foblex/flow';
import { DesktopBridge } from '../../core/desktop-bridge.service';
import { shortCommit } from '../../core/format';
import { LanguageService } from '../../core/i18n/i18n';
import { flowSummaryOf } from '../../core/graph/consolidated';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_LABEL_KEY, BUSINESS_KIND_SLUG, LABEL_ORIGIN_LABEL_KEY, pathTo } from '../../core/graph/business';
import { BusinessGeometry, businessLayout } from '../../core/graph/business-layout';
import { businessToDrawio } from '../../core/graph/drawio';
import { HostLinkOf, fillLinkTemplate } from '../../core/graph/link-template';
import { LAYOUT_DIRECTION_GLYPH, LAYOUT_DIRECTION_LABEL_KEY, LayoutDirection, NEXT_LAYOUT_DIRECTION } from '../../core/graph/layout';
import { FlowSummary, readingLabel } from '../../core/graph/summary';
import { BusinessFlow, BusinessKind, BusinessStep, CodeRef, LabelState, RepositoryHostKind, RepositoryLinks, SurveyDetail } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Zoom da visão de negócio: mesmos limites e passo do canvas do grafo.
const ZOOM_MIN = 0.25;
const ZOOM_MAX = 2;
const ZOOM_STEP = 1.2;
const FIT_PADDING = 40;
// O zoom feito por código só chega ao Foblex depois: a escala é lida em seguida.
const SYNC_SCALE_DELAY_MS = 50;
const CENTER_DELAY_MS = 50;
// Rótulo do ramo perto da chegada (0 = origem, 1 = destino): quando duas ligações saem da mesma porta, o trecho do
// meio é comum às duas, e só o final mostra de que ramo é o rótulo.
export const BRANCH_LABEL_POSITION = 0.88;

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
  inferred: boolean;
}

// A visão de negócio do fluxo (Fase 57): só os passos-chave (entrada, decisões, notificações, recusas,
// chamadas entre sistemas e fim), em texto de negócio e com a referência ao código. Fica no lugar do canvas
// do grafo, como o mapa, mas desenha com o próprio Foblex: um balão por passo-chave, ligações em segmentos
// ortogonais e o rótulo do ramo ("Sim", "Não") sobre o fundo da tela. Os balões não se arrastam nesta fase
// (mover e guardar a posição são da Fase 58); a tela se move e se aproxima.
@Component({
  selector: 'app-business-view',
  imports: [FFlowModule, TranslatePipe],
  templateUrl: './business-view.component.html',
  styleUrl: './business-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessViewComponent {
  private readonly api = inject(RastroApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(LanguageService);
  private readonly bridge = inject(DesktopBridge);
  private readonly canvas = viewChild(FCanvasComponent);

  readonly detail = input.required<SurveyDetail>();
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
  protected readonly loading = computed(() => !this.flow() && !this.error());
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

  protected readonly labelPosition = BRANCH_LABEL_POSITION;
  protected readonly markerEnd = EFMarkerType.END_ALL_STATES;
  protected readonly BusinessKind = BusinessKind;
  protected readonly LabelState = LabelState;
  protected readonly LayoutDirection = LayoutDirection;
  protected readonly next = NEXT_LAYOUT_DIRECTION[LayoutDirection.Business];
  protected readonly directionLabel = LAYOUT_DIRECTION_LABEL_KEY;
  protected readonly directionGlyph = LAYOUT_DIRECTION_GLYPH;
  // Rótulo do ramo em texto simples (then e else viram "Se sim" e "Se não"), como no grafo e nas ligações.
  protected readonly branchText = plainBranch;
  protected readonly slug = BUSINESS_KIND_SLUG;
  protected readonly glyph = BUSINESS_KIND_GLYPH;
  protected readonly kindLabel = BUSINESS_KIND_LABEL_KEY;
  protected readonly originLabel = LABEL_ORIGIN_LABEL_KEY;
  protected readonly stateLabel = BUSINESS_STATE_LABEL_KEY;
  protected readonly hostLabel = BUSINESS_HOST_LABEL_KEY;

  private readonly nodes = computed(() => new Map((this.detail().graph?.nodes ?? []).map((node) => [node.id, node])));

  protected readonly geometry = computed<BusinessGeometry | null>(() => {
    const flow = this.flow();
    return flow && flow.steps.length > 1 ? businessLayout(flow) : null;
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
    return (this.flow()?.edges ?? []).flatMap((edge, index) => (geometry.boxes.has(edge.from) && geometry.boxes.has(edge.to) && edge.from !== edge.to ? [{ index, from: edge.from, to: edge.to, label: edge.label ? plainBranch(edge.label) : null, inferred: edge.inferred }] : []));
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
    effect((onCleanup) => {
      const detail = this.detail();
      this.reload();
      untracked(() => {
        // Outro levantamento começa limpo; o mesmo, com o grafo novo, mantém o passo escolhido.
        if (detail.survey.id !== loadedId) {
          loadedId = detail.survey.id;
          this.flow.set(null);
          this.stepId.set(null);
          this.links.set([]);
        }
        this.linksFailed.set(false);
        this.error.set(null);
      });
      const sub = this.api.business(detail.survey.id).subscribe({
        next: (flow) =>
          untracked(() => {
            this.flow.set(flow);
            this.fitted = false;
            if (!flow.steps.some((s) => s.id === this.stepId())) this.stepId.set(null);
          }),
        error: (error) => untracked(() => this.error.set(apiErrorMessage(error))),
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
        sub.unsubscribe();
        linksSub.unsubscribe();
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
        if (found && !this.pickedHere) setTimeout(() => this.canvas()?.centerGroupOrNode(found.id, true), CENTER_DELAY_MS);
      });
    });
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

  // Enquadra o quadro inteiro, sem passar de 100%.
  protected fit(): void {
    this.canvas()?.fitToScreen({ x: FIT_PADDING, y: FIT_PADDING }, true, false, 1);
    this.syncScale();
  }

  // Abre já enquadrado.
  protected onFullRendered(): void {
    if (this.fitted) return;
    this.fitted = true;
    this.canvas()?.fitToScreen({ x: FIT_PADDING, y: FIT_PADDING }, false, false, 1);
    this.syncScale();
  }

  private syncScale(): void {
    setTimeout(() => {
      const canvas = this.canvas();
      if (canvas) this.scale.set(canvas.getScale());
    }, SYNC_SCALE_DELAY_MS);
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected toggleDirection(): void {
    this.directionChange.emit(this.next);
  }

  protected seeGraph(): void {
    this.directionChange.emit(LayoutDirection.Horizontal);
  }

  protected pick(step: BusinessStep): void {
    this.pickedHere = true;
    this.stepId.set(step.id);
    this.selectNode.emit(step.nodeId);
    this.pickedHere = false;
  }

  protected pickGrouped(nodeId: string): void {
    this.selectNode.emit(nodeId);
  }

  protected pickPath(step: BusinessStep): void {
    this.pick(step);
    this.focusStep(step.id);
  }

  protected close(): void {
    const step = this.current();
    this.stepId.set(null);
    this.selectNode.emit(null);
    if (step) this.focusStep(step.id);
  }

  protected focusSection(): void {
    const step = this.current();
    if (step) this.focusFlow.emit(step.nodeId);
  }

  protected viewInGraph(): void {
    const step = this.current();
    if (step) this.showInGraph.emit(step.nodeId);
  }

  protected open(url: string | null): void {
    if (url) this.bridge.openExternal(url);
  }

  // Foco sem rolar: o contêiner do Foblex tem overflow escondido e rolaria a imagem para longe do que ele calcula.
  private focusStep(id: string): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-step-id="${CSS.escape(id)}"]`)?.focus({ preventScroll: true }));
  }

  // Rótulo do balão para o leitor de tela: tipo e texto (sem repetir o tipo quando o texto já começa por ele) e o
  // estado ("proposto", "inferido") quando valer.
  protected stepAria(step: BusinessStep): string {
    const kind = this.i18n.t(BUSINESS_KIND_LABEL_KEY[step.kind]);
    const repeats = step.label.toLowerCase().startsWith(kind.toLowerCase());
    const text = repeats ? step.label : this.i18n.t('business.stepAria', { kind, label: step.label });
    const states = [this.isProposed(step) ? this.i18n.t('business.proposed') : null, step.inferred ? this.i18n.t('business.inferred') : null].filter((x) => x !== null);
    return states.length > 0 ? this.i18n.t('business.stepAriaStates', { text, states: states.join(', ') }) : text;
  }

  protected isProposed(step: BusinessStep): boolean {
    return step.labelState === LabelState.Proposed;
  }

  // draw.io (Fase 57): a visão de negócio como está na tela, com o link de cada balão para o código.
  exportDrawio(name: string, linkOf?: HostLinkOf): string | null {
    const flow = this.flow();
    const geometry = this.geometry();
    return flow && geometry ? businessToDrawio(flow, geometry, name, linkOf) : null;
  }
}
