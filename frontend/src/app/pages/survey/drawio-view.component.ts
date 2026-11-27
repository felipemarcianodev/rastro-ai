import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { RastroApi } from '../../core/rastro-api.service';
import { DrawioBaseline, readDrawioChanges } from '../../core/graph/drawio-merge';
import { snapToGrid } from '../../core/graph/business-layout';
import { DrawioSource, StepPosition } from '../../core/models';
import { LanguageService } from '../../core/i18n/i18n';
import { UiLanguage } from '../../core/i18n/language';
import { LayoutDirection } from '../../core/graph/layout';
import { ThemeService } from '../../core/theme.service';
import { ConfirmDialogComponent } from './confirm-dialog.component';
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
// Intervalo do salvamento depois do último autosave (Fase 65) e quanto tempo o "Desenho salvo" fica na tela.
export const DRAWIO_SAVE_DEBOUNCE_MS = 800;
export const DRAWIO_SAVED_MS = 2500;

const SAVE_FAILED_KEY = 'drawio.saveFailed';
const RESET_FAILED_KEY = 'drawio.reset.failed';

enum DrawioState {
  Loading = 0,
  Ready = 1,
  Failed = 2,
}

// O desenho draw.io: o editor do draw.io dentro de um iframe de mesma origem, carregado com o XML do desenho
// de origem. Só aceita mensagem da própria janela do iframe e da mesma origem.
@Component({
  selector: 'app-drawio-view',
  imports: [TranslatePipe, DrawingSwitcherComponent, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:message)': 'onMessage($event)' },
  template: `
    <div class="drawio-bar">
      @if (switcher()) {
        <app-drawing-switcher [active]="LayoutDirection.Drawio" [drawioAvailable]="true" [restricted]="editing()" [allowDrawio]="true" [vertical]="false" (pick)="directionChange.emit($event)" />
      }
      @if (surveyId()) {
        <button type="button" class="btn btn-g btn-small" (click)="confirmingReset.set(true)" [title]="'drawio.reset.title' | translate">{{ 'drawio.reset.button' | translate }}</button>
      }
      <span class="muted drawio-saved" role="status">@if (saved()) {{{ 'drawio.saved' | translate }}}</span>
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
    @if (saveError(); as error) {
      <div class="toast is-error" role="alert">
        <span>{{ error | translate }}</span>
        <button type="button" class="btn btn-g btn-small" (click)="saveError.set(null)">{{ 'common.close' | translate }}</button>
      </div>
    } @else if (removed() > 0) {
      <div class="toast" role="status">{{ 'drawio.removed' | translate }}</div>
    }
    @if (confirmingReset()) {
      <app-confirm-dialog [title]="'drawio.reset.confirmTitle' | translate" [text]="'drawio.reset.confirmText' | translate" [confirmLabel]="'drawio.reset.confirmAction' | translate" [busy]="resetting()" (confirm)="confirmReset()" (cancel)="confirmingReset.set(false)" />
    }
  `,
})
export class DrawioViewComponent {
  private readonly sanitizer = inject(DomSanitizer);
  private readonly language = inject(LanguageService);
  private readonly theme = inject(ThemeService);
  private readonly frame = viewChild.required<ElementRef<HTMLIFrameElement>>('frame');

  // O .drawio do desenho de origem, carregado quando o editor avisa que iniciou (e de novo se mudar).
  readonly xml = input.required<string>();
  // O que o editor guarda (Fase 65): o levantamento, a origem do desenho e a linha de base. Sem o levantamento, nada se grava.
  readonly surveyId = input<string | null>(null);
  readonly source = input<DrawioSource>(DrawioSource.Graph);
  readonly baseline = input<DrawioBaseline | null>(null);
  // Na edição do fluxo (Fase 67) o editor mexe no rascunho: só os arranjos do grafo ficam na barra e nada vai para a tabela.
  readonly editing = input(false);
  // O quadro de fluxos (Fase 68) não tem um levantamento só: a página que abriu o editor grava (saveXml recebe o XML do
  // editor depois do intervalo) e avisa o resultado por announceSaved e failSave. Sem a barra de desenhos do levantamento.
  readonly saveXml = input<((xml: string) => void) | null>(null);
  readonly switcher = input(true);
  readonly selectNode = output<string>();
  // O editor gravou uma mudança (autosave): a Fase 65 guarda a posição por aqui.
  readonly changed = output<string>();
  readonly directionChange = output<LayoutDirection>();
  // O "Reorganizar" apagou o guardado: a página gera o desenho do código de novo e manda para o editor (reload).
  readonly reorganized = output<void>();

  protected readonly LayoutDirection = LayoutDirection;
  protected readonly State = DrawioState;
  protected readonly state = signal(DrawioState.Loading);

  // O XML mais recente do editor (o último autosave); vale para exportar. Sem autosave, vale o carregado.
  private current: string | null = null;
  private initialized = false;
  private timer?: ReturnType<typeof setTimeout>;
  private readonly api = inject(RastroApi);
  protected readonly saved = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly removed = signal(0);
  protected readonly confirmingReset = signal(false);
  protected readonly resetting = signal(false);
  private saveTimer?: ReturnType<typeof setTimeout>;
  private savedTimer?: ReturnType<typeof setTimeout>;
  private pendingXml: string | null = null;
  // O último XML enviado ao editor: o mesmo XML de novo não recarrega, e a assinatura do que já está guardado não regrava.
  private sentXml: string | null = null;
  private savedSignature = '';
  private knownPositions = new Map<string, { x: number; y: number }>();
  private knownTexts = new Map<string, string>();

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
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.timer);
      clearTimeout(this.savedTimer);
      // O que o último autosave deixou pendente é gravado antes de a tela sair.
      if (this.pendingXml) this.flush();
      clearTimeout(this.saveTimer);
    });
    // A linha de base nova (o desenho foi gerado de novo): o que vale como carregado muda.
    effect(() => {
      const baseline = this.baseline();
      untracked(() => {
        this.knownPositions = new Map(baseline?.positions ?? []);
        this.knownTexts = new Map(baseline?.texts ?? []);
        this.removed.set(0);
      });
    });
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
        if (this.initialized && xml !== this.sentXml) this.sendLoad(xml);
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
          this.scheduleSave(message['xml']);
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

  // Carrega um XML novo no editor, mesmo igual ao anterior (o Reorganizar volta ao desenho gerado).
  reload(xml: string): void {
    this.current = xml;
    this.sendLoad(xml);
  }

  private sendLoad(xml: string): void {
    clearTimeout(this.saveTimer);
    this.pendingXml = null;
    this.sentXml = xml;
    this.savedSignature = signatureOf(readDrawioChanges(xml));
    this.post({ action: DRAWIO_ACTION_LOAD, xml, autosave: 1 });
  }

  // ---- Guardar o que o usuário mexeu (Fase 65) ----

  private scheduleSave(xml: string): void {
    if (!this.surveyId() && !this.saveXml()) return;
    this.pendingXml = xml;
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.flush(), DRAWIO_SAVE_DEBOUNCE_MS);
  }

  // Grava já o que está pendente (a página que sai do editor não espera o intervalo).
  flushPending(): void {
    if (this.pendingXml) this.flush();
  }

  private flush(): void {
    clearTimeout(this.saveTimer);
    const xml = this.pendingXml;
    const id = this.surveyId();
    this.pendingXml = null;
    const saveXml = this.saveXml();
    if (saveXml) {
      if (xml) saveXml(xml);
      return;
    }
    const changes = xml && id ? readDrawioChanges(xml) : null;
    if (!changes || !id) return;
    const source = this.source();
    const baseline = this.baseline();
    // Os passos do fluxo vêm do código: apagar o balão no editor não apaga nada, e ele volta na próxima abertura.
    this.removed.set(baseline ? [...baseline.ids].filter((rastroId) => !changes.texts.has(rastroId)).length : 0);
    const signature = signatureOf(changes);
    if (signature !== this.savedSignature) {
      this.api.saveDrawioLayout(id, source, { cells: changes.cells, annotations: changes.annotations }).subscribe({
        next: () => {
          this.savedSignature = signature;
          this.announceSaved();
        },
        error: () => this.saveError.set(SAVE_FAILED_KEY),
      });
    }
    if (source === DrawioSource.Business && baseline) this.saveBusiness(id, changes, baseline);
  }

  // Origem visão de negócio: o balão que mudou de lugar também muda na visão (a coordenada do .drawio volta para a da
  // tela pela diferença guardada ao gerar) e o texto editado vai como texto do usuário.
  private saveBusiness(id: string, changes: NonNullable<ReturnType<typeof readDrawioChanges>>, baseline: DrawioBaseline): void {
    const shift = baseline.shift;
    const steps: StepPosition[] = [];
    if (shift) {
      for (const cell of changes.cells) {
        const known = this.knownPositions.get(cell.rastroId);
        if (known && (known.x !== cell.x || known.y !== cell.y)) {
          steps.push({ stepId: cell.rastroId, x: snapToGrid(cell.x - shift.x), y: snapToGrid(cell.y - shift.y) });
        }
      }
    }
    if (steps.length > 0) {
      const moved = new Map(changes.cells.map((c) => [c.rastroId, { x: c.x, y: c.y }]));
      this.api.saveBusinessLayout(id, { steps, edges: [] }).subscribe({
        next: () => {
          for (const step of steps) this.knownPositions.set(step.stepId, moved.get(step.stepId)!);
          this.announceSaved();
        },
        error: () => this.saveError.set(SAVE_FAILED_KEY),
      });
    }
    for (const [stepId, text] of changes.texts) {
      if (text === '' || !this.knownTexts.has(stepId) || this.knownTexts.get(stepId) === text) continue;
      this.api.saveBusinessLabel(id, stepId, text).subscribe({
        next: () => {
          this.knownTexts.set(stepId, text);
          this.announceSaved();
        },
        error: () => this.saveError.set(SAVE_FAILED_KEY),
      });
    }
  }

  // O erro de gravação de quem grava por fora (o quadro): o mesmo aviso do levantamento.
  failSave(): void {
    this.saveError.set(SAVE_FAILED_KEY);
  }

  announceSaved(): void {
    this.saveError.set(null);
    this.saved.set(true);
    clearTimeout(this.savedTimer);
    this.savedTimer = setTimeout(() => this.saved.set(false), DRAWIO_SAVED_MS);
  }

  protected confirmReset(): void {
    const id = this.surveyId();
    if (!id) return;
    clearTimeout(this.saveTimer);
    this.pendingXml = null;
    this.resetting.set(true);
    this.api.resetDrawioLayout(id, this.source()).subscribe({
      next: () => {
        this.resetting.set(false);
        this.confirmingReset.set(false);
        this.saveError.set(null);
        this.reorganized.emit();
      },
      error: () => {
        this.resetting.set(false);
        this.confirmingReset.set(false);
        this.saveError.set(RESET_FAILED_KEY);
      },
    });
  }

  private armTimeout(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      if (this.state() === DrawioState.Loading) this.state.set(DrawioState.Failed);
    }, DRAWIO_LOAD_TIMEOUT_MS);
  }
}

// O que vai para a API de um XML: as posições e as anotações (a mesma leitura dá a mesma assinatura).
function signatureOf(changes: ReturnType<typeof readDrawioChanges>): string {
  return changes ? JSON.stringify([changes.cells, changes.annotations]) : '';
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
