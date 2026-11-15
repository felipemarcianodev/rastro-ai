import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, booleanAttribute, computed, inject, input, output, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../../core/desktop-bridge.service';
import { isAbsoluteFolder } from '../../core/editor-links';
import { LanguageService } from '../../core/i18n/i18n';
import { tr } from '../../core/i18n/locale';
import { API_ERROR_MESSAGE_KEY, REPOSITORY_NAME_PATTERN, RepositoryItem } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// UX-G-L16: formulário único de cadastro de repositório, usado nas Configurações e no diálogo da tela inicial.
// O pai só cuida da lista e do que vem depois: o formulário valida, chama a API e avisa o cadastrado.
@Component({
  selector: 'app-register-repository',
  imports: [FormsModule, TranslatePipe],
  template: `
    <form class="form" (ngSubmit)="register()" novalidate [attr.aria-label]="labelText()">
      <div class="field">
        <label class="field-label" [for]="id('name')">{{ 'register.name' | translate }}</label>
        <input [id]="id('name')" class="input code" name="registerName" [(ngModel)]="newName" [placeholder]="'register.namePlaceholder' | translate" autocomplete="off" spellcheck="false"
          [attr.aria-invalid]="nameError() ? true : null" [attr.aria-describedby]="id('name-help') + (nameError() ? ' ' + id('name-error') : '')" />
        <span [id]="id('name-help')" class="field-help">{{ 'register.nameHelp' | translate }}</span>
        @if (nameError(); as error) {
          <span [id]="id('name-error')" class="field-error">{{ error }}</span>
        }
      </div>
      <div class="field">
        <label class="field-label" [for]="id('path')">{{ 'register.path' | translate }}</label>
        <div class="field-row">
          <input [id]="id('path')" class="input code" name="registerPath" [(ngModel)]="newPath" [placeholder]="'register.pathPlaceholder' | translate" autocomplete="off" spellcheck="false"
            [attr.aria-invalid]="pathError() ? true : null" [attr.aria-describedby]="pathError() ? id('path-error') : id('path-help')" />
          @if (desktop.available) {
            <button type="button" class="btn btn-g" (click)="browseFolder()" [disabled]="registering()">{{ 'register.browse' | translate }}</button>
          }
        </div>
        <span [id]="id('path-help')" class="field-help">{{ 'register.pathHelp' | translate }}</span>
        @if (pathError(); as error) {
          <span [id]="id('path-error')" class="field-error">{{ error }}</span>
        }
      </div>
      @if (registerError(); as error) {
        <p class="field-error" role="alert">{{ error }}</p>
      }
      <span class="visually-hidden" role="status">{{ notice() }}</span>
      @if (notice(); as text) {
        <p class="note-ok" aria-hidden="true">{{ text }}</p>
      }
      <div [class]="inDialog() ? 'dialog-foot' : 'actions'">
        <ng-content />
        <button type="submit" [class]="primary() ? 'btn btn-p' : 'btn btn-g'" [disabled]="registering()">{{ registering() ? ('register.registering' | translate) : submitText() }}</button>
      </div>
    </form>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .actions {
      display: flex;
      align-items: center;
      gap: var(--space-3);
    }
  `,
})
export class RegisterRepositoryComponent {
  private readonly api = inject(RastroApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  // Janela própria (ADR-007): o "Procurar…" ao lado da pasta só existe nela.
  protected readonly desktop = inject(DesktopBridge);

  // Prefixo dos ids (repo-name, register-name...), para o pai e os testes acharem os campos.
  readonly idPrefix = input.required<string>();
  // Vazios: valem o texto padrão do idioma ativo.
  readonly label = input('');
  readonly submitLabel = input('');
  readonly primary = input(false, { transform: booleanAttribute });
  readonly inDialog = input(false, { transform: booleanAttribute });
  readonly registered = output<RepositoryItem>();
  // Avisa o pai de que o cadastro está em andamento (ele trava o Cancelar e o fechar do diálogo).
  readonly busy = output<boolean>();

  private readonly i18n = inject(LanguageService);
  protected readonly labelText = computed(() => this.label() || this.i18n.t('register.label'));
  protected readonly submitText = computed(() => this.submitLabel() || this.i18n.t('register.submit'));
  protected newName = '';
  protected newPath = '';
  protected readonly registering = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly pathError = signal<string | null>(null);
  protected readonly registerError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected id(suffix: string): string {
    return `${this.idPrefix()}-${suffix}`;
  }

  protected register(): void {
    const name = this.newName.trim();
    const path = this.newPath.trim();
    this.registerError.set(null);
    this.notice.set(null);
    this.nameError.set(REPOSITORY_NAME_PATTERN.test(name) ? null : tr(API_ERROR_MESSAGE_KEY['REPOSITORY_NAME_INVALID']));
    this.pathError.set(isAbsoluteFolder(path) ? null : tr(API_ERROR_MESSAGE_KEY['REPOSITORY_PATH_INVALID']));
    if (this.nameError()) return this.focus('name');
    if (this.pathError()) return this.focus('path');

    this.setBusy(true);
    this.api.addRepository(name, path).subscribe({
      next: (created) => {
        this.setBusy(false);
        this.newName = '';
        this.newPath = '';
        this.notice.set(this.i18n.t('register.registered', { name: created.name }));
        this.registered.emit(created);
        this.focus('name');
      },
      error: (error) => {
        this.setBusy(false);
        const code = error instanceof HttpErrorResponse ? (error.error?.code as string | undefined) : undefined;
        const text = apiErrorMessage(error);
        // O erro de cada campo fica junto dele; o resto vai para a mensagem do formulário.
        if (code === 'REPOSITORY_NAME_INVALID' || code === 'REPOSITORY_EXISTS') {
          this.nameError.set(text);
          this.focus('name');
        } else if (code === 'REPOSITORY_PATH_INVALID' || code === 'REPOSITORY_FOLDER_NOT_FOUND') {
          this.pathError.set(text);
          this.focus('path');
        } else {
          this.registerError.set(text);
        }
      },
    });
  }

  // Caixa "Escolher pasta" do sistema, pela janela própria. Cancelar deixa o campo como estava.
  protected async browseFolder(): Promise<void> {
    const path = await this.desktop.pickFolder();
    if (!path) return;
    this.newPath = path;
    this.pathError.set(null);
    this.focus('path');
  }

  private setBusy(value: boolean): void {
    this.registering.set(value);
    this.busy.emit(value);
  }

  private focus(suffix: string): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${this.id(suffix)}`)?.focus());
  }
}
