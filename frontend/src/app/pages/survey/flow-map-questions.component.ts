import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiDisclosureComponent } from '../../core/ai-disclosure.component';
import { ANSWER_SEAL_GLYPH, ANSWER_SEAL_LABEL, ANSWER_SEAL_SLUG, QUESTION_ROLE_LABEL, filterBy } from '../../core/graph/flow-insight';
import { readingLabel } from '../../core/graph/summary';
import { AI_PROVIDER_LABEL, AiProvider, FlowAnswer, FlowNode, FlowQuestion, QuestionRole } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

interface Shown {
  question: string;
  answer: FlowAnswer;
  // Só a resposta da pergunta livre vem de um provedor de IA.
  provider: AiProvider | null;
}

// Perguntas sobre o fluxo (Fase 33): as prontas por papel, respondidas só com o que o código prova,
// e a pergunta livre, que só existe com a IA ligada e o uso Sugestões habilitado (ADR-001).
@Component({
  selector: 'app-flow-map-questions',
  imports: [FormsModule, AiDisclosureComponent],
  templateUrl: './flow-map-questions.component.html',
  styleUrl: './flow-map-questions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlowMapQuestionsComponent {
  private readonly api = inject(RastroApi);

  readonly surveyId = input.required<string>();
  readonly canAsk = input(false);
  readonly nodes = input<ReadonlyMap<string, FlowNode>>(new Map());
  // Fonte clicada: o nó do grafo (o mapa escolhe também o bloco que o contém).
  readonly pickNode = output<string>();

  protected readonly questions = signal<FlowQuestion[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly role = signal<QuestionRole | null>(null);
  protected readonly shown = signal<Shown | null>(null);
  protected readonly chosenId = signal<string | null>(null);
  protected readonly visible = computed(() => filterBy(this.questions() ?? [], (q) => q.role, this.role()));
  protected readonly asking = signal(false);
  protected readonly askError = signal<string | null>(null);
  protected text = '';
  protected readonly filters: { value: QuestionRole | null; label: string }[] = [
    { value: null, label: 'Todos' },
    ...[QuestionRole.TechLead, QuestionRole.Staff, QuestionRole.Manager].map((value) => ({ value, label: QUESTION_ROLE_LABEL[value] })),
  ];
  protected readonly roleLabel = QUESTION_ROLE_LABEL;
  protected readonly sealLabel = ANSWER_SEAL_LABEL;
  protected readonly sealGlyph = ANSWER_SEAL_GLYPH;
  protected readonly sealSlug = ANSWER_SEAL_SLUG;
  protected readonly providerLabel = AI_PROVIDER_LABEL;
  private readonly reload = signal(0);

  constructor() {
    effect((onCleanup) => {
      const id = this.surveyId();
      this.reload();
      untracked(() => this.error.set(null));
      const sub = this.api.questions(id).subscribe({
        next: (questions) => untracked(() => this.questions.set(questions)),
        error: (error) => untracked(() => this.error.set(apiErrorMessage(error))),
      });
      onCleanup(() => sub.unsubscribe());
    });
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected choose(question: FlowQuestion): void {
    this.chosenId.set(question.id);
    this.shown.set({ question: question.text, answer: question.answer, provider: null });
  }

  protected ask(): void {
    const question = this.text.trim();
    if (!question || this.asking() || !this.canAsk()) return;
    this.asking.set(true);
    this.askError.set(null);
    this.api.ask(this.surveyId(), question).subscribe({
      next: (reply) => {
        this.asking.set(false);
        this.chosenId.set(null);
        this.shown.set({ question, answer: reply.answer, provider: reply.provider });
      },
      error: (error) => {
        this.asking.set(false);
        this.askError.set(apiErrorMessage(error));
      },
    });
  }

  protected sourceText(nodeId: string): string {
    const node = this.nodes().get(nodeId);
    return node ? readingLabel(node) : nodeId;
  }
}
