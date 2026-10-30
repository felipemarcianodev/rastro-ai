import { FindingsListComponent } from './findings-list.component';
import { FlowSummaryComponent } from './flow-summary.component';
import { FlowSummary } from '../../core/graph/summary';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Trail } from '../../core/graph/trail';
import {
  ChangeKind,
  CrossFlowFinding,
  EDGE_CONFIDENCE_LABEL,
  EdgeDecision,
  EDGE_CONFIDENCE_SLUG,
  EdgeConfidence,
  FlowEdge,
  FlowGraph,
  FlowNode,
  NODE_KIND_GLYPH,
  NODE_KIND_LABEL,
  NODE_KIND_SLUG,
  NodeKind,
  NOTE_MAX_LENGTH,
  PROPOSAL_ORIGIN_BADGE,
  PROPOSAL_ORIGIN_LABEL,
  PROPOSAL_ORIGIN_SLUG,
  SourceLocation,
  SourceSnippet,
  SurveySegment,
  UNRESOLVED_REASON_HELP,
  UNRESOLVED_REASON_LABEL,
} from '../../core/models';
import { breakable, shortCommit } from '../../core/format';
import { CopyFeedback } from '../../core/copy-feedback';
import { EditorLinks } from '../../core/editor-links';
import { FOREIGN_EDIT_MESSAGE } from '../../core/graph/draft-editor';
import { rulesTimeline } from '../../core/graph/rules-timeline';
import { collapsedOwner } from '../../core/graph/collapse';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Espera a seleção parar (setas) antes de pedir o trecho de código.
const SNIPPET_DELAY_MS = 150;

@Component({
  selector: 'app-node-panel',
  imports: [FormsModule, RouterLink, FindingsListComponent, FlowSummaryComponent],
  templateUrl: './node-panel.component.html',
  styleUrl: './node-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NodePanelComponent {
  private readonly api = inject(RastroApi);

  readonly surveyId = input.required<string>();
  readonly repository = input('');
  // Commit do primeiro trecho e trechos de outros sistemas (Fase 25), para dizer de onde o passo vem.
  readonly commitHash = input<string | undefined>(undefined);
  readonly segments = input<SurveySegment[]>([]);
  protected readonly foreignMessage = FOREIGN_EDIT_MESSAGE;
  protected readonly editor = inject(EditorLinks);
  readonly graph = input.required<FlowGraph>();
  // Resumo do fluxo (Fase 23), montado pelo canvas a partir do desenho.
  readonly summary = input<FlowSummary | null>(null);
  readonly selectedId = input<string | null>(null);
  readonly trail = input<Trail | null>(null);
  readonly selectNode = output<string | null>();
  // Fechar devolve o foco ao passo no grafo (quem fecha pelo teclado não se perde).
  readonly closePanel = output<string>();
  readonly decideEdge = output<EdgeDecision>();
  readonly deciding = input(false);
  readonly canFillHoles = input(false);
  readonly filling = input(false);
  readonly fillMessages = input<string[]>([]);
  readonly fillHole = output<string>();
  readonly canDescribe = input(false);
  readonly describing = input(false);
  readonly describeMessage = input<string | null>(null);
  // nodeId nulo = o fluxo inteiro.
  readonly describe = output<{ nodeId: string | null; overwrite: boolean }>();
  // Edição (Fase 7).
  readonly editing = input(false);
  // "Ver somente este fluxo" também pelo painel (o menu de contexto não é a única porta).
  readonly focusId = input<string | null>(null);
  readonly focusFlow = output<string>();
  // IA ligada e configurada: sem ela, o painel diz onde ligar em vez de esconder em silêncio.
  readonly aiActive = input(false);
  // Anotação do usuário no passo (Fase 12).
  readonly notes = input<ReadonlyMap<string, string>>(new Map());
  readonly savingNote = input(false);
  readonly noteMessage = input<{ ok: boolean; text: string } | null>(null);
  readonly saveNote = output<{ nodeId: string; text: string }>();
  readonly noteUndo = input<{ nodeId: string; text: string } | null>(null);
  // Regra cruzada entre fluxos (Fase 13).
  readonly crossFlow = input<CrossFlowFinding[]>([]);
  // Esperando a resposta de um salvar: fecha o editor só se der certo (erro mantém o texto).
  private awaitingNote = false;
  protected noteText = '';
  protected readonly editingNote = signal(false);
  protected readonly noteMax = NOTE_MAX_LENGTH;
  protected readonly applied = signal(false);
  private appliedTimer?: ReturnType<typeof setTimeout>;
  readonly showAll = output<void>();
  readonly removeEdge = output<FlowEdge>();
  // Prompt de refatoração de um achado (índice na lista do grafo).
  readonly refactorPrompt = output<number>();

  protected hasFindings(nodeId: string): boolean {
    return (this.graph().findings ?? []).some((f) => f.nodeId === nodeId);
  }
  readonly removeStep = output<string>();
  readonly restoreStep = output<string>();
  readonly setIntent = output<{ nodeId: string; intent: string }>();
  readonly connectTo = output<{ from: string; to: string }>();
  protected intentText = '';
  protected connectTarget = '';
  protected readonly ChangeKind = ChangeKind;

  protected readonly snippet = signal<SourceSnippet | null>(null);
  protected readonly snippetError = signal<string | null>(null);
  protected readonly loadingSnippet = signal(false);
  private snippetTimer?: ReturnType<typeof setTimeout>;
  protected readonly locationCopy = new CopyFeedback();

  protected readonly kindLabel = NODE_KIND_LABEL;
  protected readonly kindSlug = NODE_KIND_SLUG;
  protected readonly glyph = NODE_KIND_GLYPH;
  protected readonly reasonLabel = UNRESOLVED_REASON_LABEL;
  protected readonly reasonHelp = UNRESOLVED_REASON_HELP;
  protected readonly NodeKind = NodeKind;
  protected readonly breakable = breakable;

  private readonly byId = computed(() => new Map(this.graph().nodes.map((n) => [n.id, n])));

  protected readonly selected = computed(() => {
    const id = this.selectedId();
    return id ? (this.byId().get(id) ?? null) : null;
  });

  // Passos para ligar a partir do selecionado (teclado no lugar de arrastar).
  protected readonly connectOptions = computed(() => {
    const node = this.selected();
    return node ? this.graph().nodes.filter((n) => n.id !== node.id && !n.system && n.change !== ChangeKind.Removed && !collapsedOwner(n.id)) : [];
  });

  protected readonly timeline = computed(() => rulesTimeline(this.graph()));

  protected readonly unresolved = computed(() => this.graph().nodes.filter((n) => n.kind === NodeKind.Unresolved));
  protected readonly proposedHoles = computed(() => new Set(this.graph().edges.map((e) => e.from)));
  protected readonly originLabel = PROPOSAL_ORIGIN_LABEL;
  protected readonly originBadge = PROPOSAL_ORIGIN_BADGE;
  protected readonly originSlug = PROPOSAL_ORIGIN_SLUG;
  protected readonly findingCount = computed(() => (this.graph().findings ?? []).length);
  protected readonly EdgeConfidence = EdgeConfidence;

  protected readonly kindCounts = computed(() => {
    const counts = new Map<NodeKind, number>();
    for (const node of this.graph().nodes) counts.set(node.kind, (counts.get(node.kind) ?? 0) + 1);
    return [...counts.entries()].sort((a, b) => a[0] - b[0]);
  });

  // Arestas que chegam ao nó e que saem dele, com o nó do outro lado.
  protected readonly links = computed(() => {
    const node = this.selected();
    if (!node) return { incoming: [], outgoing: [] };
    const edges = this.graph().edges;
    const link = (edge: FlowEdge, otherId: string) => ({ edge, other: this.byId().get(otherId) });
    return {
      incoming: edges.filter((e) => e.to === node.id && e.from !== node.id).map((e) => link(e, e.from)),
      outgoing: edges
        .filter((e) => e.from === node.id && e.to !== node.id)
        .sort((a, b) => a.order - b.order)
        .map((e) => link(e, e.to)),
    };
  });

  // O caminho começa na entrada ou no passo A do "caminhos a partir daqui".
  protected readonly pathTitle = computed(() => {
    const first = this.path()[0];
    return !first || first.id === this.graph().entryNodeId ? 'Caminho desde a entrada' : `Caminho a partir de ${first.label}`;
  });

  protected readonly path = computed(() => {
    const trail = this.trail();
    if (!trail) return [];
    return trail.path.map((id) => this.byId().get(id)).filter((n): n is FlowNode => !!n);
  });

  constructor() {
    effect(() => {
      const message = this.noteMessage();
      if (!message || !this.awaitingNote) return;
      this.awaitingNote = false;
      if (message.ok) {
        this.editingNote.set(false);
        this.focusNoteButton();
      }
    });

    effect(() => {
      const node = this.selected();
      this.intentText = node?.intent ?? '';
      this.noteText = node ? (untracked(() => this.notes().get(node.id)) ?? '') : '';
      this.editingNote.set(false);
      this.connectTarget = '';
      this.snippet.set(null);
      this.snippetError.set(null);
      this.locationCopy.reset();
      const place = node ? this.placeOf(node) : undefined;
      if (!place) return;

      this.loadingSnippet.set(true);
      clearTimeout(this.snippetTimer);
      const system = node?.system;
      this.snippetTimer = setTimeout(() => this.api.source(this.surveyId(), place.file, place.line, undefined, system).subscribe({
        next: (snippet) => {
          this.snippet.set(snippet);
          this.loadingSnippet.set(false);
        },
        error: (error) => {
          this.snippetError.set(apiErrorMessage(error));
          this.loadingSnippet.set(false);
        },
      }), SNIPPET_DELAY_MS);
    });
  }

  protected startNote(node: FlowNode): void {
    this.noteText = this.notes().get(node.id) ?? '';
    this.editingNote.set(true);
    setTimeout(() => document.getElementById('note-text')?.focus());
  }

  protected submitNote(node: FlowNode, text: string): void {
    this.awaitingNote = true;
    this.saveNote.emit({ nodeId: node.id, text: text.trim() });
  }

  protected cancelNote(): void {
    this.editingNote.set(false);
    this.focusNoteButton();
  }

  protected undoDelete(): void {
    const undo = this.noteUndo();
    if (undo) this.saveNote.emit(undo);
  }

  // Ao fechar o editor o foco volta para o botão do bloco (não cai no body).
  private focusNoteButton(): void {
    setTimeout(() => document.querySelector<HTMLElement>('.note-block > button')?.focus());
  }

  protected applyIntent(node: FlowNode): void {
    this.setIntent.emit({ nodeId: node.id, intent: this.intentText });
    this.applied.set(true);
    clearTimeout(this.appliedTimer);
    this.appliedTimer = setTimeout(() => this.applied.set(false), 2000);
  }

  // Onde ler o código do passo: a posição dele ou, no nó externo (que não tem), a da 1ª chamada.
  protected fileName(file: string): string {
    return file.split(/[\/]/).pop() ?? file;
  }

  protected callSites(node: FlowNode): number {
    return this.graph().edges.filter((e) => e.to === node.id && e.location).length;
  }

  protected placeOf(node: FlowNode): SourceLocation | undefined {
    if (node.location) return node.location;
    if (node.kind !== NodeKind.External) return undefined;
    return this.graph()
      .edges.filter((e) => e.to === node.id && e.location)
      .sort((a, b) => a.order - b.order)[0]?.location;
  }

  protected editorLink(node: FlowNode): string | null {
    const place = this.placeOf(node);
    return place ? this.editor.link(node.system ?? this.repository(), place.file, place.line) : null;
  }

  // De onde o passo vem: o sistema (repositório) e o commit do trecho. Só aparece quando o fluxo
  // passa por mais de um sistema; o primeiro usa o repositório e o commit do levantamento.
  protected origin(node: FlowNode): { system: string; commit?: string } | null {
    // Levantamento antigo pode vir sem "segments": tratado como um sistema só.
    const segments = this.segments() ?? [];
    if (segments.length === 0) return null;
    const system = node.system ?? this.repository();
    const commit = node.system ? segments.find((s) => s.repository === node.system)?.commitHash : this.commitHash();
    return { system, commit: commit ? shortCommit(commit) : undefined };
  }

  // O link vscode:// abre pelo próprio navegador (sem passar pelo sanitizador do [href]).
  protected readonly editorStatus = signal<string | null>(null);
  protected readonly editorHintDismissed = signal(readHintDismissed());

  protected dismissEditorHint(): void {
    this.editorHintDismissed.set(true);
    try {
      localStorage.setItem(EDITOR_HINT_KEY, 'true');
    } catch {
      // Armazenamento bloqueado: a dica some só nesta visita.
    }
  }
  private editorTimer?: ReturnType<typeof setTimeout>;

  protected openInEditor(node: FlowNode): void {
    const link = this.editorLink(node);
    if (!link) return;
    window.location.assign(link);
    this.editorStatus.set('Abrindo no VS Code… Se não abrir, confira a pasta em Configurações.');
    clearTimeout(this.editorTimer);
    this.editorTimer = setTimeout(() => this.editorStatus.set(null), 5000);
  }

  protected copyLocation(node: FlowNode): void {
    const place = this.placeOf(node);
    if (place) this.locationCopy.copy(`${place.file}:${place.line}`);
  }

  protected edgeDescription(edge: FlowEdge): string {
    const base = EDGE_CONFIDENCE_LABEL[edge.confidence];
    return edge.origin !== undefined && edge.confidence !== EdgeConfidence.Extracted
      ? `${base}; proposta de ${PROPOSAL_ORIGIN_LABEL[edge.origin]}`
      : base;
  }

  protected edgeSlug(edge: FlowEdge): string {
    return EDGE_CONFIDENCE_SLUG[edge.confidence];
  }
}

const EDITOR_HINT_KEY = 'rastro.editorHintDismissed';

function readHintDismissed(): boolean {
  try {
    return localStorage.getItem(EDITOR_HINT_KEY) === 'true';
  } catch {
    return false;
  }
}
