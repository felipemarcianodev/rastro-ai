import { AfterViewInit, Directive, ElementRef, OnDestroy, inject } from '@angular/core';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Diálogo modal acessível (prompt, validação, C4, confirmações). Vai no .overlay:
// - o foco entra no diálogo ao abrir e volta a quem abriu ao fechar;
// - Tab e Shift+Tab ficam presos dentro;
// - o resto da página fica inert enquanto ele está aberto (leitor de tela e clique).
@Directive({
  selector: '[appDialog]',
  host: { '(keydown.tab)': 'trap($event)', '(keydown.shift.tab)': 'trap($event)' },
})
export class DialogDirective implements AfterViewInit, OnDestroy {
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
    const first = this.focusables().find((el) => !el.classList.contains('btn-icon')) ?? this.focusables()[0];
    (first ?? this.dialog())?.focus();
  }

  ngOnDestroy(): void {
    for (const el of this.inerted) el.inert = false;
    // O botão que abriu pode ter sumido (o diálogo trocou a tela): aí o foco fica onde estiver.
    if (this.opener?.isConnected) this.opener.focus();
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

  private dialog(): HTMLElement | null {
    const dialog = this.host.nativeElement.querySelector<HTMLElement>('[role="dialog"], [role="alertdialog"]');
    if (dialog && !dialog.hasAttribute('tabindex')) dialog.tabIndex = -1;
    return dialog;
  }

  private focusables(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => !el.hidden && el.offsetParent !== null);
  }
}
