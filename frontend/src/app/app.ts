import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AiStatusService } from './core/ai-status.service';
import { LastRepositoryService } from './core/last-repository.service';
import { RepositoryItem } from './core/models';
import { RastroApi } from './core/rastro-api.service';

// Áreas do produto no cabeçalho.
export enum NavArea {
  Surveys = 0,
  Database = 1,
  Settings = 2,
}

const SETTINGS_PATH = '/configuracoes';
const DATABASE_PREFIX = '/repositorios/';
const SURVEY_PREFIX = '/surveys/';
const MAIN_ID = 'conteudo';
const FRAGMENT_EDITOR = 'editor';
export const FRAGMENT_AI = 'ia';

interface BancoTarget {
  commands: string[];
  fragment: string | undefined;
}

export function areaOf(url: string): NavArea | null {
  const path = url.split(/[?#]/)[0];
  if (path === '/' || path === '' || path.startsWith(SURVEY_PREFIX)) return NavArea.Surveys;
  if (path.startsWith(DATABASE_PREFIX)) return NavArea.Database;
  if (path === SETTINGS_PATH) return NavArea.Settings;
  return null;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly last = inject(LastRepositoryService);
  protected readonly title = 'Rastro AI';
  protected readonly ai = inject(AiStatusService);
  protected readonly Area = NavArea;
  protected readonly aiFragment = FRAGMENT_AI;

  private readonly url = signal(this.router.url);
  private readonly repositories = signal<RepositoryItem[]>([]);

  protected readonly area = computed(() => areaOf(this.url()));

  // Último repositório usado se ainda existe; senão o único; senão o primeiro. Sem nenhum, Configurações.
  protected readonly bancoTarget = computed<BancoTarget>(() => {
    const repos = this.repositories();
    if (repos.length === 0) return { commands: [SETTINGS_PATH], fragment: FRAGMENT_EDITOR };
    const last = this.last.name();
    const chosen = repos.find((r) => r.name === last) ?? repos[0];
    return { commands: ['/repositorios', chosen.name, 'banco'], fragment: undefined };
  });

  constructor() {
    this.ai.refresh();
    this.loadRepositories();
    let previous = areaOf(this.router.url);
    let previousPath: string | null = null;
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe((e) => {
      this.url.set(e.urlAfterRedirects);
      // UX-FIM-C12: página nova leva o foco ao h1. Não na primeira carga; troca só de query ou fragmento não conta.
      const path = e.urlAfterRedirects.split(/[?#]/)[0];
      if (previousPath !== null && path !== previousPath) this.focusHeading();
      previousPath = path;
      // Saiu das Configurações: a lista de repositórios pode ter mudado.
      const current = areaOf(e.urlAfterRedirects);
      if (previous === NavArea.Settings && current !== NavArea.Settings) this.loadRepositories();
      previous = current;
    });
  }

  // UX-FIM-C03: o link de pular foca o main sem trocar a rota.
  protected skipToContent(event: Event, target: HTMLElement): void {
    event.preventDefault();
    target.focus({ preventScroll: true });
  }

  // Depois de a página desenhar, foca o h1, a menos que a página já tenha posto o foco num item
  // dentro do conteúdo (a seleção de ?passo= e ?tabela=).
  private focusHeading(): void {
    setTimeout(() => {
      const main = document.getElementById(MAIN_ID);
      const active = document.activeElement;
      const onItem = !!main && active instanceof HTMLElement && active !== main && active !== document.body && main.contains(active);
      if (onItem) return;
      const heading = main?.querySelector<HTMLElement>('h1');
      if (!heading) return;
      if (!heading.hasAttribute('tabindex')) heading.setAttribute('tabindex', '-1');
      heading.focus({ preventScroll: true });
    });
  }

  private loadRepositories(): void {
    this.api.repositories().subscribe({
      next: (repos) => this.repositories.set(repos),
      // Sem a lista, o "Banco" cai nas Configurações.
      error: () => this.repositories.set([]),
    });
  }
}
