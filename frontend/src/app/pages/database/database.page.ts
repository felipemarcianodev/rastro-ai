import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CopyFeedback } from '../../core/copy-feedback';
import { DB_CARD, DB_CARD_MAX_ROWS, dbMapGeometry, dbMapSvg } from '../../core/database/db-map';
import {
  DbSchema,
  DbUsageReport,
  sameName,
  SCHEMA_FINDING_BADGE,
  SCHEMA_SOURCE_LABEL,
  SQL_DIALECT_LABEL,
  SchemaSourceKind,
  findTable,
  matches,
  neighborhood,
  relationsOf,
  tableKey,
  toErMermaid,
} from '../../core/database/db-schema';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { readDiagramColors } from '../../core/mermaid-render';
import { RepositoryItem } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { DbItemKind, DbPick, DbPanelComponent } from './db-panel.component';

const MESSAGE_MS = 4000;
const REVEAL_MARGIN = 16;
// Recorte por tabela: 0 é o banco inteiro; 1 a 3, os vizinhos até tantos saltos.
const HOP_CHOICES = [1, 2, 3];
// Zoom do mapa: de metade a uma vez e meia, de quarto em quarto.
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 1.5;
const ZOOM_STEP = 0.25;

// Tela do banco (Fase 38, ADR-006): o esquema lido do código do repositório, com o mapa relacional,
// a lista com busca e o painel da tabela. Nada aqui consulta um banco nem usa IA. A aba "Analisar
// consulta" entra na Fase 40, com a análise; até lá, a tela não mostra aba morta.
@Component({
  selector: 'app-database-page',
  imports: [RouterLink, DbPanelComponent],
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

  protected readonly schema = signal<DbSchema | null>(null);
  // Sem os usos (falha ou carga), o painel diz isso; o mapa não depende deles.
  protected readonly usage = signal<DbUsageReport | null>(null);
  protected readonly usageError = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly repositories = signal<RepositoryItem[]>([]);
  protected readonly query = signal('');
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
    const q = this.query();
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
  protected readonly matching = computed<ReadonlySet<string> | null>(() => (this.query().trim() ? new Set(this.list()?.tables ?? []) : null));

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
    effect((onCleanup) => {
      const name = this.nome();
      this.reload();
      untracked(() => {
        this.schema.set(null);
        this.error.set(null);
        this.selection.set(null);
        this.hops.set(0);
        this.usage.set(null);
        this.usageError.set(false);
      });
      const sub = this.api.schema(name).subscribe({
        next: (schema) =>
          untracked(() => {
            this.schema.set(schema);
            const wanted = this.tabela();
            const table = wanted ? findTable(schema, wanted) : undefined;
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
    this.selection.set(null);
    this.hops.set(0);
    if (key) this.focusCard(key);
  }

  protected onEscape(): void {
    if (this.exportOpen()) this.exportOpen.set(false);
    else if (this.selection()) this.closePanel();
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
    this.exportOpen.set(false);
    if (!schema) return;
    this.copy.copy('```mermaid\n' + toErMermaid(schema, this.shownKeys()) + '```\n');
  }

  protected downloadMermaid(): void {
    const schema = this.schema();
    this.exportOpen.set(false);
    if (!schema) return;
    downloadBlob(new Blob([toErMermaid(schema, this.shownKeys())], { type: 'text/plain' }), `${this.fileName()}.mmd`);
    this.show({ ok: true, text: `Download iniciado: ${this.fileName()}.mmd` });
  }

  protected exportImage(format: 'png' | 'svg'): void {
    const geometry = this.geometry();
    this.exportOpen.set(false);
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
