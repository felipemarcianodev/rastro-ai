import { BusinessFlow, BusinessKind, BusinessStep, LabelOrigin } from '../models';

// Visão de negócio (Fase 57): rótulos, glifos e o caminho da entrada até um passo-chave. Puro e
// determinístico.

export const BUSINESS_KIND_SLUG: Record<BusinessKind, string> = {
  [BusinessKind.Start]: 'start',
  [BusinessKind.Decision]: 'decision',
  [BusinessKind.Notification]: 'notification',
  [BusinessKind.Rejection]: 'rejection',
  [BusinessKind.CrossSystem]: 'cross-system',
  [BusinessKind.Step]: 'step',
  [BusinessKind.End]: 'end',
};

export const BUSINESS_KIND_LABEL_KEY: Record<BusinessKind, string> = {
  [BusinessKind.Start]: 'business.kind.start',
  [BusinessKind.Decision]: 'business.kind.decision',
  [BusinessKind.Notification]: 'business.kind.notification',
  [BusinessKind.Rejection]: 'business.kind.rejection',
  [BusinessKind.CrossSystem]: 'business.kind.cross-system',
  [BusinessKind.Step]: 'business.kind.step',
  [BusinessKind.End]: 'business.kind.end',
};

// Letra ou símbolo, nunca só cor (design-system.md).
export const BUSINESS_KIND_GLYPH: Record<BusinessKind, string> = {
  [BusinessKind.Start]: '▶',
  [BusinessKind.Decision]: '?',
  [BusinessKind.Notification]: '✉',
  [BusinessKind.Rejection]: '✕',
  [BusinessKind.CrossSystem]: '↗',
  [BusinessKind.Step]: '•',
  [BusinessKind.End]: '■',
};

export const LABEL_ORIGIN_LABEL_KEY: Record<LabelOrigin, string> = {
  [LabelOrigin.Rule]: 'business.origin.rule',
  [LabelOrigin.Ai]: 'business.origin.ai',
  [LabelOrigin.Agent]: 'business.origin.agent',
  [LabelOrigin.User]: 'business.origin.user',
};

export interface BusinessPathItem {
  step: BusinessStep;
  // Rótulo do ramo da ligação que chega a este passo; nulo na entrada ou sem rótulo.
  via: string | null;
}

// Passo de entrada: o primeiro de tipo Start; sem ele, o primeiro sem ligação de chegada; sem ele, o primeiro.
export function entryStep(flow: BusinessFlow): BusinessStep | null {
  const start = flow.steps.find((s) => s.kind === BusinessKind.Start);
  if (start) return start;
  const targets = new Set(flow.edges.map((e) => e.to));
  return flow.steps.find((s) => !targets.has(s.id)) ?? flow.steps[0] ?? null;
}

// Caminho da entrada até o passo, com o rótulo do ramo de cada ligação: o primeiro caminho em largura
// (o mais curto; no empate, o da primeira ligação na lista). Passo desconhecido ou inalcançável dá [].
export function pathTo(flow: BusinessFlow, stepId: string): BusinessPathItem[] {
  const byId = new Map(flow.steps.map((s) => [s.id, s]));
  const entry = entryStep(flow);
  if (!entry || !byId.has(stepId)) return [];

  const parent = new Map<string, { from: string; via: string | null }>();
  const seen = new Set<string>([entry.id]);
  const queue: string[] = [entry.id];
  for (let head = 0; head < queue.length && !seen.has(stepId); head++) {
    const current = queue[head];
    for (const edge of flow.edges) {
      if (edge.from !== current || seen.has(edge.to) || !byId.has(edge.to)) continue;
      seen.add(edge.to);
      parent.set(edge.to, { from: current, via: edge.label || null });
      queue.push(edge.to);
    }
  }
  if (!seen.has(stepId)) return [];

  const path: BusinessPathItem[] = [];
  let at: string | undefined = stepId;
  let via: string | null = parent.get(stepId)?.via ?? null;
  while (at !== undefined) {
    path.unshift({ step: byId.get(at)!, via });
    const up = parent.get(at);
    at = up?.from;
    via = at !== undefined ? (parent.get(at)?.via ?? null) : null;
  }
  return path;
}
