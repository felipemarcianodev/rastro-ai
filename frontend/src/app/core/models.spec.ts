import {
  EDGE_CONFIDENCE_LABEL,
  EDGE_CONFIDENCE_SLUG,
  EdgeConfidence,
  NODE_KIND_GLYPH,
  NODE_KIND_LABEL,
  NODE_KIND_SLUG,
  NodeKind,
  SURVEY_STATUS_LABEL,
  SURVEY_STATUS_SLUG,
  SurveyStatus,
  UNRESOLVED_REASON_HELP,
  UNRESOLVED_REASON_LABEL,
  UnresolvedReason,
} from './models';

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
      expect(NODE_KIND_LABEL[kind]).toBeTruthy();
      expect(NODE_KIND_GLYPH[kind]).toBeTruthy();
    }
    for (const c of values(EdgeConfidence) as EdgeConfidence[]) {
      expect(EDGE_CONFIDENCE_SLUG[c]).toBeTruthy();
      expect(EDGE_CONFIDENCE_LABEL[c]).toBeTruthy();
    }
    for (const r of values(UnresolvedReason) as UnresolvedReason[]) {
      expect(UNRESOLVED_REASON_LABEL[r]).toBeTruthy();
      expect(UNRESOLVED_REASON_HELP[r]).toBeTruthy();
    }
    for (const s of values(SurveyStatus) as SurveyStatus[]) {
      expect(SURVEY_STATUS_SLUG[s]).toBeTruthy();
      expect(SURVEY_STATUS_LABEL[s]).toBeTruthy();
    }
  });

  it('slugs não se repetem (viram classes CSS)', () => {
    const slugs = Object.values(NODE_KIND_SLUG);
    expect(new Set(slugs).size).toBe(slugs.length);
  });
});
