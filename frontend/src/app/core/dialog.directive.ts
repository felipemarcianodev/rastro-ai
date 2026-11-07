import { AfterViewInit, Directive, ElementRef, OnDestroy, inject, output } from '@angular/core';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const FIELD_TAGS = new Set(['INPUT', 'SELECT', 'TEXTAREA']);
// Marca os botões que só fecham o diálogo: o foco inicial não cai neles.
const DIALOG_CLOSE_ATTR = 'data-dialog-close';

// Diálogo modal acessível (prompt, validação, C4, confirmações). Vai no .overlay:
// - o foco entra no diálogo ao abrir e volta a quem abriu ao fechar;
// - Tab e Shift+Tab ficam presos dentro;
// - o resto da página fica inert enquanto ele está aberto (leitor de tela e clique);
// - o Esc emite dialogClose e não sobe para o documento: só o diálogo de cima fecha, e as
//   telas que usam o Esc no documento (limpar seleção) não agem no mesmo toque (UX-FIM-C04).
@Directive({
  selector: '[appDialog]',
  host: { '(keydown.tab)': 'trap($event)', '(keydown.shift.tab)': 'trap($event)', '(keydown.escape)': 'escape($event)' },
})
export class DialogDirective implements AfterViewInit, OnDestroy {
  readonly dialogClose = output<void>();
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  private readonly inerted: HTMLElement[] = [];

  ngAfterViewInit(): void {
    // Cada ancestral até o body: os irmãos ficam inert, o caminho até o diálogo não.
    for (let node: HTMLElement | null = this.host.nativeElement; node && node !== document.body; node = node.parentElement) {
      for (const sibling of Array.from(node.parentElement?.children ?? [])) {
        if (sibling !== node && sibling instanceof HTMLElement && !sibling.inert && sibling.tagName !== 'SCRIPT') {
          sibling.inert = true;
          this.inerted.push(sibling);
        }
      }
    }
    (this.initialFocus() ?? this.dialog())?.focus();
  }

  ngOnDestroy(): void {
    for (const el of this.inerted) el.inert = false;
    // O botão que abriu pode ter sumido (o diálogo trocou a tela): aí o foco fica onde estiver.
    if (this.opener?.isConnected) this.opener.focus();
  }

  protected escape(event: Event): void {
    if (event.defaultPrevented) return;
    event.stopPropagation();
    this.dialogClose.emit();
  }

  protected trap(event: Event): void {
    const items = this.focusables();
    if (items.length === 0) return;
    const keyboard = event as KeyboardEvent;
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    if (keyboard.shiftKey && (active === first || !this.host.nativeElement.contains(active))) {
      keyboard.preventDefault();
      last.focus();
    } else if (!keyboard.shiftKey && (active === last || !this.host.nativeElement.contains(active))) {
      keyboard.preventDefault();
      first.focus();
    }
  }

  // UX-G-L03: o foco inicial vai ao [autofocus], senão ao primeiro campo, senão ao primeiro botão
  // que não seja o de fechar (marcado com data-dialog-close).
  private initialFocus(): HTMLElement | null {
    const items = this.focusables();
    return (
      items.find((el) => el.hasAttribute('autofocus')) ??
      items.find((el) => FIELD_TAGS.has(el.tagName) && !(el instanceof HTMLInputElement && el.type === 'hidden')) ??
      items.find((el) => !el.hasAttribute(DIALOG_CLOSE_ATTR) && !el.classList.contains('btn-icon')) ??
      items[0] ??
      null
    );
  }

  private dialog(): HTMLElement | null {
    const dialog = this.host.nativeElement.querySelector<HTMLElement>('[role="dialog"], [role="alertdialog"]');
    if (dialog && !dialog.hasAttribute('tabindex')) dialog.tabIndex = -1;
    return dialog;
  }

  // UX-G-L25: getClientRects, e não offsetParent, que é null em elemento com position: fixed.
  private focusables(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => !el.hidden && el.getClientRects().length > 0);
  }
}
