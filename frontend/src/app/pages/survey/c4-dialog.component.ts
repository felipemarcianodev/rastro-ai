import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogDirective } from '../../core/dialog.directive';
import { RouterLink } from '@angular/router';
import { AI_PROVIDER_LABEL, AiUse, C4Export, C4Person, PeopleSuggestion, aiCan } from '../../core/models';
import { AiStatusService } from '../../core/ai-status.service';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { CopyFeedback } from '../../core/copy-feedback';

const MAX_PEOPLE = 10;

// Linha editável do C1; fromAi some quando o usuário mexe no nome ou na descrição.
interface PersonRow {
  name: string;
  description: string;
  fromAi: boolean;
}

// Diagramas C4 do fluxo em Structurizr DSL (Fase 11). O código dá C2, C3 e C4; as pessoas do C1
// o compilador não sabe, então o usuário informa aqui (ficam salvas com o levantamento).
@Component({
  selector: 'app-c4-dialog',
  imports: [FormsModule, DialogDirective, RouterLink],
  templateUrl: './c4-dialog.component.html',
  styleUrl: './c4-dialog.component.scss',
})
export class C4DialogComponent implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly ai = inject(AiStatusService);
  protected readonly canSuggest = computed(() => aiCan(this.ai.status(), AiUse.ContextSuggestions));
  protected readonly suggested = signal<PeopleSuggestion | null>(null);
  protected readonly suggesting = signal(false);
  protected readonly suggestError = signal<string | null>(null);
  protected readonly acceptedAll = signal(false);
  protected readonly providerLabel = AI_PROVIDER_LABEL;

  readonly surveyId = input.required<string>();
  readonly closed = output<void>();

  protected readonly dsl = signal<string | null>(null);
  protected readonly people = signal<PersonRow[]>([]);
  protected readonly saving = signal(false);
  protected readonly message = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly dslCopy = new CopyFeedback();
  protected readonly loadError = signal<string | null>(null);
  // Linhas com descrição e sem nome (índice da linha).
  protected readonly nameErrors = signal<Set<number>>(new Set());
  protected readonly maxPeople = MAX_PEOPLE;
  // O DSL e o download usam as pessoas salvas: o que mudou e não foi salvo fica sinalizado.
  private saved = '[]';

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadError.set(null);
    this.api.c4(this.surveyId()).subscribe({
      next: (result) => this.show(result),
      error: (error) => this.loadError.set(apiErrorMessage(error)),
    });
  }

  protected addPerson(): void {
    if (this.people().length < MAX_PEOPLE) this.people.update((list) => [...list, { name: '', description: '', fromAi: false }]);
  }

  protected removePerson(index: number): void {
    this.people.update((list) => list.filter((_, i) => i !== index));
    this.nameErrors.set(new Set());
  }

  protected savePeople(): void {
    const missing = new Set(this.people().flatMap((p, i) => (!p.name.trim() && p.description.trim() ? [i] : [])));
    this.nameErrors.set(missing);
    if (missing.size > 0) {
      this.message.set({ ok: false, text: 'Dê um nome a cada pessoa com descrição.' });
      document.getElementById(`c4-name-${[...missing][0]}`)?.focus();
      return;
    }
    const people: C4Person[] = this.people()
      .filter((p) => p.name.trim() || p.description.trim())
      .map((p) => ({ name: p.name, description: p.description || undefined, ...(p.fromAi ? { fromAi: true } : {}) }));
    this.saving.set(true);
    this.api.saveC4People(this.surveyId(), people).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.show(result);
        this.message.set({ ok: true, text: 'Pessoas salvas. O C1 já mostra quem usa o sistema.' });
      },
      error: (error) => {
        this.saving.set(false);
        this.message.set({ ok: false, text: apiErrorMessage(error) });
      },
    });
  }

  protected copy(): void {
    const text = this.dsl();
    if (text) this.dslCopy.copy(text);
  }

  protected suggestPeople(): void {
    this.suggesting.set(true);
    this.suggestError.set(null);
    this.api.suggestPeople(this.surveyId()).subscribe({
      next: (result) => {
        this.suggesting.set(false);
        this.acceptedAll.set(false);
        this.suggested.set(result);
        this.ai.refresh();
      },
      error: (error) => {
        this.suggesting.set(false);
        this.suggestError.set(apiErrorMessage(error));
      },
    });
  }

  // Leva a sugestão para a lista; ela só vale depois de "Salvar pessoas".
  protected addSuggested(person: C4Person): void {
    if (this.people().length >= MAX_PEOPLE) return;
    this.people.update((list) => [...list, { name: person.name, description: person.description ?? '', fromAi: true }]);
    this.suggested.update((s) => (s ? { ...s, people: s.people.filter((p) => p !== person) } : s));
    // Foco no próximo "Acrescentar" ou, no fim, em "Salvar pessoas".
    this.acceptedAll.set(this.suggested()?.people.length === 0);
    setTimeout(() => {
      const next = document.querySelector<HTMLElement>('.ai-suggestion button');
      (next ?? Array.from(document.querySelectorAll<HTMLElement>('.people-actions button')).find((b) => b.textContent?.includes('Salvar')))?.focus();
    });
  }

  protected unsaved(): boolean {
    return JSON.stringify(this.normalized()) !== this.saved;
  }

  private show(result: C4Export): void {
    this.dsl.set(result.dsl);
    this.people.set(result.people.map((p) => ({ name: p.name, description: p.description ?? '', fromAi: p.fromAi === true })));
    this.saved = JSON.stringify(this.normalized());
  }

  // Editar o texto de uma pessoa sugerida a torna do usuário: o selo "IA" sai.
  protected edited(index: number): void {
    if (this.people()[index]?.fromAi) this.people.update((list) => list.map((p, i) => (i === index ? { ...p, fromAi: false } : p)));
  }

  private normalized(): PersonRow[] {
    return this.people()
      .filter((p) => p.name.trim() || p.description.trim())
      .map((p) => ({ name: p.name.trim(), description: p.description.trim(), fromAi: p.fromAi }));
  }

  protected downloadUrl(): string {
    return `/api/surveys/${this.surveyId()}/c4/workspace.dsl`;
  }
}
