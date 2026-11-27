import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { LAYOUT_DIRECTION_GLYPH, LayoutDirection, isOutsideCanvas } from '../../core/graph/layout';

// Ordem dos botões: os três arranjos do grafo, depois os desenhos que a página faz no lugar do canvas.
export const DRAWING_SWITCHER_ORDER: readonly LayoutDirection[] = [
  LayoutDirection.Timeline,
  LayoutDirection.Horizontal,
  LayoutDirection.Tree,
  LayoutDirection.Map,
  LayoutDirection.Business,
  LayoutDirection.Consolidated,
];

// Nome de cada desenho (aria-label do botão e texto lido na troca) e a dica do title: chaves de tradução.
export const DRAWING_LABEL_KEY: Record<LayoutDirection, string> = {
  [LayoutDirection.Timeline]: 'drawingSwitcher.timeline.label',
  [LayoutDirection.Horizontal]: 'drawingSwitcher.horizontal.label',
  [LayoutDirection.Tree]: 'drawingSwitcher.tree.label',
  [LayoutDirection.Map]: 'survey.view.map.label',
  [LayoutDirection.Business]: 'survey.view.business.label',
  [LayoutDirection.Consolidated]: 'drawingSwitcher.mermaid.label',
};

export const DRAWING_TITLE_KEY: Record<LayoutDirection, string> = {
  [LayoutDirection.Timeline]: 'drawingSwitcher.timeline.title',
  [LayoutDirection.Horizontal]: 'drawingSwitcher.horizontal.title',
  [LayoutDirection.Tree]: 'drawingSwitcher.tree.title',
  [LayoutDirection.Map]: 'survey.view.map.title',
  [LayoutDirection.Business]: 'survey.view.business.title',
  [LayoutDirection.Consolidated]: 'survey.view.consolidated.title',
};

// Barra de desenhos do painel: um botão por tipo de desenho, com o ativo marcado. Fica no canto do desenho,
// junto do zoom, e a troca sobe por "pick" (a página guarda a escolha e anuncia a troca ao leitor de tela).
@Component({
  selector: 'app-drawing-switcher',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="drawing-switcher" [class.is-vertical]="vertical()" role="toolbar" [attr.aria-label]="'survey.group.drawing' | translate" (keydown)="onKeydown($event)">
      @for (direction of directions; track direction) {
        <button type="button" class="btn btn-icon drawing-switcher-item"
          [class.is-pressed]="direction === active()"
          [attr.aria-pressed]="direction === active()"
          [attr.aria-label]="labelKey[direction] | translate"
          [title]="(isDisabled(direction) ? 'drawingSwitcher.disabledTitle' : titleKey[direction]) | translate"
          [attr.tabindex]="direction === active() ? 0 : -1"
          [disabled]="isDisabled(direction)"
          (click)="choose(direction)">
          <span aria-hidden="true">{{ glyph[direction] }}</span>
        </button>
      }
    </div>
  `,
})
export class DrawingSwitcherComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly active = input.required<LayoutDirection>();
  // Na edição e na comparação só o canvas mostra o rascunho e as diferenças: os outros desenhos ficam desligados.
  readonly restricted = input(false);
  // Coluna (canto do canvas) ou linha (barra do mapa e da visão de negócio).
  readonly vertical = input(false);
  readonly pick = output<LayoutDirection>();

  protected readonly directions = DRAWING_SWITCHER_ORDER;
  protected readonly labelKey = DRAWING_LABEL_KEY;
  protected readonly titleKey = DRAWING_TITLE_KEY;
  protected readonly glyph = LAYOUT_DIRECTION_GLYPH;

  protected isDisabled(direction: LayoutDirection): boolean {
    return this.restricted() && isOutsideCanvas(direction);
  }

  protected choose(direction: LayoutDirection): void {
    if (direction === this.active() || this.isDisabled(direction)) return;
    this.pick.emit(direction);
  }

  // Setas andam entre os botões (o foco, sem ativar); só o botão ativo está na ordem do Tab.
  protected onKeydown(event: KeyboardEvent): void {
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
    if (step === 0) return;
    const buttons = [...this.host.nativeElement.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
    const at = buttons.findIndex((b) => b === document.activeElement);
    if (buttons.length === 0 || at < 0) return;
    event.preventDefault();
    event.stopPropagation();
    buttons[(at + step + buttons.length) % buttons.length].focus({ preventScroll: true });
  }
}
