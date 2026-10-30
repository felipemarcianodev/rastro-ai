import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { resolveDiagram } from '../../core/diagram-source';
import { consolidatedMermaid, mergeDiagram, nodeOfStep, stepId } from '../../core/graph/consolidated';
import { LAYOUT_DIRECTION_GLYPH, LAYOUT_DIRECTION_LABEL, LayoutDirection, NEXT_LAYOUT_DIRECTION } from '../../core/graph/layout';
import { DIAGRAM_RENDERER, readDiagramColors } from '../../core/mermaid-render';
import { FlowGraph, SurveyDetail, SurveyDiagram } from '../../core/models';
import { RastroApi } from '../../core/rastro-api.service';
import { DiagramStageComponent, StagePick } from '../diagram/diagram-stage.component';

// O quarto desenho do fluxo (Fase 28): a visão consolidada da Fase 27, com a edição salva do dono,
// no lugar do canvas. Clicar num passo escolhe o nó do grafo e abre o painel do passo.
@Component({
  selector: 'app-consolidated-view',
  imports: [RouterLink, DiagramStageComponent],
  templateUrl: './consolidated-view.component.html',
  styleUrl: './consolidated-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConsolidatedViewComponent {
  private readonly api = inject(RastroApi);
  private readonly renderer = inject(DIAGRAM_RENDERER);
  private readonly stage = viewChild(DiagramStageComponent);

  readonly detail = input.required<SurveyDetail>();
  readonly selectedId = input<string | null>(null);
  // Recorte "ver somente este fluxo": o desenho é o do recorte, com a edição do dono nos passos que ficam.
  readonly focus = input<FlowGraph | null>(null);
  readonly selectNode = output<string | null>();
  readonly directionChange = output<LayoutDirection>();

  protected readonly svg = signal('');
  protected readonly renderError = signal(false);
  protected readonly edited = signal(false);
  // O diagrama salvo (undefined: ainda carregando; null: não há).
  private readonly saved = signal<SurveyDiagram | null | undefined>(undefined);
  protected readonly loading = computed(() => this.saved() === undefined || (!this.svg() && !this.renderError()));
  protected readonly selectedStep = computed(() => {
    const id = this.selectedId();
    return id ? stepId(id) : null;
  });
  // O passo escolhido no grafo não tem passo no desenho (método de passagem, chamada do .NET).
  protected readonly selectedHidden = signal(false);

  protected readonly LayoutDirection = LayoutDirection;
  protected readonly next = NEXT_LAYOUT_DIRECTION[LayoutDirection.Consolidated];
  protected readonly directionLabel = LAYOUT_DIRECTION_LABEL;
  protected readonly directionGlyph = LAYOUT_DIRECTION_GLYPH;

  private renderSeq = 0;
  private resolving?: Subscription;

  constructor() {
    effect((onCleanup) => {
      const id = this.detail().survey.id;
      untracked(() => {
        this.saved.set(undefined);
        this.svg.set('');
      });
      const sub = this.api.diagram(id).subscribe({
        next: (saved) => this.saved.set(saved),
        // Sem a edição salva o desenho segue com o gerado do fluxo.
        error: () => this.saved.set(null),
      });
      onCleanup(() => sub.unsubscribe());
    });

    effect(() => {
      const detail = this.detail();
      const saved = this.saved();
      const focus = this.focus();
      if (saved === undefined || !detail.graph) return;
      untracked(() => {
        this.resolving?.unsubscribe();
        this.resolving = resolveDiagram(this.api, detail, saved).subscribe((resolved) => {
          this.edited.set(resolved.text !== resolved.generated);
          this.render(focus ? mergeDiagram(resolved.text, consolidatedMermaid(focus, detail.survey.repository)).text : resolved.text);
        });
      });
    });

    // Seleção de fora (painel, busca, setas): o passo acende e vem para a vista.
    effect(() => {
      const step = this.selectedStep();
      this.svg();
      const stage = this.stage();
      if (!step || !stage) {
        this.selectedHidden.set(false);
        return;
      }
      untracked(() => {
        const found = this.hasStep(step);
        this.selectedHidden.set(!found);
        if (found) stage.reveal(step);
      });
    });
  }

  private render(text: string): void {
    const seq = ++this.renderSeq;
    this.renderer(text, readDiagramColors()).then(
      (svg) => {
        if (seq !== this.renderSeq) return;
        this.renderError.set(false);
        this.svg.set(svg);
      },
      () => {
        if (seq !== this.renderSeq) return;
        // O texto salvo não desenha: a tela de edição mostra o erro do Mermaid e a linha.
        this.renderError.set(true);
      },
    );
  }

  protected onPick(pick: StagePick | null): void {
    const graph = this.detail().graph;
    this.selectNode.emit(pick && graph ? nodeOfStep(graph, pick.id) : null);
  }

  protected toggleDirection(): void {
    this.directionChange.emit(this.next);
  }

  private hasStep(step: string): boolean {
    const svg = this.svg();
    return svg.includes(`flowchart-${step}-`) || svg.includes(`flowchart-${step}_r`);
  }
}
