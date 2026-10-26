import { NodeKind } from '../models';
import { summarySentence } from './summary';
import { OutlineItem } from './timeline-layout';

const item = (id: string, number: string, children: OutlineItem[] = [], head?: string): OutlineItem => ({ id, of: id, number, depth: 0, children, ...(head ? { head } : {}) });

const TEXTS: Record<string, [string, NodeKind]> = {
  begin: ['Begin', NodeKind.Method],
  try: ['try', NodeKind.Try],
  get: ['Get pending alerts', NodeKind.Method],
  loop: ['Para cada alert em pendingAlerts', NodeKind.Loop],
  failure: ['Mark failure', NodeKind.Method],
  rethrow: ['Repassa o erro', NodeKind.Throw],
};

describe('resumo do fluxo (Fase 23)', () => {
  it('frase com a linha principal, abrindo o que se tenta; o que um método chama fica de fora', () => {
    const outline = [
      item('begin', '1', [item('inner', '1.1')]),
      item('try', '2', [item('get', '2.1'), item('loop', '2.2'), item('failure', '2.3', [], 'catch (Exception)'), item('rethrow', '2.4')]),
    ];

    const sentence = summarySentence(outline, (i) => ({ text: TEXTS[i.of][0], kind: TEXTS[i.of][1] }));

    expect(sentence).toBe('Begin; try: get pending alerts, para cada alert em pendingAlerts; catch (Exception): mark failure, repassa o erro.');
  });

  it('fluxo vazio, frase vazia', () => {
    expect(summarySentence([], () => ({ text: '', kind: NodeKind.Method }))).toBe('');
  });
});
