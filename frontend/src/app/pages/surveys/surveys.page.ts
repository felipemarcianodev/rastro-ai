import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DB_SURVEY_ORIGIN_PARAM, DB_TAB_QUERY_PARAM } from '../../core/database/query-analysis';
import { DialogDirective } from '../../core/dialog.directive';
import { EditorLinks, isAbsoluteFolder } from '../../core/editor-links';
import { plural, shortCommit, shortDate } from '../../core/format';
import { LastRepositoryService } from '../../core/last-repository.service';
import {
  API_ERROR_MESSAGE,
  CreateSurveyRequest,
  REPOSITORY_NAME_PATTERN,
  RepositoryItem,
  SURVEY_STATUS_LABEL,
  SURVEY_STATUS_SLUG,
  SurveyStatus,
  SurveySummary,
  isFinished,
} from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

const POLL_MS = 2500;
// Caminho de projeto TypeScript escolhido no lugar de uma solution (tsconfig.json, tsconfig.app.json).
const TSCONFIG_SUFFIX = '.json';
// Ponto de entrada que parece de componente Angular: "DetalhePedidoComponent.cancelar".
const COMPONENT_ENTRY = /Component\.\w/;
const MIN_DEPTH = 1;
const MAX_DEPTH = 40;
// Linhas do esqueleto da lista enquanto os levantamentos carregam.
const SKELETON_ROWS = [0, 1, 2];
// Parâmetro da URL que pré-seleciona o repositório (usado pelas Configurações).
export const REPOSITORY_QUERY_PARAM = 'repositorio';

// Nome do parâmetro da rota do banco que escolhe a aba (o valor da aba de consulta é DB_TAB_QUERY_PARAM).
const DB_TAB_PARAM = 'aba';

interface RepoCard {
  name: string;
  count: string;
  stack: string | null;
}

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
      'Num projeto Angular, a lista traz o tsconfig (como tsconfig.app.json) e o ponto de entrada é Componente.metodo.',
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
  imports: [FormsModule, RouterLink, DialogDirective],
  templateUrl: './surveys.page.html',
  styleUrl: './surveys.page.scss',
})
export class SurveysPage implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly editor = inject(EditorLinks);
  private readonly lastRepository = inject(LastRepositoryService);
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

  protected readonly skeletonRows = SKELETON_ROWS;

  protected readonly hasRepositories = computed(() => this.repositories().length > 0);
  // Primeiro uso: a lista de repositórios já chegou e está vazia.
  protected readonly firstUse = computed(() => this.reposState() === 'ready' && !this.hasRepositories());

  // Painel "Novo levantamento": abre pelo botão do topo ou pelo "Levantar fluxo" de um cartão.
  protected readonly formOpen = signal(false);
  protected readonly queryTabParams = { [DB_TAB_PARAM]: DB_TAB_QUERY_PARAM };

  // Um cartão por repositório: quantos levantamentos tem e, quando há um, a solution do mais recente.
  protected readonly repoCards = computed<RepoCard[]>(() => {
    const surveys = this.surveys() ?? [];
    return this.repositories().map((repo) => {
      const own = surveys.filter((s) => s.repository === repo.name);
      const latest = own.reduce<SurveySummary | null>((a, b) => (a === null || b.createdAt > a.createdAt ? b : a), null);
      return {
        name: repo.name,
        count: own.length === 0 ? 'Nenhum levantamento' : plural(own.length, 'levantamento', 'levantamentos'),
        stack: latest?.solutionPath ?? null,
      };
    });
  });

  protected dbRoute(repository: string): string[] {
    return ['/repositorios', repository, 'banco'];
  }

  protected surveyOriginParams(id: string): Record<string, string> {
    return { [DB_SURVEY_ORIGIN_PARAM]: id };
  }

  protected openForm(repository?: string): void {
    if (repository && repository !== this.repository) this.selectRepository(repository);
    else if (repository) this.lastRepository.remember(repository);
    this.formOpen.set(true);
  }

  protected closeForm(): void {
    if (!this.submitting() && !this.registerOpen()) this.formOpen.set(false);
  }

  // Diálogo "Cadastrar repositório" (mesma API das Configurações).
  protected readonly registerOpen = signal(false);
  protected readonly registering = signal(false);
  protected newName = '';
  protected newPath = '';
  protected readonly nameError = signal<string | null>(null);
  protected readonly pathError = signal<string | null>(null);
  protected readonly registerError = signal<string | null>(null);

  protected openRegister(): void {
    this.newName = '';
    this.newPath = '';
    this.nameError.set(null);
    this.pathError.set(null);
    this.registerError.set(null);
    this.registerOpen.set(true);
  }

  protected closeRegister(): void {
    if (!this.registering()) this.registerOpen.set(false);
  }

  protected register(): void {
    const name = this.newName.trim();
    const path = this.newPath.trim();
    this.registerError.set(null);
    this.nameError.set(REPOSITORY_NAME_PATTERN.test(name) ? null : API_ERROR_MESSAGE['REPOSITORY_NAME_INVALID']);
    this.pathError.set(isAbsoluteFolder(path) ? null : API_ERROR_MESSAGE['REPOSITORY_PATH_INVALID']);
    if (this.nameError()) return this.focusId('register-name');
    if (this.pathError()) return this.focusId('register-path');

    this.registering.set(true);
    this.api.addRepository(name, path).subscribe({
      next: (created) => {
        this.registering.set(false);
        const list = [...this.repositories(), created].sort((a, b) => a.name.localeCompare(b.name));
        this.repositories.set(list);
        this.editor.set(list);
        this.reposState.set('ready');
        this.registerOpen.set(false);
        this.selectRepository(created.name);
        this.formOpen.set(true);
      },
      error: (error) => {
        this.registering.set(false);
        const code = error instanceof HttpErrorResponse ? (error.error?.code as string | undefined) : undefined;
        const text = apiErrorMessage(error);
        if (code === 'REPOSITORY_NAME_INVALID' || code === 'REPOSITORY_EXISTS') {
          this.nameError.set(text);
          this.focusId('register-name');
        } else if (code === 'REPOSITORY_PATH_INVALID' || code === 'REPOSITORY_FOLDER_NOT_FOUND') {
          this.pathError.set(text);
          this.focusId('register-path');
        } else {
          this.registerError.set(text);
        }
      },
    });
  }

  private focusId(id: string): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus());
  }

  // Fluxo Angular (Fase 26): o caminho escolhido é um tsconfig, e o ponto de entrada vira Componente.metodo.
  protected isTsconfig(): boolean {
    return this.solutionPath.endsWith(TSCONFIG_SUFFIX);
  }

  // null até a API responder; sem resposta, nenhum aviso (não dá para afirmar que falta o Node).
  protected readonly nodeAvailable = signal<boolean | null>(null);

  // Aviso discreto, só onde ele ajuda: sem Node, num repositório sem nenhuma solution (provável
  // projeto só Angular) ou quando o ponto de entrada digitado parece de componente. Mostrar a
  // todo repositório .NET seria ruído.
  protected nodeNotice(): boolean {
    return (
      this.nodeAvailable() === false &&
      !!this.repository &&
      !this.loadingSolutions() &&
      (this.solutions().length === 0 || COMPONENT_ENTRY.test(this.entryPoint))
    );
  }

  // Costura entre sistemas (Fase 25): desligada por padrão. Os outros repositórios vêm primeiro e
  // o próprio por último (a chamada HTTP de um serviço para ele mesmo).
  protected readonly crossChosen = signal<ReadonlySet<string>>(new Set());
  protected readonly crossSelf = signal(false);
  protected readonly crossOthers = computed(() => this.repositories().filter((r) => r.name !== this.repositorySignal()));
  private readonly repositorySignal = signal('');

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
      this.formOpen.set(true);
    }
    this.loadSurveys();
    this.loadRepositories();
    this.api.health().subscribe({
      next: (health) => this.nodeAvailable.set(health?.angular ?? null),
      // Sem o status, a tela só não avisa sobre o Node; o resto do formulário não depende dele.
      error: () => this.nodeAvailable.set(null),
    });
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
        const preselected = this.route.snapshot.queryParamMap.get(REPOSITORY_QUERY_PARAM);
        if (retry && repos.some((r) => r.name === retry.repository)) {
          this.selectRepository(retry.repository, retry.solutionPath);
          const cross = retry.crossRepositories ?? [];
          this.crossChosen.set(new Set(cross.filter((n) => n !== retry.repository)));
          this.crossSelf.set(cross.includes(retry.repository));
          this.formOpen.set(true);
          // Depois do render: o foco vai ao ponto de entrada, com o texto selecionado para trocar.
          setTimeout(() => this.host.nativeElement.querySelector<HTMLInputElement>('#entry')?.select());
        } else if (preselected && repos.some((r) => r.name === preselected)) {
          this.selectRepository(preselected);
          this.formOpen.set(true);
        } else if (repos.length === 1) this.selectRepository(repos[0].name);
      },
      error: (error) => {
        this.reposError.set(apiErrorMessage(error));
        this.reposState.set('error');
      },
    });
  }

  protected toggleCross(name: string, on: boolean): void {
    const next = new Set(this.crossChosen());
    if (on) next.add(name);
    else next.delete(name);
    this.crossChosen.set(next);
  }

  protected selectRepository(name: string, preferredSolution = ''): void {
    this.repository = name;
    this.repositorySignal.set(name);
    // O repositório escolhido deixa de ser "outro": sai da lista de costura.
    this.toggleCross(name, false);
    this.solutionPath = '';
    this.solutions.set([]);
    if (!name) return;
    this.lastRepository.remember(name);

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
    const cross = this.crossRepositories();
    this.api
      .createSurvey({
        repository: this.repository,
        solutionPath: this.solutionPath,
        entryPoint: this.entryPoint.trim(),
        maxDepth: this.maxDepth,
        ...(cross.length > 0 ? { crossRepositories: cross } : {}),
      })
      .subscribe({
        next: (created) => this.router.navigate(['/surveys', created.id]),
        error: (error) => {
          this.formError.set(apiErrorMessage(error));
          this.submitting.set(false);
        },
      });
  }

  // Os outros marcados, na ordem do cadastro, e o próprio repositório por último.
  private crossRepositories(): string[] {
    const others = this.crossOthers().map((r) => r.name).filter((name) => this.crossChosen().has(name));
    return this.crossSelf() ? [...others, this.repository] : others;
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
