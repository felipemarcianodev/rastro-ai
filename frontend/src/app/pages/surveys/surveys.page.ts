import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { plural, shortCommit, shortDate } from '../../core/format';
import {
  CreateSurveyRequest,
  RepositoryItem,
  SURVEY_STATUS_LABEL,
  SURVEY_STATUS_SLUG,
  SurveyStatus,
  SurveySummary,
  isFinished,
} from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

const POLL_MS = 2500;
const MIN_DEPTH = 1;
const MAX_DEPTH = 40;

type Field = 'repository' | 'solution' | 'entry' | 'depth';

// Explicação de cada campo (botão "i"): o resumo vai no title; o texto, no quadro que abre.
interface FieldInfo {
  summary: string;
  text: string[];
}

const FIELD_INFO: Record<Field, FieldInfo> = {
  repository: {
    summary: 'A pasta do código que o Rastro vai ler.',
    text: [
      'A pasta do código que o Rastro vai ler, cadastrada em Configurações. O Rastro só lê o código: não executa nada nem precisa do sistema no ar.',
    ],
  },
  solution: {
    summary: 'O arquivo .sln ou .slnx que reúne os projetos.',
    text: [
      'O arquivo .sln ou .slnx que reúne os projetos do sistema. O Rastro compila por ele para saber quem chama quem, inclusive a implementação real atrás de cada interface.',
      'Se houver mais de uma, escolha a que contém o projeto onde está o ponto de entrada.',
    ],
  },
  entry: {
    summary: 'O método onde a funcionalidade começa, como Classe.Metodo.',
    text: [
      'O método onde a funcionalidade começa: a action do controller que recebe a requisição, o Execute do job ou o Handle do handler da mensagem.',
      'Escreva a classe e o método: PedidosController.Criar. Se houver duas classes com esse nome, inclua o namespace: Loja.Api.Controllers.PedidosController.Criar.',
      'Não serve a rota (/api/pedidos), nem o namespace sem a classe.',
    ],
  },
  depth: {
    summary: 'Quantos níveis de chamada seguir a partir do ponto de entrada.',
    text: [
      'Quantos níveis de chamada o Rastro segue a partir do ponto de entrada. 15 serve para a maioria dos fluxos.',
      'Onde o limite corta, o caminho fica marcado no grafo, e dá para continuar dali depois.',
    ],
  },
};

// Filtro da lista por estado (chips). Andamento junta "na fila" e "analisando".
export enum SurveyFilter {
  All = 0,
  Completed = 1,
  InProgress = 2,
  Failed = 3,
}

const FILTER_LABEL: Record<SurveyFilter, string> = {
  [SurveyFilter.All]: 'Todos',
  [SurveyFilter.Completed]: 'Prontos',
  [SurveyFilter.InProgress]: 'Em andamento',
  [SurveyFilter.Failed]: 'Com falha',
};

const FILTER_MATCH: Record<SurveyFilter, (status: SurveyStatus) => boolean> = {
  [SurveyFilter.All]: () => true,
  [SurveyFilter.Completed]: (status) => status === SurveyStatus.Completed,
  [SurveyFilter.InProgress]: (status) => !isFinished(status),
  [SurveyFilter.Failed]: (status) => status === SurveyStatus.Failed,
};

// Busca sem acento e sem caixa: "aprovacao" acha "AprovacaoPedido".
export function matchesSearch(survey: SurveySummary, query: string): boolean {
  const normalize = (text: string) => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const terms = normalize(query).split(/\s+/).filter(Boolean);
  const haystack = normalize(`${survey.entryPoint} ${survey.repository} ${survey.solutionPath} ${survey.branch ?? ''}`);
  return terms.every((term) => haystack.includes(term));
}

@Component({
  selector: 'app-surveys-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './surveys.page.html',
  styleUrl: './surveys.page.scss',
})
export class SurveysPage implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private pollTimer?: ReturnType<typeof setTimeout>;
  // Pedido de um levantamento que falhou (estado da navegação), para preencher o formulário.
  private retry: CreateSurveyRequest | null = null;

  protected readonly surveys = signal<SurveySummary[] | null>(null);
  protected readonly listError = signal<string | null>(null);
  // Aviso de exclusão vindo da página do levantamento (estado da navegação).
  protected readonly deletedNotice = signal<string | null>(null);
  protected readonly repositories = signal<RepositoryItem[]>([]);
  protected readonly reposState = signal<'loading' | 'error' | 'ready'>('loading');
  protected readonly reposError = signal<string | null>(null);
  protected readonly solutions = signal<string[]>([]);
  protected readonly loadingSolutions = signal(false);

  protected repository = '';
  protected solutionPath = '';
  protected entryPoint = '';
  protected maxDepth = 15;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Partial<Record<Field, string>>>({});
  protected readonly fieldInfo = FIELD_INFO;
  protected readonly openInfo = signal<Field | null>(null);

  protected readonly statusLabel = SURVEY_STATUS_LABEL;
  protected readonly statusSlug = SURVEY_STATUS_SLUG;
  protected readonly SurveyStatus = SurveyStatus;
  protected readonly shortDate = shortDate;
  protected readonly shortCommit = shortCommit;
  protected readonly plural = plural;

  protected readonly hasRepositories = computed(() => this.repositories().length > 0);

  protected readonly query = signal('');
  protected readonly filter = signal(SurveyFilter.All);
  protected readonly filters = [SurveyFilter.All, SurveyFilter.Completed, SurveyFilter.InProgress, SurveyFilter.Failed];
  protected readonly filterLabel = FILTER_LABEL;
  protected readonly filterCounts = computed(() => {
    const surveys = this.surveys() ?? [];
    return new Map(this.filters.map((f) => [f, surveys.filter((s) => FILTER_MATCH[f](s.status)).length]));
  });
  protected readonly visible = computed(() =>
    (this.surveys() ?? []).filter((s) => FILTER_MATCH[this.filter()](s.status) && matchesSearch(s, this.query())),
  );
  // Leitor de tela: avisa quando um levantamento termina (o polling troca a lista em silêncio).
  protected readonly announcement = signal('');
  private lastStatus = new Map<string, SurveyStatus>();

  protected clearFilters(): void {
    this.query.set('');
    this.filter.set(SurveyFilter.All);
  }

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));
  }

  ngOnInit(): void {
    const deleted = (history.state as { deleted?: string } | null)?.deleted;
    if (deleted) {
      this.deletedNotice.set(`Levantamento de ${deleted} excluído.`);
      setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('#list-title')?.focus());
      setTimeout(() => this.deletedNotice.set(null), 5000);
    }
    this.retry = (history.state as { retry?: CreateSurveyRequest } | null)?.retry ?? null;
    if (this.retry) {
      this.entryPoint = this.retry.entryPoint;
      this.maxDepth = this.retry.maxDepth ?? this.maxDepth;
    }
    this.loadSurveys();
    this.loadRepositories();
  }

  // Estados do campo: carregando, erro com "Tentar de novo" junto dele, vazio (falta configurar).
  protected loadRepositories(): void {
    this.reposState.set('loading');
    this.reposError.set(null);
    this.api.repositories().subscribe({
      next: (repos) => {
        this.repositories.set(repos);
        this.reposState.set('ready');
        const retry = this.retry;
        this.retry = null;
        if (retry && repos.some((r) => r.name === retry.repository)) {
          this.selectRepository(retry.repository, retry.solutionPath);
          // Depois do render: o foco vai ao ponto de entrada, com o texto selecionado para trocar.
          setTimeout(() => this.host.nativeElement.querySelector<HTMLInputElement>('#entry')?.select());
        } else if (repos.length === 1) this.selectRepository(repos[0].name);
      },
      error: (error) => {
        this.reposError.set(apiErrorMessage(error));
        this.reposState.set('error');
      },
    });
  }

  protected selectRepository(name: string, preferredSolution = ''): void {
    this.repository = name;
    this.solutionPath = '';
    this.solutions.set([]);
    if (!name) return;

    this.loadingSolutions.set(true);
    this.api.solutions(name).subscribe({
      next: (solutions) => {
        this.solutions.set(solutions);
        this.solutionPath = solutions.includes(preferredSolution) ? preferredSolution : (solutions[0] ?? '');
        this.loadingSolutions.set(false);
      },
      error: (error) => {
        this.formError.set(apiErrorMessage(error));
        this.loadingSolutions.set(false);
      },
    });
  }

  protected toggleInfo(field: Field): void {
    this.openInfo.set(this.openInfo() === field ? null : field);
  }

  protected retryList(): void {
    this.listError.set(null);
    this.loadSurveys();
  }

  protected submit(): void {
    this.formError.set(null);
    const errors = this.validate();
    this.fieldErrors.set(errors);
    const first = (['repository', 'solution', 'entry', 'depth'] as Field[]).find((f) => errors[f]);
    if (first) {
      this.host.nativeElement.querySelector<HTMLElement>(`#${first}`)?.focus();
      return;
    }

    this.submitting.set(true);
    this.api
      .createSurvey({
        repository: this.repository,
        solutionPath: this.solutionPath,
        entryPoint: this.entryPoint.trim(),
        maxDepth: this.maxDepth,
      })
      .subscribe({
        next: (created) => this.router.navigate(['/surveys', created.id]),
        error: (error) => {
          this.formError.set(apiErrorMessage(error));
          this.submitting.set(false);
        },
      });
  }

  private validate(): Partial<Record<Field, string>> {
    const errors: Partial<Record<Field, string>> = {};
    if (!this.repository) errors.repository = 'Escolha um repositório.';
    if (!this.solutionPath) errors.solution = 'Escolha uma solution.';
    if (!this.entryPoint.trim()) errors.entry = 'Informe o método de entrada, por exemplo PedidosController.Criar.';
    const depth = Number(this.maxDepth);
    if (!Number.isInteger(depth) || depth < MIN_DEPTH || depth > MAX_DEPTH)
      errors.depth = `Use um número inteiro de ${MIN_DEPTH} a ${MAX_DEPTH}.`;
    return errors;
  }

  private loadSurveys(): void {
    clearTimeout(this.pollTimer);
    this.api.surveys().subscribe({
      next: (surveys) => {
        const finished = surveys.filter((s) => {
          const before = this.lastStatus.get(s.id);
          return before !== undefined && !isFinished(before) && isFinished(s.status);
        });
        if (finished.length > 0) this.announcement.set(finished.map((s) => `${s.entryPoint}: ${this.statusLabel[s.status]}`).join('. '));
        this.lastStatus = new Map(surveys.map((s) => [s.id, s.status]));
        this.surveys.set(surveys);
        this.listError.set(null);
        if (surveys.some((s) => !isFinished(s.status))) this.pollTimer = setTimeout(() => this.loadSurveys(), POLL_MS);
      },
      error: (error) => this.listError.set(apiErrorMessage(error)),
    });
  }
}
