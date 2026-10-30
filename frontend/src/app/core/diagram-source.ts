import { Observable, catchError, map, of } from 'rxjs';
import { consolidatedMermaid, mergeDiagram } from './graph/consolidated';
import { SurveyDetail, SurveyDiagram } from './models';
import { RastroApi } from './rastro-api.service';

// O texto da visão consolidada de um levantamento pronto (Fases 27 e 28): a edição salva dele; a
// herdada de um levantamento anterior, juntada com o gerado do grafo atual; ou o gerado do fluxo.
export interface ResolvedDiagram {
  generated: string;
  text: string;
  // Edição salva neste levantamento (null quando é herdada ou não há).
  saved: SurveyDiagram | null;
  // Edição trazida do levantamento anterior: quantos passos editados saíram.
  inherited: { dropped: number } | null;
}

export function resolveDiagram(api: RastroApi, detail: SurveyDetail, saved: SurveyDiagram | null): Observable<ResolvedDiagram> {
  const generated = detail.graph ? consolidatedMermaid(detail.graph, detail.survey.repository) : '';
  if (!saved?.fromSurveyId) return of({ generated, text: saved?.mermaid ?? generated, saved, inherited: null });
  // Diagrama herdado: o levantamento anterior diz o que o dono de fato editou. Se ele não carregar,
  // o merge segue sem baseline.
  return api.survey(saved.fromSurveyId).pipe(
    catchError(() => of(null)),
    map((previous) => {
      const baseline = previous?.graph ? consolidatedMermaid(previous.graph, previous.survey.repository) : undefined;
      const merged = mergeDiagram(saved.mermaid, generated, baseline);
      return { generated, text: merged.text, saved: null, inherited: { dropped: merged.dropped } };
    }),
  );
}
