import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, signal, viewChild } from '@angular/core';
import { baseStepId, nodeIdFromSvg } from '../../core/graph/consolidated';

// Escala do desenho: de 10% a 400%; os botões andam 20% por clique, como no grafo.
const SCALE_MIN = 0.1;
const SCALE_MAX = 4;
const SCALE_STEP = 1.2;
// Roda do mouse: quanto cada unidade de deltaY aproxima (a pinça do touchpad chega com Ctrl).
const WHEEL_RATE = 0.0015;
const PINCH_RATE = 0.01;
const LINE_PX = 16;
// Margem do enquadrar e distância que separa o arrastar do clique.
const FIT_PAD = 24;
const DRAG_PX = 4;
const PAN_KEY_PX = 60;
// Abaixo desta escala o texto do Mermaid não se lê: o desenho abre na largura, a partir do topo.
const READABLE_SCALE = 0.6;

export interface StagePick {
  id: string;
  label: string;
  element: Element;
}

interface View {
  x: number;
  y: number;
  scale: number;
}

// Palco do SVG do Mermaid (Fase 28): arrastar o fundo move, a roda aproxima em volta do cursor e o
// passo clicado sai em "pick". Usado pela visão consolidada no fluxo e pela tela de edição.
@Component({
  selector: 'app-diagram-stage',
  templateUrl: './diagram-stage.component.html',
  styleUrl: './diagram-stage.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiagramStageComponent {
  // SVG já sanitizado pelo Mermaid (modo "strict").
  readonly svg = input('');
  // Passo escolhido (id do Mermaid). Com "repeats", acende também as repetições (s1_r2).
  readonly selected = input<string | null>(null);
  readonly repeats = input(false);
  readonly label = input('Desenho do fluxo');
  readonly pick = output<StagePick | null>();

  private readonly viewport = viewChild.required<ElementRef<HTMLElement>>('viewport');
  private readonly content = viewChild.required<ElementRef<HTMLElement>>('content');

  protected readonly view = signal<View>({ x: 0, y: 0, scale: 1 });
  protected readonly dragging = signal(false);
  private size = { width: 0, height: 0 };
  // Enquadrado uma vez por desenho; um palco ainda sem tamanho tenta de novo no próximo SVG.
  private fitted = false;
  private drag: { px: number; py: number; x: number; y: number; id: number; moved: boolean } | null = null;

  constructor() {
    // O SVG entra por innerHTML: o sanitizador do Angular estragaria o desenho. O modo "strict" do
    // Mermaid já passou o SVG pelo DOMPurify.
    effect(() => {
      const svg = this.svg();
      const el = this.content().nativeElement;
      el.innerHTML = svg;
      const root = el.querySelector('svg');
      this.size = root ? naturalSize(root) : { width: 0, height: 0 };
      if (root) {
        root.removeAttribute('style');
        root.setAttribute('width', String(this.size.width));
        root.setAttribute('height', String(this.size.height));
      }
      // Abre enquadrado; o texto que muda depois não perde o zoom nem a posição.
      if (!root) this.fitted = false;
      else if (!this.fitted) this.fitted = this.fitView(true);
    });

    // Realce do escolhido e passos alcançáveis pelo teclado (depois que o SVG novo entrou).
    effect(() => {
      this.svg();
      const selected = this.selected();
      const repeats = this.repeats();
      for (const node of this.nodes()) {
        const id = nodeIdFromSvg(node.id);
        const hit = selected !== null && id !== null && (repeats ? baseStepId(id) === baseStepId(selected) : id === selected);
        node.classList.toggle('is-selected', hit);
        node.setAttribute('tabindex', '0');
        node.setAttribute('role', 'button');
        node.setAttribute('aria-label', `Passo: ${(node.textContent ?? '').trim()}`);
        node.setAttribute('aria-pressed', String(hit));
      }
    });
  }

  protected transform(): string {
    const { x, y, scale } = this.view();
    return `translate(${x}px, ${y}px) scale(${scale})`;
  }

  protected percent(): number {
    return Math.round(this.view().scale * 100);
  }

  // Zoom ---------------------------------------------------------------------------------

  fit(): void {
    this.fitView();
  }

  // "opening": a primeira vista. Fluxo alto demais para caber legível abre na largura, do topo.
  private fitView(opening = false): boolean {
    const { width, height } = this.viewportSize();
    if (width <= 0 || height <= 0 || this.size.width <= 0) return false;
    const across = (width - 2 * FIT_PAD) / this.size.width;
    const whole = clamp(Math.min(across, (height - 2 * FIT_PAD) / this.size.height, 1));
    if (opening && whole < READABLE_SCALE) {
      const scale = clamp(Math.min(across, 1));
      this.view.set({ scale, x: (width - this.size.width * scale) / 2, y: FIT_PAD });
      return true;
    }
    const scale = whole;
    this.view.set({
      scale,
      x: (width - this.size.width * scale) / 2,
      y: Math.max(FIT_PAD, (height - this.size.height * scale) / 2),
    });
    return true;
  }

  zoomIn(): void {
    this.zoomAtCenter(this.view().scale * SCALE_STEP);
  }

  zoomOut(): void {
    this.zoomAtCenter(this.view().scale / SCALE_STEP);
  }

  actualSize(): void {
    this.zoomAtCenter(1);
  }

  private zoomAtCenter(scale: number): void {
    const { width, height } = this.viewportSize();
    this.zoomAt(width / 2, height / 2, scale);
  }

  // Mantém parado o ponto (px, py) da vista enquanto a escala muda.
  private zoomAt(px: number, py: number, next: number): void {
    const view = this.view();
    const scale = clamp(next);
    const ratio = scale / view.scale;
    this.view.set({ scale, x: px - (px - view.x) * ratio, y: py - (py - view.y) * ratio });
  }

  protected onWheel(event: WheelEvent): void {
    if (event.deltaY === 0) return;
    event.preventDefault();
    const delta = event.deltaMode === WheelEvent.DOM_DELTA_LINE ? event.deltaY * LINE_PX : event.deltaY;
    const rect = this.viewport().nativeElement.getBoundingClientRect();
    this.zoomAt(event.clientX - rect.left, event.clientY - rect.top, this.view().scale * Math.exp(-delta * (event.ctrlKey ? PINCH_RATE : WHEEL_RATE)));
  }

  // Leva o passo para o meio da vista quando ele está fora dela (seleção pelo painel ou pela busca).
  reveal(id: string): void {
    const node = this.nodes().find((n) => {
      const nodeId = nodeIdFromSvg(n.id);
      return nodeId !== null && (this.repeats() ? baseStepId(nodeId) === baseStepId(id) : nodeId === id);
    });
    if (node) this.revealElement(node);
  }

  private revealElement(node: Element): void {
    const box = node.getBoundingClientRect();
    const rect = this.viewport().nativeElement.getBoundingClientRect();
    const inside = box.left >= rect.left && box.right <= rect.right && box.top >= rect.top && box.bottom <= rect.bottom;
    if (inside || rect.width === 0) return;
    const view = this.view();
    this.view.set({
      ...view,
      x: view.x + (rect.left + rect.width / 2) - (box.left + box.width / 2),
      y: view.y + (rect.top + rect.height / 2) - (box.top + box.height / 2),
    });
  }

  // Arrastar e clicar ------------------------------------------------------------------------

  protected onPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    const view = this.view();
    this.drag = { px: event.clientX, py: event.clientY, x: view.x, y: view.y, id: event.pointerId, moved: false };
  }

  protected onPointerMove(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || drag.id !== event.pointerId) return;
    const dx = event.clientX - drag.px;
    const dy = event.clientY - drag.py;
    if (!drag.moved) {
      if (Math.hypot(dx, dy) < DRAG_PX) return;
      drag.moved = true;
      this.dragging.set(true);
      try {
        // Com o ponteiro preso, soltar fora do palco ainda termina o arrastar.
        this.viewport().nativeElement.setPointerCapture(event.pointerId);
      } catch (error) {
        // Ponteiro que o navegador já soltou: o arrastar segue sem a captura.
        if (!(error instanceof DOMException)) throw error;
      }
    }
    this.view.update((view) => ({ ...view, x: drag.x + dx, y: drag.y + dy }));
  }

  // Soltar sem ter arrastado é um clique: escolhe o passo (ou limpa, no fundo).
  protected onPointerUp(event: PointerEvent): void {
    const drag = this.drag;
    this.drag = null;
    this.dragging.set(false);
    if (!drag || drag.id !== event.pointerId || drag.moved) return;
    this.emitPick(event.target instanceof Element ? event.target.closest('g.node') : null);
  }

  protected onPointerCancel(): void {
    this.drag = null;
    this.dragging.set(false);
  }

  // Enter e Espaço num passo valem o clique; setas movem e + e − dão zoom com o foco no palco.
  protected onKeydown(event: KeyboardEvent): void {
    const node = event.target instanceof Element ? event.target.closest('g.node') : null;
    if (node && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      this.emitPick(node);
      return;
    }
    const pan: Record<string, [number, number]> = { ArrowLeft: [PAN_KEY_PX, 0], ArrowRight: [-PAN_KEY_PX, 0], ArrowUp: [0, PAN_KEY_PX], ArrowDown: [0, -PAN_KEY_PX] };
    if (pan[event.key]) {
      event.preventDefault();
      const [dx, dy] = pan[event.key];
      this.view.update((view) => ({ ...view, x: view.x + dx, y: view.y + dy }));
    } else if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      this.zoomIn();
    } else if (event.key === '-') {
      event.preventDefault();
      this.zoomOut();
    }
  }

  // Passo que recebe foco pelo Tab e está fora da vista vem para o meio.
  protected onFocusIn(event: FocusEvent): void {
    const node = event.target instanceof Element ? event.target.closest('g.node') : null;
    if (node) this.revealElement(node);
  }

  // O foco num passo faz o navegador rolar o palco (overflow: hidden), e a imagem descolaria da
  // posição calculada: a rolagem volta a zero.
  protected onScroll(): void {
    const el = this.viewport().nativeElement;
    el.scrollTop = 0;
    el.scrollLeft = 0;
  }

  private emitPick(node: Element | null): void {
    const id = node ? nodeIdFromSvg(node.id) : null;
    this.pick.emit(node && id ? { id, label: (node.textContent ?? '').trim(), element: node } : null);
  }

  private nodes(): Element[] {
    return Array.from(this.content().nativeElement.querySelectorAll('g.node'));
  }

  private viewportSize(): { width: number; height: number } {
    const el = this.viewport().nativeElement;
    return { width: el.clientWidth, height: el.clientHeight };
  }
}

function clamp(scale: number): number {
  return Math.min(SCALE_MAX, Math.max(SCALE_MIN, scale));
}

// Tamanho do desenho pelo viewBox (o Mermaid põe width="100%" e o tamanho no max-width).
function naturalSize(svg: SVGSVGElement): { width: number; height: number } {
  const box = svg.viewBox?.baseVal;
  if (box && box.width > 0 && box.height > 0) return { width: box.width, height: box.height };
  const width = parseFloat(svg.getAttribute('width') ?? '');
  const height = parseFloat(svg.getAttribute('height') ?? '');
  return { width: width > 0 ? width : 0, height: height > 0 ? height : 0 };
}
