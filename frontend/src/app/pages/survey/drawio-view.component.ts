import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/i18n';
import { UiLanguage } from '../../core/i18n/language';
import { LayoutDirection } from '../../core/graph/layout';
import { ThemeService } from '../../core/theme.service';
import { DrawingSwitcherComponent } from './drawing-switcher.component';

// Onde a API serve o draw.io (mesma origem, ADR-011) e os parâmetros fixos do modo embed: sem rede, sem salvar
// nem sair pelo editor, sem configurar nem bibliotecas externas.
export const DRAWIO_PATH = '/drawio/index.html';
const DRAWIO_EMBED_PARAMS: Readonly<Record<string, string>> = {
  embed: '1',
  proto: 'json',
  spin: '1',
  stealth: '1',
  offline: '1',
  noSaveBtn: '1',
  noExitBtn: '1',
  saveAndExit: '0',
  configure: '0',
  libraries: '0',
};

// Idioma do editor, pelo idioma da tela.
export const DRAWIO_LANG: Record<UiLanguage, string> = {
  [UiLanguage.English]: 'en',
  [UiLanguage.Portuguese]: 'pt-br',
  [UiLanguage.Spanish]: 'es',
};

// Eventos que o editor manda à página (protocolo json do modo embed) e a ação que a página manda a ele.
export const DRAWIO_EVENT = {
  Init: 'init',
  Load: 'load',
  Autosave: 'autosave',
  RastroSelect: 'rastro-select',
} as const;
export const DRAWIO_ACTION_LOAD = 'load';

// Quanto esperar o editor responder antes de avisar (a primeira abertura carrega o editor inteiro).
export const DRAWIO_LOAD_TIMEOUT_MS = 20000;

enum DrawioState {
  Loading = 0,
  Ready = 1,
  Failed = 2,
}

// O desenho draw.io: o editor do draw.io dentro de um iframe de mesma origem, carregado com o XML do desenho
// de origem. Só aceita mensagem da própria janela do iframe e da mesma origem.
@Component({
  selector: 'app-drawio-view',
  imports: [TranslatePipe, DrawingSwitcherComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:message)': 'onMessage($event)' },
  template: `
    <div class="drawio-bar">
      <app-drawing-switcher [active]="LayoutDirection.Drawio" [drawioAvailable]="true" [vertical]="false" (pick)="directionChange.emit($event)" />
    </div>
    @if (state() === State.Loading) {
      <p class="muted drawio-status" role="status">{{ 'drawio.loading' | translate }}</p>
    }
    @if (state() === State.Failed) {
      <div class="notice is-error drawio-status" role="alert">
        <span>{{ 'drawio.failed' | translate }}</span>
        <button type="button" class="btn btn-g btn-small" (click)="retry()">{{ 'common.retry' | translate }}</button>
      </div>
    }
    <iframe #frame class="drawio-frame" [src]="src()" [title]="'drawio.frameTitle' | translate"></iframe>
  `,
})
export class DrawioViewComponent {
  private readonly sanitizer = inject(DomSanitizer);
  private readonly language = inject(LanguageService);
  private readonly theme = inject(ThemeService);
  private readonly frame = viewChild.required<ElementRef<HTMLIFrameElement>>('frame');

  // O .drawio do desenho de origem, carregado quando o editor avisa que iniciou (e de novo se mudar).
  readonly xml = input.required<string>();
  readonly selectNode = output<string>();
  // O editor gravou uma mudança (autosave): a Fase 65 guarda a posição por aqui.
  readonly changed = output<string>();
  readonly directionChange = output<LayoutDirection>();

  protected readonly LayoutDirection = LayoutDirection;
  protected readonly State = DrawioState;
  protected readonly state = signal(DrawioState.Loading);

  // O XML mais recente do editor (o último autosave); vale para exportar. Sem autosave, vale o carregado.
  private current: string | null = null;
  private initialized = false;
  private timer?: ReturnType<typeof setTimeout>;

  private readonly dark = computed(() => {
    const choice = this.theme.choice();
    return choice === 'dark' || (choice === 'system' && !!window.matchMedia?.('(prefers-color-scheme: dark)').matches);
  });

  // A URL é montada só de constantes, do idioma da tela e do tema: de mesma origem e sem dado de fora. Por isso
  // o bypass do sanitizador vale só aqui.
  protected readonly src = computed<SafeResourceUrl>(() => {
    const params = new URLSearchParams(DRAWIO_EMBED_PARAMS);
    params.set('lang', DRAWIO_LANG[this.language.language()]);
    if (this.dark()) params.set('ui', 'dark');
    return this.sanitizer.bypassSecurityTrustResourceUrl(`${DRAWIO_PATH}?${params.toString()}`);
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    // Trocar idioma ou tema recarrega o editor: volta a "carregando" e espera o novo init.
    effect(() => {
      this.src();
      untracked(() => {
        this.initialized = false;
        this.state.set(DrawioState.Loading);
        this.armTimeout();
      });
    });
    // O XML de entrada mudou com o editor já aberto: carrega o novo.
    effect(() => {
      const xml = this.xml();
      untracked(() => {
        this.current = xml;
        if (this.initialized) this.sendLoad(xml);
      });
    });
  }

  // Aceita só mensagem da mesma origem e da janela do próprio iframe, com JSON válido.
  protected onMessage(event: MessageEvent): void {
    if (event.origin !== window.location.origin || event.source !== this.frame().nativeElement.contentWindow) return;
    const message = parse(event.data);
    if (!message) return;
    switch (message['event']) {
      case DRAWIO_EVENT.Init:
        this.initialized = true;
        this.sendLoad(this.current ?? this.xml());
        break;
      case DRAWIO_EVENT.Load:
        clearTimeout(this.timer);
        this.state.set(DrawioState.Ready);
        break;
      case DRAWIO_EVENT.Autosave:
        if (typeof message['xml'] === 'string') {
          this.current = message['xml'];
          this.changed.emit(message['xml']);
        }
        break;
      case DRAWIO_EVENT.RastroSelect:
        if (typeof message['rastroNode'] === 'string' && message['rastroNode'] !== '') this.selectNode.emit(message['rastroNode']);
        break;
    }
  }

  protected retry(): void {
    this.initialized = false;
    this.state.set(DrawioState.Loading);
    this.armTimeout();
    this.frame().nativeElement.contentWindow?.location.reload();
  }

  // O XML do que está no editor agora (o nome do arquivo é da página; o XML não o leva).
  exportDrawio(): string {
    return this.current ?? this.xml();
  }

  protected post(message: Record<string, unknown>): void {
    this.frame().nativeElement.contentWindow?.postMessage(JSON.stringify(message), window.location.origin);
  }

  private sendLoad(xml: string): void {
    this.post({ action: DRAWIO_ACTION_LOAD, xml, autosave: 1 });
  }

  private armTimeout(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      if (this.state() === DrawioState.Loading) this.state.set(DrawioState.Failed);
    }, DRAWIO_LOAD_TIMEOUT_MS);
  }
}

// Mensagem do editor: texto JSON (ou objeto) com o campo "event"; qualquer outra coisa é ignorada.
function parse(data: unknown): Record<string, unknown> | null {
  let value: unknown = data;
  if (typeof data === 'string') {
    try {
      value = JSON.parse(data);
    } catch {
      // Mensagem que não é JSON não é do editor: ignora.
      return null;
    }
  }
  return value !== null && typeof value === 'object' && typeof (value as Record<string, unknown>)['event'] === 'string' ? (value as Record<string, unknown>) : null;
}
