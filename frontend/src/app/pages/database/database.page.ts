import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CopyFeedback } from '../../core/copy-feedback';
import { DB_CARD, DB_CARD_MAX_ROWS, dbMapGeometry, dbMapSvg } from '../../core/database/db-map';
import {
  DbSchema,
  DbUsageReport,
  KeyMark,
  sameName,
  SCHEMA_FINDING_BADGE,
  SCHEMA_SOURCE_LABEL,
  SQL_DIALECT_LABEL,
  SchemaSourceKind,
  SqlDialect,
  findTable,
  matches,
  neighborhood,
  relationsOf,
  tableKey,
  toErMermaid,
} from '../../core/database/db-schema';
import { DB_TAB_QUERY_PARAM } from '../../core/database/query-analysis';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { readDiagramColors } from '../../core/mermaid-render';
import { RepositoryItem } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { LastRepositoryService } from '../../core/last-repository.service';
import { DbItemKind, DbPick, DbPanelComponent } from './db-panel.component';
import { LiveConnectionView } from '../../core/database/live-database';
import { LiveConnectionDialogComponent } from './live-connection-dialog.component';
import { QueryAnalysisComponent } from './query-analysis.component';

const MESSAGE_MS = 4000;
const REVEAL_MARGIN = 16;
// Atraso da busca da lista antes de refiltrar (UX-FIM-B10): em banco grande, não refiltra a cada tecla.
const SEARCH_DEBOUNCE_MS = 150;
// Abaixo disso o painel abre embaixo do mapa (UX-FIM-B11).
const NARROW_QUERY = '(max-width: 900px)';
const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';
const EXPORT_BUTTON_ID = 'db-export-button';
// Recorte por tabela: 0 é o banco inteiro; 1 a 3, os vizinhos até tantos saltos.
const HOP_CHOICES = [1, 2, 3];
// Zoom do mapa: de metade a uma vez e meia, de quarto em quarto.
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 1.5;
const ZOOM_STEP = 0.25;

export enum DbTab {
  Map = 0,
  Query = 1,
}

const DB_TAB_LABEL: Record<DbTab, string> = {
  [DbTab.Map]: 'Mapa do banco',
  [DbTab.Query]: 'Analisar consulta',
};

// A linha que explica cada aba (B05).
const DB_TAB_HINT: Record<DbTab, string> = {
  [DbTab.Map]: 'Tabelas, chaves e índices lidos do código',
  [DbTab.Query]: 'Cole um SQL e veja os gargalos, sem executar',
};

const DB_TAB_ID: Record<DbTab, string> = {
  [DbTab.Map]: 'db-tab-map',
  [DbTab.Query]: 'db-tab-query',
};

const DB_TABS = [DbTab.Map, DbTab.Query];

// Tela do banco (Fase 38, ADR-006): o esquema lido do código do repositório, com o mapa relacional,
// a lista com busca e o painel da tabela, e a aba de análise de consulta (Fase 40a). Nada aqui
// consulta um banco nem usa IA.
@Component({
  selector: 'app-database-page',
  imports: [RouterLink, DbPanelComponent, QueryAnalysisComponent, LiveConnectionDialogComponent],
  templateUrl: './database.page.html',
  styleUrl: './database.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'onEscape()' },
})
export class DatabasePage {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  // Vem da rota (repositorios/:nome/banco).
  readonly nome = input.required<string>();
  // ?tabela=<nome>: a tabela já escolhida ao abrir (o "No banco" do painel do passo leva para cá).
  readonly tabela = input<string | undefined>();
  // ?aba=consulta abre a análise de consulta; ?consulta= e ?origem= vêm do painel do passo.
  readonly aba = input<string | undefined>();
  readonly consulta = input<string | undefined>();
  readonly origem = input<string | undefined>();
  // ?levantamento=<id>: de onde o usuário veio; o "←" volta a ele (DB_SURVEY_ORIGIN_PARAM).
  readonly levantamento = input<string | undefined>();
  protected readonly backLink = computed(() => {
    const id = this.levantamento();
    return id ? ['/surveys', id] : ['/'];
  });
  protected readonly backLabel = computed(() => (this.levantamento() ? 'Voltar ao levantamento de origem' : 'Voltar aos levantamentos'));
  // O seletor só aparece quando o repositório da URL está na lista: com um nome que não existe, ele mostraria outro.
  protected readonly canSwitch = computed(() => this.repositories().length > 1 && this.repositories().some((r) => r.name === this.nome()));

  protected readonly DbTab = DbTab;
  protected readonly KeyMark = KeyMark;
  protected readonly tabs = DB_TABS;
  protected readonly tabLabel = DB_TAB_LABEL;
  protected readonly tabHint = DB_TAB_HINT;
  protected readonly tabId = DB_TAB_ID;
  private readonly lastRepository = inject(LastRepositoryService);
  protected readonly tab = computed(() => (this.aba() === DB_TAB_QUERY_PARAM ? DbTab.Query : DbTab.Map));

  protected readonly schema = signal<DbSchema | null>(null);
  // Sem os usos (falha ou carga), o painel diz isso; o mapa não depende deles.
  protected readonly usage = signal<DbUsageReport | null>(null);
  protected readonly usageError = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly repositories = signal<RepositoryItem[]>([]);
  protected readonly query = signal('');
  // O que de fato filtra: segue o texto digitado com atraso (SEARCH_DEBOUNCE_MS).
  protected readonly filter = signal('');
  private searchTimer?: ReturnType<typeof setTimeout>;
  protected readonly selection = signal<DbPick | null>(null);
  protected readonly hops = signal(0);
  protected readonly zoom = signal(1);
  protected readonly zoomMin = ZOOM_MIN;
  protected readonly zoomMax = ZOOM_MAX;
  protected readonly zoomPercent = computed(() => `${Math.round(this.zoom() * 100)}%`);
  protected readonly exportOpen = signal(false);
  protected readonly pngBusy = signal(false);
  protected readonly message = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly copy = new CopyFeedback();
  private readonly reload = signal(0);
  protected readonly connection = signal<LiveConnectionView | null>(null);
  protected readonly connectionOpen = signal(false);
  // A leitura da conexão falhou (UX-FIM-B07): a barra não promete "nenhum banco é consultado" sem saber.
  protected readonly connectionError = signal(false);
  // Com conexão salva, o selo e a promessa mudam; sem ela, o banco nunca é consultado.
  protected readonly live = computed(() => (this.connection()?.configured ? this.connection() : null));
  // O dialeto da conexão salva ou, sem ela, o do código do repositório (Fase 42b).
  protected readonly liveDialect = computed(() => this.live()?.dialect ?? this.schema()?.dialect ?? SqlDialect.SqlServer);
  // O repositório cujo esquema está na tela: recarregar o mesmo (conexão salva ou removida) mantém a tela como está.
  private loadedName: string | null = null;
  private messageTimer?: ReturnType<typeof setTimeout>;

  protected readonly DbItemKind = DbItemKind;
  protected readonly hopChoices = HOP_CHOICES;
  protected readonly card = DB_CARD;
  protected readonly maxRows = DB_CARD_MAX_ROWS;
  protected readonly dialectLabel = SQL_DIALECT_LABEL;
  protected readonly badgeLabel = SCHEMA_FINDING_BADGE;

  protected readonly relations = computed(() => {
    const schema = this.schema();
    return schema ? relationsOf(schema) : [];
  });

  // As fontes que de fato aparecem no esquema, para o selo da barra.
  protected readonly sourcesText = computed(() => {
    const schema = this.schema();
    if (!schema) return '';
    const kinds = new Set<SchemaSourceKind>();
    for (const table of schema.tables) for (const s of table.sources) kinds.add(s.kind);
    for (const item of [...schema.views, ...schema.procedures]) kinds.add(item.source.kind);
    return [...kinds].sort().map((k) => SCHEMA_SOURCE_LABEL[k]).join(', ');
  });

  protected readonly list = computed(() => {
    const schema = this.schema();
    const q = this.filter();
    if (!schema) return null;
    return {
      tables: schema.tables.filter((t) => matches(q, t, t.columns.map((c) => c.name))).map((t) => tableKey(t)),
      views: schema.views.filter((v) => matches(q, v, v.columns)).map((v) => tableKey(v)),
      procedures: schema.procedures.filter((p) => matches(q, p)).map((p) => tableKey(p)),
    };
  });

  protected readonly selectedTable = computed(() => {
    const pick = this.selection();
    return pick?.kind === DbItemKind.Table ? pick.key : null;
  });

  // Recorte ligado só com uma tabela escolhida; sem ela, o banco inteiro.
  protected readonly scope = computed(() => {
    const key = this.selectedTable();
    const hops = this.hops();
    return key && hops > 0 ? neighborhood(this.relations(), key, hops) : null;
  });

  protected readonly geometry = computed(() => {
    const schema = this.schema();
    if (!schema) return null;
    const scope = this.scope();
    const tables = scope ? schema.tables.filter((t) => scope.has(tableKey(t))) : schema.tables;
    return dbMapGeometry(tables, this.relations());
  });

  protected readonly hiddenCount = computed(() => {
    const schema = this.schema();
    const shown = this.geometry()?.cards.length ?? 0;
    return schema ? schema.tables.length - shown : 0;
  });

  // Tabelas acesas: a escolhida, ou as que a view ou a procedure escolhida lê e grava.
  protected readonly lit = computed<ReadonlySet<string> | null>(() => {
    const pick = this.selection();
    const schema = this.schema();
    if (!pick || !schema) return null;
    if (pick.kind === DbItemKind.Table) return new Set([pick.key]);
    const item = pick.kind === DbItemKind.View ? schema.views.find((v) => tableKey(v) === pick.key) : schema.procedures.find((p) => tableKey(p) === pick.key);
    const names = item ? ('writes' in item ? [...item.reads, ...item.writes] : item.reads) : [];
    return new Set(names.flatMap((n) => {
      const table = findTable(schema, n);
      return table ? [tableKey(table)] : [];
    }));
  });

  // Busca escurece no mapa o que não casa (a lista mostra só o que casa).
  protected readonly matching = computed<ReadonlySet<string> | null>(() => (this.filter().trim() ? new Set(this.list()?.tables ?? []) : null));

  // Selo do bloco: o achado do esquema pelo nome ("FK sem índice", "+1" com os outros) ou a contagem dos achados dos fluxos.
  protected readonly badges = computed(() => {
    const schema = this.schema();
    const map = new Map<string, string>();
    if (!schema) return map;
    for (const table of schema.tables) {
      const key = tableKey(table);
      const own = schema.findings.filter((f) => findTable(schema, f.table) === table);
      const flows = (this.usage()?.findings ?? []).filter((f) => sameName(f.target, key));
      const total = own.length + flows.length;
      if (total === 0) continue;
      map.set(key, own.length > 0 ? this.badgeLabel[own[0].kind] + (total > 1 ? ` +${total - 1}` : '') : total === 1 ? '1 achado' : `${total} achados`);
    }
    return map;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));

    // Abaixo de 900px o painel abre embaixo do mapa: ao escolher, ele rola para a vista (UX-FIM-B11).
    effect(() => {
      const pick = this.selection();
      if (!pick) return;
      untracked(() => {
        if (!window.matchMedia(NARROW_QUERY).matches) return;
        const behavior: ScrollBehavior = window.matchMedia(REDUCED_MOTION_QUERY).matches ? 'auto' : 'smooth';
        setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('.db-panel')?.scrollIntoView({ block: 'nearest', behavior }));
      });
    });

    effect((onCleanup) => {
      const name = this.nome();
      this.reload();
      const refresh = untracked(() => this.loadedName === name && !!this.schema());
      untracked(() => {
        this.error.set(null);
        this.usageError.set(false);
        // Mesmo repositório: o esquema, a seleção e a consulta digitada ficam até o novo esquema chegar.
        if (refresh) return;
        this.loadedName = name;
        this.schema.set(null);
        this.selection.set(null);
        this.hops.set(0);
        this.usage.set(null);
      });
      const sub = this.api.schema(name).subscribe({
        next: (schema) =>
          untracked(() => {
            this.schema.set(schema);
            // O repositório existe e tem esquema: é o último usado, para o "Banco" do cabeçalho.
            this.lastRepository.remember(name);
            const wanted = this.tabela();
            const table = wanted && !refresh ? findTable(schema, wanted) : undefined;
            if (table) this.choose({ kind: DbItemKind.Table, key: tableKey(table) });
          }),
        error: (error) => this.error.set(apiErrorMessage(error)),
      });
      const usage = this.api.schemaUsage(name).subscribe({
        next: (report) => this.usage.set(report),
        error: () => this.usageError.set(true),
      });
      onCleanup(() => {
        sub.unsubscribe();
        usage.unsubscribe();
      });
    });

    // A conexão do banco vivo acompanha o esquema (mesmo recarregamento). Sem ela, a tela segue lendo só o código.
    effect((onCleanup) => {
      const name = this.nome();
      this.reload();
      const sub = this.api.connection(name).subscribe({
        next: (connection) => {
          this.connectionError.set(false);
          this.connection.set(connection);
        },
        error: () => {
          this.connection.set(null);
          this.connectionError.set(true);
        },
      });
      onCleanup(() => sub.unsubscribe());
    });

    this.api.repositories().subscribe({
      next: (repos) => this.repositories.set(repos),
      // Sem a lista, a troca de repositório some; a tela do banco continua.
      error: () => this.repositories.set([]),
    });

    // A tabela escolhida vem para a vista, rolando só o contêiner do mapa.
    effect(() => {
      const key = this.selectedTable();
      const geometry = this.geometry();
      const scroller = this.scroller()?.nativeElement;
      const z = this.zoom();
      if (!key || !geometry || !scroller) return;
      untracked(() => {
        const found = geometry.cards.find((c) => c.key === key);
        if (!found) return;
        // Posições do desenho na escala do zoom.
        const card = { x: found.x * z, y: found.y * z, height: found.height * z };
        const width = DB_CARD.width * z;
        const top = card.y - REVEAL_MARGIN;
        const left = card.x - REVEAL_MARGIN;
        if (top < scroller.scrollTop) scroller.scrollTop = top;
        else if (card.y + card.height + REVEAL_MARGIN > scroller.scrollTop + scroller.clientHeight) scroller.scrollTop = Math.max(top, card.y + card.height + REVEAL_MARGIN - scroller.clientHeight);
        if (left < scroller.scrollLeft) scroller.scrollLeft = left;
        else if (card.x + width + REVEAL_MARGIN > scroller.scrollLeft + scroller.clientWidth) scroller.scrollLeft = card.x + width + REVEAL_MARGIN - scroller.clientWidth;
      });
    });
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected switchRepository(name: string): void {
    if (name && name !== this.nome()) this.router.navigate(['/repositorios', name, 'banco']);
  }

  protected selectTab(tab: DbTab, focus = false): void {
    if (tab !== this.tab()) {
      this.router.navigate([], { queryParams: { aba: tab === DbTab.Query ? DB_TAB_QUERY_PARAM : null }, queryParamsHandling: 'merge' });
    }
    if (focus) setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${DB_TAB_ID[tab]}`)?.focus({ preventScroll: true }));
  }

  // Setas trocam de aba, como no padrão de abas.
  protected onTabKey(event: Event, current: DbTab, step: number): void {
    event.preventDefault();
    this.selectTab(DB_TABS[(DB_TABS.indexOf(current) + step + DB_TABS.length) % DB_TABS.length], true);
  }

  // Da análise para o mapa, com a tabela escolhida.
  protected showInMap(name: string): void {
    const schema = this.schema();
    const table = schema ? findTable(schema, name) : undefined;
    this.router.navigate([], { queryParams: { aba: null, tabela: table ? tableKey(table) : null }, queryParamsHandling: 'merge' });
    if (table) this.choose({ kind: DbItemKind.Table, key: tableKey(table) }, true);
  }

  protected onSearch(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.filter.set(value), SEARCH_DEBOUNCE_MS);
  }

  protected itemId(kind: DbItemKind, key: string): string {
    return `${kind}:${key}`;
  }

  protected choose(pick: DbPick, focus = false): void {
    this.selection.set(pick);
    if (pick.kind !== DbItemKind.Table) this.hops.set(0);
    if (focus && pick.kind === DbItemKind.Table) this.focusCard(pick.key);
  }

  protected pickTable(key: string): void {
    const current = this.selection();
    this.selection.set(current?.kind === DbItemKind.Table && current.key === key ? null : { kind: DbItemKind.Table, key });
  }

  protected zoomBy(step: number): void {
    this.zoom.update((z) => Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, z + step * ZOOM_STEP)));
  }

  protected setHops(value: string): void {
    this.hops.set(Number(value) || 0);
  }

  protected closePanel(): void {
    const key = this.selectedTable();
    const pick = this.selection();
    this.selection.set(null);
    this.hops.set(0);
    if (key) this.focusCard(key);
    // View ou procedure: o foco volta ao item da lista que abriu o painel (UX-FIM-B09).
    else if (pick) this.focusItem(pick);
  }

  // Salvou ou removeu a conexão: o esquema (a fonte "banco vivo" entra ou sai) e a conexão são lidos de novo.
  protected connectionChanged(): void {
    this.reload.update((n) => n + 1);
  }

  protected connectionRemoved(): void {
    this.show({ ok: true, text: 'Conexão removida. O Rastro volta a ler só o código.' });
  }

  protected onEscape(): void {
    if (this.connectionOpen()) this.connectionOpen.set(false);
    else if (this.exportOpen()) this.closeExport();
    // Na aba de consulta o Esc não fecha o painel do mapa, que nem está à vista (UX-FIM-B12).
    else if (this.selection() && this.tab() === DbTab.Map) this.closePanel();
  }

  // Fecha o menu e devolve o foco ao botão "Exportar" (UX-FIM-B12).
  protected closeExport(): void {
    this.exportOpen.set(false);
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${EXPORT_BUTTON_ID}`)?.focus({ preventScroll: true }));
  }

  private focusItem(pick: DbPick): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-item="${CSS.escape(this.itemId(pick.kind, pick.key))}"]`)?.focus({ preventScroll: true }));
  }

  private focusCard(key: string): void {
    // Sem rolar: a rolagem é a do contêiner do mapa (efeito acima).
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-table="${CSS.escape(key)}"]`)?.focus({ preventScroll: true }));
  }

  protected cardLabel(key: string, columns: number, badge: string | undefined): string {
    return `Tabela ${key}, ${columns === 1 ? '1 campo' : columns + ' campos'}${badge ? ', achado: ' + badge : ''}`;
  }

  // Exportar o que está no mapa (o recorte, se houver) --------------------------------------

  private shownKeys(): Set<string> {
    return new Set(this.geometry()?.cards.map((c) => c.key) ?? []);
  }

  protected copyMermaid(): void {
    const schema = this.schema();
    this.closeExport();
    if (!schema) return;
    this.copy.copy('```mermaid\n' + toErMermaid(schema, this.shownKeys()) + '```\n');
  }

  protected downloadMermaid(): void {
    const schema = this.schema();
    this.closeExport();
    if (!schema) return;
    downloadBlob(new Blob([toErMermaid(schema, this.shownKeys())], { type: 'text/plain' }), `${this.fileName()}.mmd`);
    this.show({ ok: true, text: `Download iniciado: ${this.fileName()}.mmd` });
  }

  protected exportImage(format: 'png' | 'svg'): void {
    const geometry = this.geometry();
    this.closeExport();
    if (!geometry) return;
    const svg = dbMapSvg(geometry, readDiagramColors(), this.badges());
    const name = this.fileName();
    if (format === 'svg') {
      downloadBlob(new Blob([svg], { type: 'image/svg+xml' }), `${name}.svg`);
      this.show({ ok: true, text: `Download iniciado: ${name}.svg` });
      return;
    }
    this.pngBusy.set(true);
    svgToPng(svg, geometry.width, geometry.height).then(
      (blob) => {
        this.pngBusy.set(false);
        downloadBlob(blob, `${name}.png`);
        this.show({ ok: true, text: `Download iniciado: ${name}.png` });
      },
      (error: Error) => {
        this.pngBusy.set(false);
        this.show({ ok: false, text: `Não exportou a imagem: ${error.message} Tente o SVG.` });
      },
    );
  }

  private fileName(): string {
    return `rastro-banco-${fileSlug(this.nome())}`;
  }

  // Só a mensagem de sucesso some sozinha; o erro fica até fechar.
  private show(message: { ok: boolean; text: string }): void {
    this.message.set(message);
    clearTimeout(this.messageTimer);
    if (message.ok) this.messageTimer = setTimeout(() => this.message.set(null), MESSAGE_MS);
  }
}
