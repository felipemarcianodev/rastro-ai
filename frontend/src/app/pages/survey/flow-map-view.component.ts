import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { flowSummaryOf } from '../../core/graph/consolidated';
import {
  MAP_BLOCK_GLYPH,
  MAP_BLOCK_LABEL,
  MAP_BLOCK_SLUG,
  MAP_BOX,
  blockTitle,
  callText,
  laneTitle,
  litBlocks,
  litLinks,
  mapGeometry,
  outcomeOfLink,
  pathForOutcome,
  pathLabel,
  pathNeighbors,
} from '../../core/graph/flow-map';
import { fieldsText } from '../../core/graph/flow-insight';
import { LAYOUT_DIRECTION_GLYPH, LAYOUT_DIRECTION_LABEL, LayoutDirection, NEXT_LAYOUT_DIRECTION } from '../../core/graph/layout';
import { plainBranch } from '../../core/graph/plain';
import { FlowSummary, readingLabel } from '../../core/graph/summary';
import { AiStatusService } from '../../core/ai-status.service';
import { AiUse, FlowMap, MapBlock, MapLaneKind, MapLink, MapLinkKind, SurveyDetail, aiCan } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { FlowMapQuestionsComponent } from './flow-map-questions.component';
import { FlowMapRulesComponent } from './flow-map-rules.component';

// Largura abaixo da qual o mapa vira uma coluna só.
const NARROW_QUERY = '(max-width: 640px)';
// Setas do teclado andam pelo caminho: para a frente (+1) e para trás (-1).
const WALK_KEY: Record<string, number> = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
const REVEAL_MARGIN = 16;

// Abas do painel do mapa (Fases 31 a 33).
export enum MapTab {
  Block = 0,
  Rules = 1,
  Questions = 2,
}

const MAP_TABS: { tab: MapTab; label: string }[] = [
  { tab: MapTab.Block, label: 'Bloco' },
  { tab: MapTab.Rules, label: 'Regras' },
  { tab: MapTab.Questions, label: 'Perguntas' },
];

// O quinto desenho do fluxo (Fase 31): o mapa da Fase 30, com uma faixa por sistema, os blocos na
// ordem do fluxo, as ligações entre eles e o seletor de caminho. Clicar num bloco escolhe o nó dele
// no grafo (o painel do passo abre) e mostra o painel do bloco aqui mesmo.
@Component({
  selector: 'app-flow-map-view',
  imports: [FlowMapRulesComponent, FlowMapQuestionsComponent],
  templateUrl: './flow-map-view.component.html',
  styleUrl: './flow-map-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(keydown)': 'onKey($event)' },
})
export class FlowMapViewComponent {
  private readonly api = inject(RastroApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly ai = inject(AiStatusService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  readonly detail = input.required<SurveyDetail>();
  readonly selectedId = input<string | null>(null);
  readonly selectNode = output<string | null>();
  readonly directionChange = output<LayoutDirection>();
  // Resumo do fluxo para a visão geral do painel: sem o canvas, é o desenho que o monta.
  readonly summaryChange = output<FlowSummary>();
  // "Ver só este trecho": o recorte do fluxo pelo nó do bloco.
  readonly focusFlow = output<string>();

  protected readonly map = signal<FlowMap | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = computed(() => !this.map() && !this.error());
  protected readonly empty = computed(() => !!this.map() && this.map()!.blocks.length === 0);
  private readonly reload = signal(0);
  // Caminho escolhido (null: todos). Bloco escolhido (null: nenhum).
  protected readonly pathId = signal<string | null>(null);
  protected readonly blockId = signal<string | null>(null);
  protected readonly narrow = signal(false);
  protected readonly tab = signal(MapTab.Block);
  // As abas de regras e de perguntas só buscam os dados na primeira vez que abrem.
  protected readonly opened = signal<ReadonlySet<MapTab>>(new Set());
  protected readonly MapTab = MapTab;
  protected readonly tabs = MAP_TABS;
  // A pergunta livre só existe com IA ligada, chave salva e o uso Sugestões habilitado (ADR-001).
  protected readonly canAsk = computed(() => aiCan(this.ai.status(), AiUse.Suggestions));

  protected readonly LayoutDirection = LayoutDirection;
  protected readonly next = NEXT_LAYOUT_DIRECTION[LayoutDirection.Map];
  protected readonly directionLabel = LAYOUT_DIRECTION_LABEL;
  protected readonly directionGlyph = LAYOUT_DIRECTION_GLYPH;
  protected readonly slug = MAP_BLOCK_SLUG;
  protected readonly glyph = MAP_BLOCK_GLYPH;
  protected readonly kindLabel = MAP_BLOCK_LABEL;
  protected readonly box = MAP_BOX;
  protected readonly MapLinkKind = MapLinkKind;
  protected readonly MapLaneKind = MapLaneKind;

  protected readonly nodes = computed(() => new Map((this.detail().graph?.nodes ?? []).map((node) => [node.id, node])));
  private readonly titles = computed(() => new Map((this.map()?.blocks ?? []).map((block) => [block.id, blockTitle(block, this.nodes())])));
  protected readonly laneTitles = computed(() => new Map((this.map()?.lanes ?? []).map((lane) => [lane.id, laneTitle(lane, this.detail().survey.repository)])));
  protected readonly geometry = computed(() => {
    const map = this.map();
    return map && map.blocks.length > 0 ? mapGeometry(map, this.narrow()) : null;
  });
  private readonly chosen = computed(() => this.map()?.paths.find((p) => p.id === this.pathId()) ?? null);
  protected readonly lit = computed(() => litBlocks(this.chosen()));
  protected readonly litLinkIndexes = computed(() => {
    const map = this.map();
    return map ? litLinks(map, this.chosen()) : null;
  });
  protected readonly pathButtons = computed(() => {
    const map = this.map();
    return (map?.paths ?? []).map((path, position) => ({ path, label: pathLabel(map!, path, position) }));
  });
  private readonly current = computed(() => this.map()?.blocks.find((b) => b.id === this.blockId()) ?? null);

  // O que o painel do bloco mostra: os passos que ele recolhe, os desfechos, as chamadas que saem
  // dele e os vizinhos no caminho.
  protected readonly panel = computed(() => {
    const map = this.map();
    const block = this.current();
    if (!map || !block) return null;
    const titles = this.titles();
    const nodes = this.nodes();
    const around = pathNeighbors(map, this.chosen(), block.id);
    const steps = block.nodeIds.flatMap((id) => {
      const node = nodes.get(id);
      return node ? [{ id, text: readingLabel(node), where: node.location ? `${node.location.file}:${node.location.line}` : null }] : [];
    });
    const outcomes = (block.outcomes ?? []).map((outcome, index) => ({
      index,
      text: plainBranch(outcome.label),
      target: outcome.to ? (titles.get(outcome.to) ?? null) : null,
      ends: outcome.ends,
      fails: outcome.fails,
      chosen: !!this.chosen()?.choices.some((c) => c.decision === block.id && c.outcome === index),
    }));
    const calls = map.links.filter((l) => l.kind === MapLinkKind.Call && l.from === block.id).map((l) => ({ to: l.to, title: titles.get(l.to) ?? l.to, text: callText(l), fields: fieldsText(l.http) }));
    return {
      block,
      title: titles.get(block.id) ?? block.title,
      lane: this.laneTitles().get(block.lane) ?? block.lane,
      where: block.location ? `${block.location.file}:${block.location.line}` : null,
      steps,
      outcomes,
      calls,
      previous: around.previous,
      next: around.next,
      previousTitle: around.previous ? titles.get(around.previous) : null,
      nextTitle: around.next ? titles.get(around.next) : null,
    };
  });

  constructor() {
    const query = window.matchMedia(NARROW_QUERY);
    this.narrow.set(query.matches);
    const onChange = (event: MediaQueryListEvent) => this.narrow.set(event.matches);
    query.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', onChange));

    let loadedId: string | null = null;
    effect((onCleanup) => {
      const detail = this.detail();
      this.reload();
      untracked(() => {
        // Outro levantamento começa limpo; o mesmo, com o grafo novo, mantém o que estava escolhido.
        if (detail.survey.id !== loadedId) {
          loadedId = detail.survey.id;
          this.map.set(null);
          this.pathId.set(null);
          this.blockId.set(null);
        }
        this.error.set(null);
      });
      const sub = this.api.map(detail.survey.id).subscribe({
        next: (map) =>
          untracked(() => {
            this.map.set(map);
            if (!map.paths.some((p) => p.id === this.pathId())) this.pathId.set(null);
            if (!map.blocks.some((b) => b.id === this.blockId())) this.blockId.set(null);
          }),
        error: (error) => untracked(() => this.error.set(apiErrorMessage(error))),
      });
      onCleanup(() => sub.unsubscribe());
    });

    effect(() => {
      const graph = this.detail().graph;
      if (graph) this.summaryChange.emit(flowSummaryOf(graph));
    });

    // Seleção de fora (painel do passo, busca, setas): o bloco que recolhe o passo acende.
    effect(() => {
      const selected = this.selectedId();
      const map = this.map();
      untracked(() => {
        if (!selected) {
          this.blockId.set(null);
          return;
        }
        if (!map) return;
        const current = this.current();
        if (current && (current.nodeId === selected || current.nodeIds.includes(selected))) return;
        const found = map.blocks.find((b) => b.nodeId === selected) ?? map.blocks.find((b) => b.nodeIds.includes(selected));
        this.blockId.set(found?.id ?? null);
      });
    });

    // O bloco escolhido vem para a vista, rolando só o contêiner do mapa.
    effect(() => {
      const id = this.blockId();
      const geometry = this.geometry();
      const scroller = this.scroller()?.nativeElement;
      if (!id || !geometry || !scroller) return;
      untracked(() => {
        const placed = geometry.blocks.find((p) => p.block.id === id);
        if (!placed) return;
        const { width, height } = MAP_BOX;
        const top = placed.y - REVEAL_MARGIN;
        const left = placed.x - REVEAL_MARGIN;
        if (top < scroller.scrollTop) scroller.scrollTop = top;
        else if (placed.y + height + REVEAL_MARGIN > scroller.scrollTop + scroller.clientHeight) scroller.scrollTop = placed.y + height + REVEAL_MARGIN - scroller.clientHeight;
        if (left < scroller.scrollLeft) scroller.scrollLeft = left;
        else if (placed.x + width + REVEAL_MARGIN > scroller.scrollLeft + scroller.clientWidth) scroller.scrollLeft = placed.x + width + REVEAL_MARGIN - scroller.clientWidth;
      });
    });
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected toggleDirection(): void {
    this.directionChange.emit(this.next);
  }

  protected choosePath(id: string | null): void {
    this.pathId.set(id);
  }

  protected pickBlock(block: MapBlock): void {
    this.tab.set(MapTab.Block);
    this.blockId.set(block.id);
    this.selectNode.emit(block.nodeId);
  }

  protected setTab(tab: MapTab): void {
    this.tab.set(tab);
    this.opened.update((current) => new Set(current).add(tab));
  }

  // Fonte de uma regra: escolhe o bloco no mapa, sem tirar o leitor da lista de regras.
  protected pickRuleSource(blockId: string): void {
    const block = this.map()?.blocks.find((b) => b.id === blockId);
    if (!block) return;
    this.blockId.set(block.id);
    this.selectNode.emit(block.nodeId);
  }

  // Fonte de uma resposta: o nó no grafo e o bloco do mapa que o recolhe.
  protected pickAnswerSource(nodeId: string): void {
    const block = this.map()?.blocks.find((b) => b.nodeId === nodeId) ?? this.map()?.blocks.find((b) => b.nodeIds.includes(nodeId));
    if (block) this.blockId.set(block.id);
    this.selectNode.emit(nodeId);
  }

  // Dica da ligação de chamada: verbo, rota e os campos enviados (Fase 34).
  protected callTitle(link: MapLink): string | null {
    const text = callText(link);
    const fields = fieldsText(link.http);
    return text && fields ? `${text}. Campos: ${fields}` : text;
  }

  protected pickBlockId(id: string): void {
    const block = this.map()?.blocks.find((b) => b.id === id);
    if (!block) return;
    this.pickBlock(block);
    this.focusBlock(id);
  }

  protected pickStep(nodeId: string): void {
    this.selectNode.emit(nodeId);
  }

  // Clicar num desfecho escolhe o primeiro caminho que fez aquela escolha.
  protected pickOutcome(decision: string, outcome: number): void {
    const map = this.map();
    const path = map ? pathForOutcome(map, decision, outcome) : null;
    if (path) this.pathId.set(path.id);
  }

  protected pickLinkOutcome(index: number): void {
    const map = this.map();
    const link = map?.links[index];
    if (!map || !link) return;
    this.pickOutcome(link.from, outcomeOfLink(map, link));
  }

  protected walk(step: number): void {
    const panel = this.panel();
    const target = panel ? (step > 0 ? panel.next : panel.previous) : null;
    if (target) this.pickBlockId(target);
  }

  protected closeBlock(): void {
    const id = this.blockId();
    this.blockId.set(null);
    this.selectNode.emit(null);
    if (id) this.focusBlock(id);
  }

  protected focusBlock(id: string): void {
    // Sem rolar: a rolagem do mapa é a do reveal, e o navegador não deve rolar os contêineres de cima.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-block-id="${CSS.escape(id)}"]`)?.focus({ preventScroll: true }));
  }

  protected focusSection(): void {
    const block = this.current();
    if (block) this.focusFlow.emit(block.nodeId);
  }

  protected onKey(event: KeyboardEvent): void {
    const step = WALK_KEY[event.key];
    if (step === undefined || !this.blockId() || event.ctrlKey || event.metaKey || event.altKey) return;
    if (event.target instanceof Element && event.target.closest('input, textarea, select')) return;
    event.preventDefault();
    this.walk(step);
  }

  protected titleOf(block: MapBlock): string {
    return this.titles().get(block.id) ?? block.title;
  }

  protected blockLabel(block: MapBlock): string {
    return `${MAP_BLOCK_LABEL[block.kind]}: ${this.titleOf(block)}`;
  }
}
