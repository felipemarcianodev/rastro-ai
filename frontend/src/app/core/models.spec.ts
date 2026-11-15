import {
  EDGE_CONFIDENCE_LABEL_KEY,
  EDGE_CONFIDENCE_SLUG,
  EdgeConfidence,
  NODE_KIND_GLYPH,
  NODE_KIND_LABEL_KEY,
  NODE_KIND_SLUG,
  NodeKind,
  SURVEY_STATUS_LABEL_KEY,
  SURVEY_STATUS_SLUG,
  SurveyStatus,
  UNRESOLVED_REASON_HELP_KEY,
  UNRESOLVED_REASON_LABEL_KEY,
  UnresolvedReason,
} from './models';
import { tr } from './i18n/locale';

// Texto no idioma ativo (o setup global usa pt-BR): a chave sem tradução volta igual a ela mesma.
function text(key: string): string {
  const value = tr(key);
  return value === key ? '' : value;
}

// Valores numéricos de um enum TS (sem as chaves reversas).
function values(enumObject: object): number[] {
  return Object.values(enumObject).filter((v): v is number => typeof v === 'number');
}

describe('models', () => {
  it('espelha os valores dos enums do backend (int no JSON)', () => {
    expect(values(NodeKind)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    expect(values(EdgeConfidence)).toEqual([0, 1, 2]);
    expect(values(UnresolvedReason)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    expect(values(SurveyStatus)).toEqual([0, 1, 2, 3]);
  });

  it('todo valor de enum tem slug, rótulo e explicação', () => {
    for (const kind of values(NodeKind) as NodeKind[]) {
      expect(NODE_KIND_SLUG[kind]).toBeTruthy();
      expect(text(NODE_KIND_LABEL_KEY[kind])).toBeTruthy();
      expect(NODE_KIND_GLYPH[kind]).toBeTruthy();
    }
    for (const c of values(EdgeConfidence) as EdgeConfidence[]) {
      expect(EDGE_CONFIDENCE_SLUG[c]).toBeTruthy();
      expect(text(EDGE_CONFIDENCE_LABEL_KEY[c])).toBeTruthy();
    }
    for (const r of values(UnresolvedReason) as UnresolvedReason[]) {
      expect(text(UNRESOLVED_REASON_LABEL_KEY[r])).toBeTruthy();
      expect(text(UNRESOLVED_REASON_HELP_KEY[r])).toBeTruthy();
    }
    for (const s of values(SurveyStatus) as SurveyStatus[]) {
      expect(SURVEY_STATUS_SLUG[s]).toBeTruthy();
      expect(text(SURVEY_STATUS_LABEL_KEY[s])).toBeTruthy();
    }
  });

  it('slugs não se repetem (viram classes CSS)', () => {
    const slugs = Object.values(NODE_KIND_SLUG);
    expect(new Set(slugs).size).toBe(slugs.length);
  });
});
