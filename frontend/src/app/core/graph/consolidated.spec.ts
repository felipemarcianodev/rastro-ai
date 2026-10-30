import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { consolidatedMermaid, kindClassOf, mergeDiagram, nodeIdFromSvg, setKindStyle, setNodeStyle, stepId } from './consolidated';

const node = (id: string, kind: NodeKind, label = id, extra: Partial<FlowNode> = {}): FlowNode => ({ id, kind, label, ...extra });
const edge = (from: string, to: string, order: number, label?: string): FlowEdge => ({ from, to, order, label, confidence: EdgeConfidence.Extracted });
const graph = (nodes: FlowNode[], edges: FlowEdge[]): FlowGraph => ({ schemaVersion: 1, entryPoint: 'OrderJob.Execute', entryNodeId: 'job', nodes, edges });

const id = stepId;
const line = (text: string, fragment: string) => text.split('\n').some((l) => l.trim() === fragment);

describe('visão consolidada (Fase 27)', () => {
  // CORE-DIA-001
  it('consolidatedMermaid_metodoDePassagem_someEOsFilhosEntramNoLugar', () => {
    const text = consolidatedMermaid(
      graph(
        [node('job', NodeKind.Entry, 'OrderJob.Execute'), node('get', NodeKind.Method, 'Repo.GetPendingAsync'), node('query', NodeKind.Method, 'Db.Load')],
        [edge('job', 'get', 0), edge('get', 'query', 0)],
      ),
    );

    expect(text.startsWith('flowchart TD\n')).toBeTrue();
    expect(text).not.toContain(id('get'));
    expect(line(text, `${id('query')}["Load"]`)).toBeTrue();
    expect(line(text, `${id('job')} --> ${id('query')}`)).toBeTrue();
  });

  // CORE-DIA-002
  it('consolidatedMermaid_metodoComRegra_viraUmNoSoEEscondeOsFilhos', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('get', NodeKind.Method, 'Repo.Get', { rule: { text: 'Busca os alertas pendentes', origin: 0 } }),
          node('query', NodeKind.Method, 'Db.Load'),
        ],
        [edge('job', 'get', 0), edge('get', 'query', 0)],
      ),
    );

    expect(line(text, `${id('get')}["Busca os alertas pendentes"]`)).toBeTrue();
    expect(text).not.toContain(id('query'));
  });

  // CORE-DIA-003
  it('consolidatedMermaid_decisaoComSenao_saiComRotuloEOsDoisGalhosSeEncontram', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('if', NodeKind.Decision, 'if (alert.Muted)'),
          node('a', NodeKind.Method, 'X.Skip'),
          node('b', NodeKind.Method, 'X.Send'),
          node('z', NodeKind.Method, 'X.Done'),
        ],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'a', 0, 'then'), edge('if', 'b', 1, 'else')],
      ),
    );

    expect(line(text, `${id('if')}{"Se alert.Muted"}`)).toBeTrue();
    expect(line(text, `${id('if')} -- "Se sim" --> ${id('a')}`)).toBeTrue();
    expect(line(text, `${id('if')} -- "Se não" --> ${id('b')}`)).toBeTrue();
    expect(line(text, `${id('a')} --> ${id('z')}`)).toBeTrue();
    expect(line(text, `${id('b')} --> ${id('z')}`)).toBeTrue();
    expect(text).not.toContain(`${id('if')} -- "Se não" --> ${id('z')}`);
  });

  // CORE-DIA-004
  it('consolidatedMermaid_decisaoSemSenao_aPropriaDecisaoSegueComSeNao', () => {
    const text = consolidatedMermaid(
      graph(
        [node('job', NodeKind.Entry), node('if', NodeKind.Decision, 'if (x)'), node('a', NodeKind.Method, 'X.A'), node('z', NodeKind.Method, 'X.Z')],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'a', 0, 'then')],
      ),
    );

    expect(line(text, `${id('if')} -- "Se sim" --> ${id('a')}`)).toBeTrue();
    expect(line(text, `${id('a')} --> ${id('z')}`)).toBeTrue();
    expect(line(text, `${id('if')} -- "Se não" --> ${id('z')}`)).toBeTrue();
  });

  // CORE-DIA-004b
  it('consolidatedMermaid_returnNoMetodoDePassagem_encerraSoOMetodoESegueParaOPassoDeQuemChamou', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('load', NodeKind.Method, 'Cache.Load'),
          node('if', NodeKind.Decision, 'if (hit)'),
          node('ret', NodeKind.Return, 'return cached'),
          node('read', NodeKind.Method, 'Db.Read'),
          node('z', NodeKind.Method, 'X.Send'),
        ],
        [edge('job', 'load', 0), edge('job', 'z', 1), edge('load', 'if', 0), edge('load', 'read', 1), edge('if', 'ret', 0, 'then')],
      ),
    );

    expect(line(text, `${id('if')} -- "Se sim" --> ${id('ret')}`)).toBeTrue();
    expect(line(text, `${id('ret')} --> ${id('z')}`)).toBeTrue();
    expect(line(text, `${id('read')} --> ${id('z')}`)).toBeTrue();
  });

  // CORE-DIA-005
  it('consolidatedMermaid_throwTermina_semLigarAoPassoSeguinte', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('if', NodeKind.Decision, 'if (x)'),
          node('boom', NodeKind.Throw, 'throw new InvalidOperationException'),
          node('z', NodeKind.Method, 'X.Z'),
        ],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'boom', 0, 'then')],
      ),
    );

    expect(line(text, `${id('boom')}>"Para com erro InvalidOperationException"]`)).toBeTrue();
    expect(text).not.toContain(`${id('boom')} -->`);
    expect(line(text, `class ${id('boom')} error`)).toBeTrue();
  });

  // CORE-DIA-006
  it('consolidatedMermaid_trySemNo_ECatchSaiDoMesmoPontoComORotuloDaSecao', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('begin', NodeKind.Method, 'X.Begin'),
          node('try', NodeKind.Try, 'try'),
          node('work', NodeKind.Method, 'X.Work'),
          node('fail', NodeKind.Method, 'X.MarkFailure'),
        ],
        [edge('job', 'begin', 0), edge('job', 'try', 1), edge('try', 'work', 0, 'try'), edge('try', 'fail', 1, 'catch (Exception)')],
      ),
    );

    expect(text).not.toContain(id('try'));
    expect(line(text, `${id('begin')} --> ${id('work')}`)).toBeTrue();
    expect(line(text, `${id('begin')} -- "catch (Exception)" --> ${id('fail')}`)).toBeTrue();
  });

  // CORE-DIA-007
  it('consolidatedMermaid_formasEClasses_porTipoDePasso', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry, 'OrderJob.Execute'),
          node('loop', NodeKind.Loop, 'foreach (var a in alerts)'),
          node('db', NodeKind.External, 'OrderRepository.Save', { symbol: 'App.OrderRepository.Save' }),
          node('web', NodeKind.External, 'Client.Post', { symbol: 'App.Client.Post' }),
          node('hole', NodeKind.Unresolved, 'notifier.Notify'),
        ],
        [edge('job', 'loop', 0), edge('loop', 'db', 0, 'each'), edge('loop', 'web', 1, 'each'), edge('job', 'hole', 1)],
      ),
    );

    expect(line(text, `${id('job')}(["Execute"])`)).toBeTrue();
    expect(line(text, `${id('loop')}{{"Para cada a em alerts"}}`)).toBeTrue();
    expect(line(text, `${id('db')}[("OrderRepository.Save")]`)).toBeTrue();
    expect(line(text, `${id('web')}[["Client.Post"]]`)).toBeTrue();
    expect(line(text, `${id('hole')}[/"notifier.Notify"/]`)).toBeTrue();
    expect(text).toContain('classDef decision ');
    expect(line(text, `class ${id('job')} entry`)).toBeTrue();
    expect(line(text, `class ${id('loop')} decision`)).toBeTrue();
  });

  // CORE-DIA-008
  it('consolidatedMermaid_ChamadaDoDotnet_foraDoDiagrama', () => {
    const text = consolidatedMermaid(
      graph(
        [node('job', NodeKind.Entry), node('linq', NodeKind.External, 'Enumerable.ToList', { symbol: 'System.Linq.Enumerable.ToList' }), node('a', NodeKind.Method, 'X.A')],
        [edge('job', 'linq', 0), edge('job', 'a', 1)],
      ),
    );

    expect(text).not.toContain(id('linq'));
    expect(line(text, `${id('job')} --> ${id('a')}`)).toBeTrue();
  });

  // CORE-DIA-009
  it('consolidatedMermaid_passoRepetido_ganhaIdComSufixoEOTextoComSeta', () => {
    const text = consolidatedMermaid(
      graph(
        [node('job', NodeKind.Entry), node('a', NodeKind.Method, 'X.Log'), node('b', NodeKind.Method, 'X.Log')],
        [edge('job', 'a', 0), edge('job', 'a', 1)],
      ),
    );

    expect(line(text, `${id('a')}_r1["↻ Log"]`)).toBeTrue();
  });

  // CORE-DIA-010
  it('consolidatedMermaid_mesmoGrafo_mesmoTextoEMesmoIdMesmoComOutraOrdemDeNos', () => {
    const nodes = [node('job', NodeKind.Entry), node('a', NodeKind.Method, 'X.A'), node('b', NodeKind.Method, 'X.B')];
    const edges = [edge('job', 'a', 0), edge('job', 'b', 1)];

    const first = consolidatedMermaid(graph(nodes, edges));
    const again = consolidatedMermaid(graph(nodes, edges));
    const shuffled = consolidatedMermaid(graph([...nodes].reverse(), edges));

    expect(again).toBe(first);
    expect(shuffled).toBe(first);
    expect(stepId('job')).toMatch(/^s[0-9a-z]+$/);
    expect(stepId('job')).not.toBe(stepId('a'));
  });

  // CORE-DIA-011
  it('consolidatedMermaid_rotuloComHtmlEAspas_saiEscapado', () => {
    const text = consolidatedMermaid(
      graph(
        [node('job', NodeKind.Entry), node('a', NodeKind.Method, 'X.A', { rule: { text: '<script>alert("x")</script>\nclick a call alert()', origin: 0 } })],
        [edge('job', 'a', 0)],
      ),
    );

    expect(text).not.toContain('<script>');
    expect(text).toContain('#lt;script#gt;alert(#quot;x#quot;)#lt;/script#gt; click a call alert()');
    // A quebra de linha do texto não vira um comando "click" em linha própria.
    expect(text.split('\n').some((l) => l.trimStart().startsWith('click'))).toBeFalse();
  });

  describe('mergeDiagram', () => {
    const base = () =>
      graph(
        [node('job', NodeKind.Entry), node('if', NodeKind.Decision, 'if (x)'), node('a', NodeKind.Method, 'X.A'), node('z', NodeKind.Method, 'X.Z')],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'a', 0, 'then')],
      );

    // CORE-DIA-012
    it('mergeDiagram_edicaoDoDono_mantemCorTextoEClasseEContaOsPerdidos', () => {
      const generated = consolidatedMermaid(base());
      let previous = setNodeStyle(generated, id('if'), { fill: '#ff0000' });
      previous = setKindStyle(previous, 'decision', { fill: '#123456' });
      previous = previous.replace('"Se x"', '"Minha decisão"');
      previous += `  style sgone fill:#00ff00\n  class sgone minha\n`;

      const merged = mergeDiagram(previous, generated);

      expect(line(merged.text, `style ${id('if')} fill:#ff0000`)).toBeTrue();
      expect(line(merged.text, `${id('if')}{"Minha decisão"}`)).toBeTrue();
      expect(merged.text).toContain('classDef decision fill:#123456');
      expect(merged.text).not.toContain('sgone');
      expect(merged.dropped).toBe(1);
      // O que não mudou continua igual ao gerado.
      expect(line(merged.text, `${id('a')}["A"]`)).toBeTrue();
    });

    // CORE-DIA-013
    it('mergeDiagram_semEdicao_devolveOGerado', () => {
      const generated = consolidatedMermaid(base());
      const merged = mergeDiagram(generated, generated);

      expect(merged.text).toBe(generated);
      expect(merged.dropped).toBe(0);
    });

    // CORE-DIA-013b
    it('mergeDiagram_comBaseline_soMantemOTextoQueODonoEditou', () => {
      const old = graph(
        [node('job', NodeKind.Entry), node('if', NodeKind.Decision, 'if (x)'), node('a', NodeKind.Method, 'X.OldA'), node('z', NodeKind.Method, 'X.Z')],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'a', 0, 'then')],
      );
      const baseline = consolidatedMermaid(old);
      const previous = baseline.replace('"Se x"', '"Minha decisão"');
      const generated = consolidatedMermaid(base());

      const merged = mergeDiagram(previous, generated, baseline);

      // Editado pelo dono: fica. Não editado e mudou no código: recebe o texto novo.
      expect(line(merged.text, `${id('if')}{"Minha decisão"}`)).toBeTrue();
      expect(line(merged.text, `${id('a')}["A"]`)).toBeTrue();
      expect(merged.text).not.toContain('OldA');
    });

    // CORE-DIA-013c
    it('mergeDiagram_semBaseline_mantemOTextoAnteriorDeTodoPasso', () => {
      const old = graph(
        [node('job', NodeKind.Entry), node('if', NodeKind.Decision, 'if (x)'), node('a', NodeKind.Method, 'X.OldA'), node('z', NodeKind.Method, 'X.Z')],
        [edge('job', 'if', 0), edge('job', 'z', 1), edge('if', 'a', 0, 'then')],
      );

      const merged = mergeDiagram(consolidatedMermaid(old), consolidatedMermaid(base()));

      expect(line(merged.text, `${id('a')}["A"]`)).toBeFalse();
      expect(merged.text.split('\n').find((l) => l.trim().startsWith(`${id('a')}[`))).toContain('Old');
    });
  });

  describe('edição por estilo', () => {
    const sample = 'flowchart TD\n  sa["A"]\n  classDef decision fill:#e8eef5,stroke:#2f5d8a,color:#1d2a2e,stroke-dasharray:3 3\n  class sa decision\n';

    // CORE-DIA-014
    it('setNodeStyle_duasVezes_mantemUmaLinhaSo', () => {
      const once = setNodeStyle(sample, 'sa', { fill: '#ff0000', stroke: '#000000', color: '#ffffff' });
      const twice = setNodeStyle(once, 'sa', { fill: '#00ffff', stroke: '#000000', color: '#ffffff' });

      expect(once).toContain('style sa fill:#ff0000,stroke:#000000,color:#ffffff');
      expect(twice.split('\n').filter((l) => l.trim().startsWith('style sa ')).length).toBe(1);
      expect(twice).toContain('style sa fill:#00ffff,stroke:#000000,color:#ffffff');
      expect(twice.endsWith('\n')).toBeTrue();
    });

    // CORE-DIA-015
    it('setKindStyle_substituiAClassDefEMantemOTracejado', () => {
      const text = setKindStyle(sample, 'decision', { fill: '#111111', stroke: '#222222', color: '#333333' });

      expect(text).toContain('classDef decision fill:#111111,stroke:#222222,color:#333333,stroke-dasharray:3 3');
      expect(text.split('\n').filter((l) => l.includes('classDef decision')).length).toBe(1);
    });

    // CORE-DIA-016
    it('setNodeStyle_corInvalida_naoEntraNoTexto', () => {
      const text = setNodeStyle(sample, 'sa', { fill: 'red;\nclick sa call x()' });

      expect(text).not.toContain('click');
      expect(text).toBe(sample);
    });

    // CORE-DIA-017
    it('kindClassOf_eNodeIdFromSvg_leemOIdDoTexto', () => {
      expect(kindClassOf(sample, 'sa')).toBe('decision');
      expect(kindClassOf(sample, 'sb')).toBeUndefined();
      expect(nodeIdFromSvg('flowchart-s1k3z9q-4')).toBe('s1k3z9q');
      expect(nodeIdFromSvg('rastro-diagram-2-flowchart-s1k3z9q_r1-12')).toBe('s1k3z9q_r1');
      expect(nodeIdFromSvg('outro-elemento')).toBeNull();
    });
  });
});

describe('visão consolidada entre sistemas (Fase 25)', () => {
  // UI-SYS-006
  it('consolidatedMermaid_doisSistemas_fazSubgraphPorSistemaEORotuloHttpNaSeta', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry, 'OrderJob.Execute'),
          node('estornar', NodeKind.Method, 'Pagamentos.EstornarAsync'),
          node('p::criar', NodeKind.Method, 'EstornosController.Criar', { system: 'pagamentos', rule: { text: 'Cria o estorno', origin: 0 } }),
        ],
        [edge('job', 'estornar', 0), { ...edge('estornar', 'p::criar', 0), http: { method: 'POST', route: 'api/estornos' } }],
      ),
      'loja',
    );

    expect(line(text, 'subgraph sys0["loja"]')).toBeTrue();
    expect(line(text, 'subgraph sys1["pagamentos"]')).toBeTrue();
    // O método de passagem some; a seta que chega ao primeiro passo do outro sistema leva o verbo e a rota.
    expect(text).toContain(`${id('job')} -- "POST api/estornos" --> ${id('p::criar')}`);
    expect(text.indexOf(id('p::criar') + '["Cria')).toBeGreaterThan(text.indexOf('subgraph sys1'));
  });

  // UI-SYS-006b
  it('consolidatedMermaid_entradaDeOutroSistemaSemRegra_ficaComoPassoEOsFilhosSeguemDela', () => {
    const text = consolidatedMermaid(
      graph(
        [
          node('job', NodeKind.Entry),
          node('estornar', NodeKind.Method, 'Pagamentos.EstornarAsync'),
          node('p::criar', NodeKind.Method, 'EstornosController.Criar', { system: 'pagamentos' }),
          node('p::if', NodeKind.Decision, 'if (cartao)', { system: 'pagamentos' }),
        ],
        [edge('job', 'estornar', 0), { ...edge('estornar', 'p::criar', 0), http: { method: 'POST', route: 'api/estornos' } }, edge('p::criar', 'p::if', 0)],
      ),
      'loja',
    );

    expect(line(text, `${id('p::criar')}["Criar"]`)).toBeTrue();
    expect(text).toContain(`${id('job')} -- "POST api/estornos" --> ${id('p::criar')}`);
    expect(line(text, `${id('p::criar')} --> ${id('p::if')}`)).toBeTrue();
  });

  // UI-SYS-007
  it('consolidatedMermaid_umSistemaSo_naoTemSubgraph', () => {
    const text = consolidatedMermaid(graph([node('job', NodeKind.Entry), node('a', NodeKind.Method, 'A.B')], [edge('job', 'a', 0)]));

    expect(text).not.toContain('subgraph');
  });
});
