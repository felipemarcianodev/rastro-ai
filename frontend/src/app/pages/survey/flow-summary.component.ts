import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FlowSummary } from '../../core/graph/summary';

// Resumo do fluxo na visão geral (Fase 23): a frase da linha principal e a lista numerada dos
// passos, com a mesma numeração do grafo. Clicar num item leva ao passo.
@Component({
  selector: 'app-flow-summary',
  imports: [NgTemplateOutlet],
  templateUrl: './flow-summary.component.html',
  styleUrl: './flow-summary.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlowSummaryComponent {
  readonly summary = input.required<FlowSummary>();
  readonly selectNode = output<string>();
  // A lista começa fechada além do primeiro nível: quem quer o detalhe abre.
  protected readonly showAll = signal(false);
}
