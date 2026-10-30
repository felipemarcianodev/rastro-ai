import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of, switchMap } from 'rxjs';
import { CopyFeedback } from '../../core/copy-feedback';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { DiagramStyle, consolidatedMermaid, kindClassOf, mergeDiagram, nodeIdFromSvg, setKindStyle, setNodeStyle } from '../../core/graph/consolidated';
import { DIAGRAM_RENDERER, readDiagramColors, standaloneSvg } from '../../core/mermaid-render';
import { SurveyDetail, SurveyDiagram, SurveyStatus } from '../../core/models';
import { shortCommit } from '../../core/format';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { ConfirmDialogComponent, ConfirmTone } from '../survey/confirm-dialog.component';

// A prévia espera o fim da digitação antes de desenhar (o Mermaid é lento para texto grande).
const RENDER_DELAY_MS = 300;
const HISTORY_LIMIT = 100;
// Teclas seguidas no editor valem um passo só do desfazer.
const TYPING_COALESCE_MS = 800;
const MESSAGE_MS = 4000;
// Zoom da prévia: passos de 25%, de 25% a 300%.
const ZOOM_STEP = 25;
const ZOOM_MIN = 25;
const ZOOM_MAX = 300;
const ZOOM_DEFAULT = 100;

// Tokens do tema que viram sugestão de cor no painel (nada de verde na paleta).
const SWATCH_TOKENS = ['--paper', '--surface', '--surface-sunk', '--ink', '--ink-soft', '--line-strong', '--trail', '--inferred', '--unresolved', '--danger', '--kind-decision', '--kind-external'];
const HEX6 = /^#[0-9a-fA-F]{6}$/;

type Paint = Required<DiagramStyle>;
type PaintKey = keyof Paint;

const PAINT_FIELDS: { key: PaintKey; label: string; hint: string }[] = [
  { key: 'fill', label: 'Fundo', hint: 'o fundo' },
  { key: 'stroke', label: 'Borda', hint: 'a borda' },
  { key: 'color', label: 'Texto', hint: 'o texto' },
];

const DEFAULT_PAINT: Paint = { fill: '#ffffff', stroke: '#b7c1bc', color: '#1d2a2e' };

// Visão consolidada (Fase 27): o fluxo todo num Mermaid só, editável. O texto do editor é a única
// fonte: o painel de cor, o desfazer e o "gerar de novo" só reescrevem esse texto.
@Component({
  selector: 'app-diagram-page',
  imports: [RouterLink, ConfirmDialogComponent],
  templateUrl: './diagram.page.html',
  styleUrl: './diagram.page.scss',
  host: { '(document:keydown.escape)': 'onEscape()', '(document:keydown)': 'onKeydown($event)' },
})
export class DiagramPage {
  private readonly api = inject(RastroApi);
  private readonly renderer = inject(DIAGRAM_RENDERER);
  private readonly preview = viewChild<ElementRef<HTMLElement>>('preview');

  // Vem da rota (surveys/:id/visao).
  readonly id = input.required<string>();

  protected readonly detail = signal<SurveyDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal<SurveyDiagram | null>(null);
  protected readonly text = signal('');
  protected readonly svg = signal('');
  protected readonly renderError = signal<string | null>(null);
  protected readonly rendering = signal(false);
  protected readonly saving = signal(false);
  protected readonly message = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly confirmRegenerate = signal<{ text: string; dropped: number } | null>(null);
  protected readonly confirmDiscard = signal(false);
  // Edição trazida do levantamento anterior (ainda não salva neste): quantos passos editados saíram.
  protected readonly inherited = signal<{ dropped: number } | null>(null);
  protected readonly busy = signal(false);
  protected readonly canUndo = signal(false);
  protected readonly canRedo = signal(false);
  protected readonly copy = new CopyFeedback();
  protected readonly shortCommit = shortCommit;

  // Passo escolhido na prévia e as cores dele.
  protected readonly selected = signal<string | null>(null);
  protected readonly selectedLabel = signal('');
  protected readonly paint = signal<Paint>(DEFAULT_PAINT);
  protected readonly swatches = signal<string[]>([]);
  protected readonly paintFields = PAINT_FIELDS;
  protected readonly tonePrimary = ConfirmTone.Primary;
  // Zoom da prévia em %; null é "Ajustar" (cabe na largura).
  protected readonly zoom = signal<number | null>(null);
  protected readonly pngBusy = signal(false);

  private readonly generated = computed(() => {
    const graph = this.detail()?.graph;
    return graph ? consolidatedMermaid(graph, this.detail()?.survey.repository) : '';
  });
  protected readonly dirty = computed(() => this.saved() !== null && this.text() !== this.saved()!.mermaid);
  protected readonly kindClass = computed(() => {
    const id = this.selected();
    return id ? kindClassOf(this.text(), id) : undefined;
  });
  // Nada a salvar: o texto é o gerado do fluxo e não há edição salva nem herdada.
  protected readonly nothingToSave = computed(() => this.saved() === null && this.inherited() === null && this.text() === this.generated());
  // Editado num commit e o levantamento é de outro: o diagrama pode estar velho.
  protected readonly outdated = computed(() => {
    const saved = this.saved();
    const survey = this.detail()?.survey;
    return saved?.commitHash && survey?.commitHash && saved.commitHash !== survey.commitHash ? { saved: saved.commitHash, survey: survey.commitHash } : null;
  });

  private past: string[] = [];
  private future: string[] = [];
  private lastTyped = 0;
  private renderSeq = 0;
  private firstRender = true;
  private messageTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });

    // Prévia: desenha com atraso depois da última tecla e guarda o último SVG válido.
    effect((onCleanup) => {
      const text = this.text();
      if (!this.detail() || !text.trim()) return;
      const delay = this.firstRender ? 0 : RENDER_DELAY_MS;
      const timer = setTimeout(() => this.render(text), delay);
      onCleanup(() => clearTimeout(timer));
    });

    // O SVG entra por innerHTML: o sanitizador do Angular estragaria o desenho. O modo "strict" do
    // Mermaid já passou o SVG pelo DOMPurify, e o texto digitado nunca chega ao DOM sem passar por ele.
    effect(() => {
      const svg = this.svg();
      const el = this.preview()?.nativeElement;
      if (el) el.innerHTML = svg;
    });

    // Zoom por escala do SVG (sem desenhar de novo o Mermaid).
    effect(() => {
      this.svg();
      const zoom = this.zoom();
      const svg = this.preview()?.nativeElement.querySelector('svg');
      if (svg) applyZoom(svg, zoom);
    });

    // Realce do passo escolhido e passos alcançáveis por teclado (depois que o SVG novo entrou).
    effect(() => {
      this.svg();
      const id = this.selected();
      const el = this.preview()?.nativeElement;
      if (!el) return;
      for (const node of Array.from(el.querySelectorAll('g.node'))) {
        node.classList.toggle('is-selected', id !== null && nodeIdFromSvg(node.id) === id);
        node.setAttribute('tabindex', '0');
        node.setAttribute('role', 'button');
        node.setAttribute('aria-label', `Passo: ${(node.textContent ?? '').trim()}`);
      }
    });
  }

  protected reload(): void {
    this.load(this.id());
  }

  private load(id: string): void {
    this.detail.set(null);
    this.error.set(null);
    this.saved.set(null);
    this.inherited.set(null);
    this.svg.set('');
    this.selected.set(null);
    this.zoom.set(null);
    this.firstRender = true;
    this.past = [];
    this.future = [];
    this.syncHistory();
    forkJoin({ detail: this.api.survey(id), saved: this.api.diagram(id) })
      .pipe(
        // Diagrama herdado: busca o levantamento anterior para saber o que o dono de fato editou.
        // Se ele não carregar, o merge segue sem baseline.
        switchMap(({ detail, saved }) =>
          saved?.fromSurveyId
            ? this.api.survey(saved.fromSurveyId).pipe(
                catchError(() => of(null)),
                switchMap((previous) => of({ detail, saved, previous })),
              )
            : of({ detail, saved, previous: null }),
        ),
      )
      .subscribe({
      next: ({ detail, saved, previous }) => {
        if (detail.survey.status !== SurveyStatus.Completed || !detail.graph) {
          this.error.set('Este levantamento ainda não tem um fluxo pronto para consolidar.');
          return;
        }
        this.detail.set(detail);
        const generated = consolidatedMermaid(detail.graph, detail.survey.repository);
        if (saved?.fromSurveyId) {
          const baseline = previous?.graph ? consolidatedMermaid(previous.graph, previous.survey.repository) : undefined;
          const merged = mergeDiagram(saved.mermaid, generated, baseline);
          // O diagrama é do levantamento anterior: aqui nada foi salvo ainda, o dono salva.
          this.saved.set(null);
          this.inherited.set({ dropped: merged.dropped });
          this.text.set(merged.text);
          return;
        }
        this.saved.set(saved);
        this.text.set(saved?.mermaid ?? generated);
      },
      error: (error) => this.error.set(apiErrorMessage(error)),
    });
  }

  private render(text: string): void {
    const seq = ++this.renderSeq;
    this.rendering.set(true);
    this.renderer(text, readDiagramColors()).then(
      (svg) => {
        if (seq !== this.renderSeq) return;
        this.firstRender = false;
        this.rendering.set(false);
        this.renderError.set(null);
        this.svg.set(svg);
      },
      (error: unknown) => {
        if (seq !== this.renderSeq) return;
        this.firstRender = false;
        this.rendering.set(false);
        // Mantém o último desenho válido e mostra o que o Mermaid reclamou (com a linha, se vier).
        this.renderError.set(error instanceof Error ? error.message : 'O Mermaid não conseguiu desenhar este texto.');
      },
    );
  }

  // Edição --------------------------------------------------------------------------------

  protected onType(event: Event): void {
    this.change((event.target as HTMLTextAreaElement).value, true);
  }

  private change(next: string, typing = false): void {
    const current = this.text();
    if (next === current) return;
    const now = Date.now();
    if (!(typing && now - this.lastTyped < TYPING_COALESCE_MS)) {
      this.past.push(current);
      if (this.past.length > HISTORY_LIMIT) this.past.shift();
    }
    this.lastTyped = typing ? now : 0;
    this.future = [];
    this.text.set(next);
    this.syncHistory();
  }

  protected undo(): void {
    const previous = this.past.pop();
    if (previous === undefined) return;
    this.future.push(this.text());
    this.lastTyped = 0;
    this.text.set(previous);
    this.syncHistory();
  }

  protected redo(): void {
    const next = this.future.pop();
    if (next === undefined) return;
    this.past.push(this.text());
    this.lastTyped = 0;
    this.text.set(next);
    this.syncHistory();
  }

  private syncHistory(): void {
    this.canUndo.set(this.past.length > 0);
    this.canRedo.set(this.future.length > 0);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey)) return;
    // Dentro do editor o navegador já desfaz a digitação.
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

  protected onEscape(): void {
    if (this.confirmRegenerate()) this.confirmRegenerate.set(null);
    else if (this.confirmDiscard()) this.confirmDiscard.set(false);
    else if (this.selected()) this.selected.set(null);
  }

  // Cor ----------------------------------------------------------------------------------

  protected onPreviewClick(event: MouseEvent): void {
    this.pick(event.target instanceof Element ? event.target.closest('g.node') : null);
  }

  // Enter e Espaço num passo valem o mesmo que o clique.
  protected onPreviewKey(event: KeyboardEvent): void {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    const node = event.target instanceof Element ? event.target.closest('g.node') : null;
    if (!node) return;
    event.preventDefault();
    this.pick(node);
  }

  private pick(node: Element | null): void {
    const id = node ? nodeIdFromSvg(node.id) : null;
    if (!node || !id) {
      this.selected.set(null);
      return;
    }
    this.selected.set(id);
    this.selectedLabel.set((node.textContent ?? '').trim());
    this.paint.set(readPaint(node));
    this.swatches.set(readSwatches());
  }

  // Zoom ---------------------------------------------------------------------------------

  protected zoomFit(): void {
    this.zoom.set(null);
  }

  protected zoomTo(percent: number): void {
    this.zoom.set(Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, percent)));
  }

  protected zoomBy(direction: 1 | -1): void {
    this.zoomTo((this.zoom() ?? ZOOM_DEFAULT) + direction * ZOOM_STEP);
  }

  protected zoomReset(): void {
    this.zoomTo(ZOOM_DEFAULT);
  }

  // Ctrl + roda do mouse na prévia dá zoom; sem Ctrl a roda rola a prévia.
  protected onWheel(event: WheelEvent): void {
    if (!event.ctrlKey || event.deltaY === 0) return;
    event.preventDefault();
    this.zoomBy(event.deltaY < 0 ? 1 : -1);
  }

  protected setPaint(key: PaintKey, value: string): void {
    if (HEX6.test(value)) this.paint.update((paint) => ({ ...paint, [key]: value }));
  }

  protected onColor(key: PaintKey, event: Event): void {
    this.setPaint(key, (event.target as HTMLInputElement).value);
  }

  protected paintStep(): void {
    const id = this.selected();
    if (id) this.change(setNodeStyle(this.text(), id, this.paint()));
  }

  protected paintKind(): void {
    const name = this.kindClass();
    if (name) this.change(setKindStyle(this.text(), name, this.paint()));
  }

  // Salvar, gerar de novo, descartar -------------------------------------------------------

  protected save(): void {
    this.saving.set(true);
    this.api.saveDiagram(this.id(), this.text()).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.saved.set(saved);
        this.inherited.set(null);
        this.show({ ok: true, text: 'Diagrama salvo.' });
      },
      error: (error) => {
        this.saving.set(false);
        this.show({ ok: false, text: `Não salvou: ${apiErrorMessage(error)}` });
      },
    });
  }

  protected askRegenerate(): void {
    const merged = mergeDiagram(this.text(), this.generated());
    this.confirmRegenerate.set(merged);
  }

  protected regenerate(): void {
    const merged = this.confirmRegenerate();
    this.confirmRegenerate.set(null);
    if (!merged) return;
    this.change(merged.text);
    this.show({
      ok: true,
      text: merged.dropped > 0 ? `Diagrama refeito. ${droppedText(merged.dropped)}` : 'Diagrama refeito a partir do fluxo.',
    });
  }

  protected discard(): void {
    this.busy.set(true);
    this.api.deleteDiagram(this.id()).subscribe({
      next: () => {
        this.busy.set(false);
        this.confirmDiscard.set(false);
        this.saved.set(null);
        this.past = [];
        this.future = [];
        this.syncHistory();
        this.text.set(this.generated());
        this.show({ ok: true, text: 'Edição descartada. Voltou ao diagrama gerado do fluxo.' });
      },
      error: (error) => {
        this.busy.set(false);
        this.confirmDiscard.set(false);
        this.show({ ok: false, text: `Não descartou: ${apiErrorMessage(error)}` });
      },
    });
  }

  protected droppedText = droppedText;
  protected readonly inheritedNotice = 'Edição trazida do levantamento anterior.';

  // Exportar -----------------------------------------------------------------------------

  protected copyMermaid(): void {
    const text = this.text();
    this.copy.copy('```mermaid\n' + text + (text.endsWith('\n') ? '' : '\n') + '```\n');
  }

  protected downloadMermaid(): void {
    downloadBlob(new Blob([this.text()], { type: 'text/plain' }), `${this.fileName()}.mmd`);
    this.show({ ok: true, text: `Download iniciado: ${this.fileName()}.mmd` });
  }

  protected exportImage(format: 'png' | 'svg'): void {
    const image = this.svg() ? standaloneSvg(this.svg(), readDiagramColors().paper) : null;
    if (!image) {
      this.show({ ok: false, text: 'A prévia ainda não está desenhada. Espere ela aparecer e tente de novo.' });
      return;
    }
    const name = this.fileName();
    if (format === 'svg') {
      downloadBlob(new Blob([image.svg], { type: 'image/svg+xml' }), `${name}.svg`);
      this.show({ ok: true, text: `Download iniciado: ${name}.svg` });
      return;
    }
    this.pngBusy.set(true);
    svgToPng(image.svg, image.width, image.height).then(
      (blob) => {
        this.pngBusy.set(false);
        downloadBlob(blob, `${name}.png`);
        this.show({ ok: true, text: `Download iniciado: ${name}.png` });
      },
      (error: Error) => {
        this.pngBusy.set(false);
        this.show({ ok: false, text: `Não exportou a imagem: ${error.message} Tente o SVG.` });
      },
    );
  }

  private fileName(): string {
    return `rastro-visao-${fileSlug(this.detail()?.survey.entryPoint ?? 'fluxo')}`;
  }

  // Só a mensagem de sucesso some sozinha; o erro fica até o dono fechar.
  private show(message: { ok: boolean; text: string }): void {
    this.message.set(message);
    clearTimeout(this.messageTimer);
    if (message.ok) this.messageTimer = setTimeout(() => this.message.set(null), MESSAGE_MS);
  }
}

function droppedText(count: number): string {
  return count === 1 ? '1 passo editado saiu porque não existe mais no código.' : `${count} passos editados saíram porque não existem mais no código.`;
}

// Escala o SVG já desenhado: null cabe na largura, número é % do tamanho natural.
function applyZoom(svg: SVGSVGElement, zoom: number | null): void {
  svg.style.maxWidth = 'none';
  if (zoom === null) {
    svg.style.width = '100%';
    return;
  }
  const natural = svg.viewBox.baseVal?.width || parseFloat(svg.getAttribute('width') ?? '') || svg.getBoundingClientRect().width;
  svg.style.width = `${(natural * zoom) / 100}px`;
}

// Cores atuais do passo, lidas do SVG já desenhado (o que o dono está vendo).
function readPaint(node: Element): Paint {
  const shape = node.querySelector('.label-container, rect, polygon, path, ellipse, circle');
  const label = node.querySelector('text');
  const read = (el: Element | null, property: 'fill' | 'stroke', fallback: string) => (el ? toHex(getComputedStyle(el)[property]) : null) ?? fallback;
  return {
    fill: read(shape, 'fill', DEFAULT_PAINT.fill),
    stroke: read(shape, 'stroke', DEFAULT_PAINT.stroke),
    color: read(label, 'fill', DEFAULT_PAINT.color),
  };
}

// "rgb(29, 42, 46)" ou "#1d2a2e" -> "#1d2a2e"; qualquer outra coisa (none, transparent) vira null.
function toHex(value: string): string | null {
  if (HEX6.test(value)) return value.toLowerCase();
  const match = /^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)$/.exec(value.trim());
  if (!match || (match[4] !== undefined && Number(match[4]) === 0)) return null;
  return '#' + [match[1], match[2], match[3]].map((n) => Number(n).toString(16).padStart(2, '0')).join('');
}

// Cores do tema ativo como sugestão (sem repetir).
function readSwatches(): string[] {
  const style = getComputedStyle(document.documentElement);
  const colors = SWATCH_TOKENS.map((token) => style.getPropertyValue(token).trim().toLowerCase()).filter((value) => HEX6.test(value));
  return [...new Set(colors)];
}
