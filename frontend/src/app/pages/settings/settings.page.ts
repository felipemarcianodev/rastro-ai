import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiStatusService } from '../../core/ai-status.service';
import { CopyFeedback } from '../../core/copy-feedback';
import { EditorLinks, isAbsoluteFolder } from '../../core/editor-links';
import { ActivatedRoute, Router } from '@angular/router';
import { shortDate } from '../../core/format';
import {
  AI_PROVIDER_LABEL,
  AI_PROVIDER_SLUG,
  API_ERROR_MESSAGE,
  AiProvider,
  AiSettingsUpdate,
  AiSettingsView,
  REPOSITORY_NAME_PATTERN,
  RepositoryItem,
} from '../../core/models';
import { ConfirmDialogComponent } from '../survey/confirm-dialog.component';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Sem caminho de máquina: o usuário troca o marcador pela pasta onde o Rastro está.
const MCP_COMMAND = 'claude mcp add rastro -- rastro mcp';

interface ProviderForm {
  model: string;
  // Chave digitada agora (vazia = não mexer); removeKey apaga a salva.
  key: string;
  removeKey: boolean;
  hasKey: boolean;
}

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule, ConfirmDialogComponent],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss',
})
export class SettingsPage implements OnInit {
  private readonly api = inject(RastroApi);
  protected readonly ai = inject(AiStatusService);

  protected readonly loaded = signal<AiSettingsView | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveMessage = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly commandCopy = new CopyFeedback();
  private readonly editor = inject(EditorLinks);
  private readonly location = inject(Location);
  private readonly router = inject(Router);
  protected readonly repositories = signal<RepositoryItem[]>([]);
  protected readonly reposState = signal<'loading' | 'error' | 'ready'>('loading');
  private readonly route = inject(ActivatedRoute);

  // Formulário "Adicionar repositório".
  protected newName = '';
  protected newPath = '';
  protected readonly adding = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly pathError = signal<string | null>(null);
  protected readonly repoMessage = signal<{ ok: boolean; text: string } | null>(null);

  // Remoção: o diálogo de confirmação do tema.
  protected readonly pendingRemoval = signal<RepositoryItem | null>(null);
  protected readonly removing = signal(false);
  protected readonly removalText = computed(() => {
    const repo = this.pendingRemoval();
    return repo
      ? `O repositório "${repo.name}" sai desta lista e o Rastro perde os comandos de build e testes dele. Os levantamentos já feitos continuam guardados. A pasta no disco não é tocada.`
      : '';
  });

  protected enabled = false;
  protected provider = AiProvider.Claude;
  protected budget = 0;
  protected useFillHoles = true;
  protected useDescribeRules = true;
  protected useSuggestions = true;
  protected useContextSuggestions = true;
  protected readonly forms: Record<AiProvider, ProviderForm> = {
    [AiProvider.Claude]: { model: '', key: '', removeKey: false, hasKey: false },
    [AiProvider.OpenAi]: { model: '', key: '', removeKey: false, hasKey: false },
    [AiProvider.Gemini]: { model: '', key: '', removeKey: false, hasKey: false },
  };

  protected readonly providers = [AiProvider.Claude, AiProvider.OpenAi, AiProvider.Gemini];
  protected readonly providerLabel = AI_PROVIDER_LABEL;
  protected readonly mcpCommand = MCP_COMMAND;
  protected readonly shortDate = shortDate;

  protected readonly usageText = computed(() => {
    const status = this.ai.status();
    if (!status) return '';
    const used = status.tokensToday.toLocaleString('pt-BR');
    return status.dailyTokenBudget > 0
      ? `Hoje: ${used} de ${status.dailyTokenBudget.toLocaleString('pt-BR')} tokens.`
      : `Hoje: ${used} tokens (sem limite).`;
  });

  ngOnInit(): void {
    this.ai.refresh();
    this.loadRepositories();
    this.api.aiSettings().subscribe({
      next: (view) => {
        this.fill(view);
        this.focusFromFragment();
      },
      error: (error) => this.loadError.set(apiErrorMessage(error)),
    });
  }

  protected save(): void {
    if (!Number.isInteger(Number(this.budget)) || Number(this.budget) < 0) {
      this.saveMessage.set({ ok: false, text: 'O orçamento diário precisa ser um número inteiro a partir de 0.' });
      return;
    }
    const keyOf = (form: ProviderForm): string | null => (form.removeKey ? '' : form.key.trim() || null);
    const update: AiSettingsUpdate = {
      enabled: this.enabled,
      provider: this.provider,
      claudeModel: this.forms[AiProvider.Claude].model,
      claudeKey: keyOf(this.forms[AiProvider.Claude]),
      openAiModel: this.forms[AiProvider.OpenAi].model,
      openAiKey: keyOf(this.forms[AiProvider.OpenAi]),
      geminiModel: this.forms[AiProvider.Gemini].model,
      geminiKey: keyOf(this.forms[AiProvider.Gemini]),
      dailyTokenBudget: Number(this.budget),
      useFillHoles: this.useFillHoles,
      useDescribeRules: this.useDescribeRules,
      useSuggestions: this.useSuggestions,
      useContextSuggestions: this.useContextSuggestions,
    };

    this.saving.set(true);
    this.saveMessage.set(null);
    this.api.saveAiSettings(update).subscribe({
      next: (view) => {
        this.fill(view);
        this.saving.set(false);
        this.saveMessage.set({ ok: true, text: 'Configuração salva.' });
        this.ai.refresh();
      },
      error: (error) => {
        this.saving.set(false);
        this.saveMessage.set({ ok: false, text: apiErrorMessage(error) });
      },
    });
  }

  // A tela anterior é do app quando a navegação não é a primeira da sessão; senão volta para a lista.
  protected back(): void {
    const state = this.location.getState() as { navigationId?: number } | null;
    if ((state?.navigationId ?? 1) > 1) this.location.back();
    else void this.router.navigateByUrl('/');
  }

  protected loadRepositories(): void {
    this.reposState.set('loading');
    this.api.repositories().subscribe({
      next: (repos) => {
        this.repositories.set(repos);
        this.editor.set(repos);
        this.reposState.set('ready');
        this.focusFromFragment();
      },
      error: () => this.reposState.set('error'),
    });
  }

  protected addRepository(): void {
    const name = this.newName.trim();
    const path = this.newPath.trim();
    this.repoMessage.set(null);
    this.nameError.set(REPOSITORY_NAME_PATTERN.test(name) ? null : API_ERROR_MESSAGE['REPOSITORY_NAME_INVALID']);
    this.pathError.set(isAbsoluteFolder(path) ? null : API_ERROR_MESSAGE['REPOSITORY_PATH_INVALID']);
    if (this.nameError()) return this.focusField('repo-name');
    if (this.pathError()) return this.focusField('repo-path');

    this.adding.set(true);
    this.api.addRepository(name, path).subscribe({
      next: (created) => {
        this.adding.set(false);
        this.applyList([...this.repositories(), created]);
        this.newName = '';
        this.newPath = '';
        this.repoMessage.set({ ok: true, text: `Repositório "${created.name}" cadastrado.` });
        this.focusField('repo-name');
      },
      error: (error) => {
        this.adding.set(false);
        const code = error instanceof HttpErrorResponse ? (error.error?.code as string | undefined) : undefined;
        const text = apiErrorMessage(error);
        // O erro de cada campo fica junto dele; o resto vai para a mensagem do formulário.
        if (code === 'REPOSITORY_NAME_INVALID' || code === 'REPOSITORY_EXISTS') {
          this.nameError.set(text);
          this.focusField('repo-name');
        } else if (code === 'REPOSITORY_PATH_INVALID' || code === 'REPOSITORY_FOLDER_NOT_FOUND') {
          this.pathError.set(text);
          this.focusField('repo-path');
        } else {
          this.repoMessage.set({ ok: false, text });
        }
      },
    });
  }

  protected askRemove(repo: RepositoryItem): void {
    this.repoMessage.set(null);
    this.pendingRemoval.set(repo);
  }

  protected cancelRemove(): void {
    if (!this.removing()) this.pendingRemoval.set(null);
  }

  protected confirmRemove(): void {
    const repo = this.pendingRemoval();
    if (!repo) return;
    this.removing.set(true);
    this.api.removeRepository(repo.name).subscribe({
      next: () => {
        this.removing.set(false);
        this.pendingRemoval.set(null);
        this.applyList(this.repositories().filter((r) => r.name !== repo.name));
        this.repoMessage.set({ ok: true, text: `Repositório "${repo.name}" removido. Os levantamentos continuam guardados.` });
        this.focusField('repo-name');
      },
      error: (error) => {
        this.removing.set(false);
        this.pendingRemoval.set(null);
        this.repoMessage.set({ ok: false, text: apiErrorMessage(error) });
        // Já não estava cadastrado: a lista da tela estava velha.
        if (error instanceof HttpErrorResponse && error.status === 404) this.loadRepositories();
      },
    });
  }

  private applyList(repos: RepositoryItem[]): void {
    const sorted = [...repos].sort((a, b) => a.name.localeCompare(b.name));
    this.repositories.set(sorted);
    this.editor.set(sorted);
  }

  private focusField(id: string): void {
    setTimeout(() => document.getElementById(id)?.focus());
  }

  // O painel do passo manda para /configuracoes#editor: rola até o cartão e foca o 1º campo.
  // O cartão só existe depois que a configuração e a lista chegam; tenta nas duas chegadas.
  private fragmentDone = false;

  private focusFromFragment(): void {
    if (this.fragmentDone || this.route.snapshot.fragment !== 'editor') return;
    setTimeout(() => {
      const input = document.querySelector<HTMLElement>('#editor input');
      if (!input) return;
      this.fragmentDone = true;
      document.getElementById('editor')?.scrollIntoView({ block: 'start' });
      input.focus({ preventScroll: true });
    });
  }

  protected copyCommand(): void {
    this.commandCopy.copy(this.mcpCommand);
  }

  protected slug(provider: AiProvider): string {
    return AI_PROVIDER_SLUG[provider];
  }

  private fill(view: AiSettingsView): void {
    this.loaded.set(view);
    this.enabled = view.enabled;
    this.provider = view.provider;
    this.budget = view.dailyTokenBudget;
    this.useFillHoles = view.useFillHoles;
    this.useDescribeRules = view.useDescribeRules;
    this.useSuggestions = view.useSuggestions;
    this.useContextSuggestions = view.useContextSuggestions;
    this.forms[AiProvider.Claude] = { model: view.claudeModel, key: '', removeKey: false, hasKey: view.claudeHasKey };
    this.forms[AiProvider.OpenAi] = { model: view.openAiModel, key: '', removeKey: false, hasKey: view.openAiHasKey };
    this.forms[AiProvider.Gemini] = { model: view.geminiModel, key: '', removeKey: false, hasKey: view.geminiHasKey };
  }
}
