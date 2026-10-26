import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import {
  FINDING_CATEGORY_LABEL,
  FINDING_CATEGORY_OF,
  FINDING_KIND_LABEL,
  FINDING_SEVERITY_LABEL,
  FINDING_SEVERITY_SLUG,
  Finding,
  FindingCategory,
  FlowGraph,
} from '../../core/models';

interface IndexedFinding {
  finding: Finding;
  // Posição na lista do grafo: é o que a API e o MCP usam para o prompt de refatoração.
  index: number;
  nodeLabel: string;
}

// DDD primeiro: é o que o dono pediu para "trazer à luz"; métricas por último.
const CATEGORY_ORDER = [FindingCategory.Ddd, FindingCategory.CleanCode, FindingCategory.Metric];

// Achados de qualidade do fluxo (Fase 10), agrupados por categoria. Com nodeId, só os do passo.
@Component({
  selector: 'app-findings-list',
  templateUrl: './findings-list.component.html',
  styleUrl: './findings-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FindingsListComponent {
  readonly graph = input.required<FlowGraph>();
  readonly nodeId = input<string | null>(null);
  readonly selectNode = output<string>();
  readonly prompt = output<number>();

  protected readonly categoryLabel = FINDING_CATEGORY_LABEL;
  protected readonly kindLabel = FINDING_KIND_LABEL;
  protected readonly severityLabel = FINDING_SEVERITY_LABEL;
  protected readonly severitySlug = FINDING_SEVERITY_SLUG;

  protected readonly groups = computed(() => {
    const graph = this.graph();
    const labels = new Map(graph.nodes.map((n) => [n.id, n.label]));
    const nodeId = this.nodeId();
    const items: IndexedFinding[] = (graph.findings ?? [])
      .map((finding, index) => ({ finding, index, nodeLabel: labels.get(finding.nodeId) ?? finding.nodeId }))
      .filter((item) => !nodeId || item.finding.nodeId === nodeId);
    return CATEGORY_ORDER.map((category) => ({
      category,
      items: items
        .filter((item) => FINDING_CATEGORY_OF[item.finding.kind] === category)
        .sort((a, b) => b.finding.severity - a.finding.severity),
    })).filter((group) => group.items.length > 0);
  });

  // Caminhos vêm do Windows ou do Linux: separa pelas duas barras.
  protected fileName(file: string): string {
    return file.split(/[\\/]/).pop() ?? file;
  }

  protected readonly total = computed(() => this.groups().reduce((sum, group) => sum + group.items.length, 0));
}
