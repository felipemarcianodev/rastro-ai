// Testes do extrator Angular. Rodam o script de verdade (como a API faz) sobre a fixture loja-web
// e sobre projetos pequenos montados em pasta temporária, que cobrem o resto das construções.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { normalizarRota } from '../lib/rota.mjs';
import { acharGuardas, acharMetodosDeEventos } from '../lib/template.mjs';

const aqui = path.dirname(fileURLToPath(import.meta.url));
const extrator = path.join(aqui, '..', 'extract.mjs');
const fixture = path.join(aqui, 'fixtures', 'loja-web');
const stubs = path.join(fixture, 'src', 'stubs', 'angular-common-http.d.ts');
const stubMatDialog = path.join(fixture, 'src', 'stubs', 'angular-material-dialog.d.ts');
const stubCdkDialog = path.join(fixture, 'src', 'stubs', 'angular-cdk-dialog.d.ts');

const NodeKind = { Entry: 0, Method: 1, Decision: 2, Loop: 3, Try: 4, Return: 5, Throw: 6, Unresolved: 8, Jump: 9 };
const Reason = { MultipleImplementations: 0, NoImplementation: 1, DepthLimit: 6, RouteNotResolved: 7, NoMatchingEndpoint: 8 };

function rodar(raiz, entry, { depth = 10, project = 'tsconfig.json', env = {} } = {}) {
  const r = spawnSync(
    process.execPath,
    [extrator, '--root', raiz, '--project', project, '--entry', entry, '--depth', String(depth)],
    { encoding: 'utf8', env: { ...process.env, ...env } },
  );
  return { codigo: r.status, saida: r.stdout, erro: r.stderr, json: r.stdout.trim() ? JSON.parse(r.stdout) : null };
}

const temporarias = [];

// Cria um projeto Angular mínimo (com os stubs da fixture) e devolve a raiz.
function criarProjeto(arquivos, nome = 'app-teste') {
  const raiz = fs.mkdtempSync(path.join(os.tmpdir(), 'rastro-ts-'));
  temporarias.push(raiz);
  fs.mkdirSync(path.join(raiz, 'src'), { recursive: true });
  fs.writeFileSync(path.join(raiz, 'package.json'), JSON.stringify({ name: nome }));
  fs.writeFileSync(
    path.join(raiz, 'tsconfig.json'),
    JSON.stringify({
      compilerOptions: { target: 'ES2022', module: 'ES2022', moduleResolution: 'bundler', strict: true, experimentalDecorators: true, noEmit: true, types: [] },
      include: ['src/**/*.ts'],
    }),
  );
  fs.copyFileSync(stubs, path.join(raiz, 'src', 'stubs.d.ts'));
  fs.copyFileSync(stubMatDialog, path.join(raiz, 'src', 'stubs-mat-dialog.d.ts'));
  fs.copyFileSync(stubCdkDialog, path.join(raiz, 'src', 'stubs-cdk-dialog.d.ts'));
  for (const [arquivo, conteudo] of Object.entries(arquivos)) {
    const destino = path.join(raiz, arquivo);
    fs.mkdirSync(path.dirname(destino), { recursive: true });
    fs.writeFileSync(destino, conteudo);
  }
  return raiz;
}

after(() => {
  for (const pasta of temporarias) fs.rmSync(pasta, { recursive: true, force: true });
});

const no = (grafo, filtro) => grafo.nodes.find(filtro);
const rotulos = (grafo, tipo) => grafo.nodes.filter((n) => n.kind === tipo).map((n) => n.label);

describe('fixture loja-web: do clique em Cancelar até o HttpClient', () => {
  const r = rodar(fixture, 'DetalhePedidoComponent.cancelar');
  const g = r.json?.graph;

  test('sai com código 0 e só um JSON na saída padrão', () => {
    // EXT-TS-001
    assert.equal(r.codigo, 0);
    assert.equal(r.saida.trim().split('\n').length, 1);
    assert.deepEqual(Object.keys(r.json).sort(), ['graph', 'httpCalls']);
  });

  test('entrada: nó Entry com id, label, symbol, local e projeto', () => {
    // EXT-TS-002
    const entrada = no(g, (n) => n.kind === NodeKind.Entry);
    assert.equal(g.entryNodeId, 'm:src/app/pedidos/detalhe-pedido.component.ts#DetalhePedidoComponent.cancelar');
    assert.equal(entrada.id, g.entryNodeId);
    assert.equal(entrada.label, 'DetalhePedidoComponent.cancelar');
    assert.equal(entrada.symbol, 'DetalhePedidoComponent.cancelar');
    assert.deepEqual(entrada.location, { file: 'src/app/pedidos/detalhe-pedido.component.ts', line: 15 });
    assert.equal(entrada.project, 'loja-web');
    assert.equal(g.schemaVersion, 2);
  });

  test('template: o @if que envolve o botão vira uma decisão ligada à entrada', () => {
    // EXT-TS-003
    const guarda = no(g, (n) => n.id.startsWith('if:src/app/pedidos/detalhe-pedido.component.html:'));
    assert.equal(guarda.kind, NodeKind.Decision);
    assert.equal(guarda.label, 'if (!pedido.enviado)');
    assert.equal(guarda.condition, '!pedido.enviado');
    assert.equal(guarda.location.file, 'src/app/pedidos/detalhe-pedido.component.html');
    assert.equal(guarda.location.line, 3);
    assert.ok(g.edges.some((e) => e.from === g.entryNodeId && e.to === guarda.id && e.label === undefined));
  });

  test('os passos do método ficam no ramo then da guarda do template', () => {
    // EXT-TS-004
    const guarda = no(g, (n) => n.id.startsWith('if:src/app/pedidos/detalhe-pedido.component.html:'));
    const filhos = g.edges.filter((e) => e.from === guarda.id);
    assert.ok(filhos.length > 0);
    assert.ok(filhos.every((e) => e.label === 'then'));
    assert.deepEqual(filhos.map((e) => e.order), filhos.map((_, i) => i + 1));
  });

  test('decisão do método: if (!confirmar()) com return antecipado no ramo then', () => {
    // EXT-TS-005
    const decisao = no(g, (n) => n.id.startsWith('if:src/app/pedidos/detalhe-pedido.component.ts:'));
    assert.equal(decisao.label, 'if (!confirmar())');
    assert.equal(decisao.condition, '!confirmar()');
    const retorno = no(g, (n) => n.kind === NodeKind.Return);
    assert.equal(retorno.label, 'return');
    assert.ok(g.edges.some((e) => e.from === decisao.id && e.to === retorno.id && e.label === 'then'));
    assert.ok(g.nodes.some((n) => n.id === 'm:src/app/pedidos/confirmacao.ts#confirmar'));
  });

  test('sequência do subscribe: service, depois next e depois error, sob o mesmo pai', () => {
    // EXT-TS-006
    const guarda = no(g, (n) => n.id.startsWith('if:src/app/pedidos/detalhe-pedido.component.html:'));
    const destinos = g.edges.filter((e) => e.from === guarda.id).map((e) => e.to);
    const posicao = (sufixo) => destinos.findIndex((d) => d.endsWith(sufixo));
    const servico = posicao('#PedidosService.cancelar');
    const recarregar = posicao('#DetalhePedidoComponent.recarregar');
    const erro = posicao('#DetalhePedidoComponent.mostrarErro');
    assert.ok(servico >= 0 && recarregar > servico && erro > recarregar, destinos.join('\n'));
  });

  test('chamada HTTP: POST api/pedidos/{}/cancelamento, sem o endereço base', () => {
    // EXT-TS-007
    const h = no(g, (n) => n.id.startsWith('h:'));
    assert.equal(h.id, 'h:src/app/pedidos/pedidos.service.ts:10:12');
    assert.equal(h.kind, NodeKind.Unresolved);
    assert.equal(h.label, 'POST api/pedidos/{}/cancelamento');
    assert.equal(h.reason, Reason.NoMatchingEndpoint);
    assert.equal(h.detail, 'this.http.post');

    const aresta = g.edges.find((e) => e.to === h.id);
    assert.equal(aresta.from, 'm:src/app/pedidos/pedidos.service.ts#PedidosService.cancelar');
    assert.deepEqual(aresta.http, { method: 'POST', route: 'api/pedidos/{}/cancelamento' });
    assert.deepEqual(aresta.location, { file: 'src/app/pedidos/pedidos.service.ts', line: 10 });
    assert.equal(aresta.confidence, 0);
  });

  test('httpCalls: nó, chamada e profundidade do método que chama', () => {
    // EXT-TS-008
    assert.deepEqual(r.json.httpCalls, [
      {
        nodeId: 'h:src/app/pedidos/pedidos.service.ts:10:12',
        call: { method: 'POST', route: 'api/pedidos/{}/cancelamento' },
        depth: 1,
      },
    ]);
  });

  test('o JSON omite campos nulos e usa enums como int', () => {
    // EXT-TS-009
    const texto = r.saida;
    assert.ok(!texto.includes('null'));
    assert.ok(!texto.includes('"kind":"'));
    for (const n of g.nodes) assert.equal(typeof n.kind, 'number');
  });

  test('saída determinística: duas execuções dão o mesmo JSON', () => {
    // EXT-TS-010
    assert.equal(rodar(fixture, 'DetalhePedidoComponent.cancelar').saida, r.saida);
  });
});

describe('profundidade', () => {
  test('passando do limite, o método vira nó de limite de profundidade', () => {
    // EXT-TS-011
    const r = rodar(fixture, 'DetalhePedidoComponent.cancelar', { depth: 1 });
    assert.equal(r.codigo, 0);
    const limite = no(r.json.graph, (n) => n.id === 'u:depth:m:src/app/pedidos/pedidos.service.ts#PedidosService.cancelar');
    assert.equal(limite.kind, NodeKind.Unresolved);
    assert.equal(limite.reason, Reason.DepthLimit);
    assert.equal(limite.detail, 'MaxDepth = 1');
    assert.deepEqual(r.json.httpCalls, []);
  });

  test('profundidade 0 mostra só a entrada e o limite', () => {
    // EXT-TS-012
    const r = rodar(fixture, 'DetalhePedidoComponent.cancelar', { depth: 0 });
    assert.equal(r.json.graph.nodes.length, 2);
  });
});

describe('erros em pt-BR para o usuário', () => {
  test('ponto de entrada inexistente: código 1 e uma linha JSON com error', () => {
    // EXT-TS-020
    const r = rodar(fixture, 'DetalhePedidoComponent.naoExiste');
    assert.equal(r.codigo, 1);
    assert.equal(r.saida.trim().split('\n').length, 1);
    assert.match(r.json.error, /não encontrado/);
    assert.equal(r.json.graph, undefined);
  });

  test('sugere as classes que têm um método com o mesmo nome, até 5', () => {
    // EXT-TS-021
    const r = rodar(fixture, 'OutraClasse.cancelar');
    assert.equal(r.codigo, 1);
    assert.match(r.json.error, /PedidosService\.cancelar/);
    assert.match(r.json.error, /DetalhePedidoComponent\.cancelar/);
  });

  test('ponto de entrada sem o formato Classe.metodo', () => {
    // EXT-TS-022
    const r = rodar(fixture, 'cancelar');
    assert.equal(r.codigo, 1);
    assert.match(r.json.error, /Classe\.metodo/);
  });

  test('tsconfig inexistente', () => {
    // EXT-TS-023
    const r = rodar(fixture, 'DetalhePedidoComponent.cancelar', { project: 'nao-existe.json' });
    assert.equal(r.codigo, 1);
    assert.match(r.json.error, /nao-existe\.json não foi encontrado/);
  });

  test('classe com o mesmo nome em dois arquivos é ambígua', () => {
    // EXT-TS-024
    const raiz = criarProjeto({
      'src/a.ts': 'export class Tela { abrir(): void {} }',
      'src/b.ts': 'export class Tela { abrir(): void {} }',
    });
    const r = rodar(raiz, 'Tela.abrir');
    assert.equal(r.codigo, 1);
    assert.match(r.json.error, /ambíguo/);
  });

  test('argumento faltando', () => {
    // EXT-TS-025
    const r = spawnSync(process.execPath, [extrator, '--root', fixture], { encoding: 'utf8' });
    assert.equal(r.status, 1);
    assert.match(JSON.parse(r.stdout).error, /Falta o argumento --project/);
  });
});

describe('monorepo (issue #30)', () => {
  test('o TypeScript e o nome vêm da pasta do tsconfig, não da raiz do repositório', () => {
    // EXT-TS-026
    const raiz = criarProjeto({ 'frontend/src/tela.ts': 'export class Tela { abrir(): void {} }' });
    // Nada de package.json, tsconfig nem node_modules na raiz: o app Angular mora em frontend/.
    fs.rmSync(path.join(raiz, 'package.json'));
    fs.renameSync(path.join(raiz, 'tsconfig.json'), path.join(raiz, 'frontend', 'tsconfig.app.json'));
    fs.renameSync(path.join(raiz, 'src', 'stubs.d.ts'), path.join(raiz, 'frontend', 'src', 'stubs.d.ts'));
    fs.writeFileSync(path.join(raiz, 'frontend', 'package.json'), JSON.stringify({ name: 'app-do-monorepo' }));
    // TypeScript "do projeto" na subpasta: avisa que foi ele o carregado e repassa o compilador real.
    const pacote = path.join(raiz, 'frontend', 'node_modules', 'typescript');
    fs.mkdirSync(pacote, { recursive: true });
    fs.writeFileSync(path.join(pacote, 'package.json'), JSON.stringify({ name: 'typescript', main: 'index.js' }));
    const real = createRequire(import.meta.url).resolve('typescript');
    fs.writeFileSync(path.join(pacote, 'index.js'), `process.stderr.write('typescript-da-subpasta\\n'); module.exports = require(${JSON.stringify(real)});`);

    const r = rodar(raiz, 'Tela.abrir', { project: 'frontend/tsconfig.app.json', env: { RASTRO_TYPESCRIPT: '' } });

    assert.equal(r.codigo, 0, r.erro);
    assert.match(r.erro, /typescript-da-subpasta/);
    assert.equal(r.json.graph.entryPoint, 'Tela.abrir');
  });
});

describe('construções do método', () => {
  test('if/else, switch, laços, try, throw, break e continue', () => {
    // EXT-TS-030
    const raiz = criarProjeto({
      'src/tela.ts': `
export class Tela {
  executar(itens: number[], modo: string): number {
    if (itens.length === 0) {
      throw new Error('vazio');
    } else if (modo === 'x') {
      return 1;
    } else {
      this.marcar();
    }
    switch (modo) {
      case 'a':
      case 'b':
        this.marcar();
        break;
      case 'c':
        return 3;
      default:
        this.marcar();
    }
    for (const item of itens) {
      if (item > 5) continue;
      if (item > 9) break;
    }
    for (let i = 0; i < itens.length; i++) { this.marcar(); }
    while (itens.length > 100) { itens.pop(); }
    do { this.marcar(); } while (itens.length > 200);
    try {
      this.marcar();
    } catch (e) {
      throw e;
    } finally {
      this.marcar();
    }
    return 0;
  }
  marcar(): void {}
}`,
    });
    const g = rodar(raiz, 'Tela.executar').json.graph;

    assert.deepEqual(rotulos(g, NodeKind.Decision), [
      'if (itens.length === 0)',
      'if (modo === \'x\')',
      'switch (modo)',
      'if (item > 5)',
      'if (item > 9)',
    ]);
    assert.deepEqual(rotulos(g, NodeKind.Loop), [
      'foreach (item in itens)',
      'for (i < itens.length)',
      'while (itens.length > 100)',
      'do … while (itens.length > 200)',
    ]);
    assert.deepEqual(rotulos(g, NodeKind.Try), ['try']);
    assert.deepEqual(rotulos(g, NodeKind.Throw), ['throw new Error', 'throw e']);
    assert.deepEqual(rotulos(g, NodeKind.Return), ['return 1', 'return 3']);
    // break do switch é só sintaxe; break e continue do laço são desvios.
    assert.deepEqual(rotulos(g, NodeKind.Jump), ['continue', 'break']);

    const arestas = (de) => g.edges.filter((e) => e.from === de.id).map((e) => e.label);
    const decisaoIf = no(g, (n) => n.label === 'if (itens.length === 0)');
    assert.deepEqual(arestas(decisaoIf), ['then', 'else']);
    const sw = no(g, (n) => n.kind === NodeKind.Decision && n.label.startsWith('switch'));
    assert.deepEqual([...new Set(arestas(sw))], ['case \'a\' | case \'b\'', 'case \'c\'', 'default']);
    const tentar = no(g, (n) => n.kind === NodeKind.Try);
    assert.deepEqual([...new Set(arestas(tentar))], ['try', 'catch (e)', 'finally']);
    const laco = no(g, (n) => n.label === 'foreach (item in itens)');
    assert.deepEqual([...new Set(arestas(laco))], ['each']);
  });

  test('aresta repetida (mesmo from, to e label) entra uma vez só e a ordem começa em 1', () => {
    // EXT-TS-031
    const raiz = criarProjeto({
      'src/tela.ts': `
export class Tela {
  executar(): void { this.marcar(); this.marcar(); this.outro(); }
  marcar(): void {}
  outro(): void {}
}`,
    });
    const g = rodar(raiz, 'Tela.executar').json.graph;
    const saindo = g.edges.filter((e) => e.from === g.entryNodeId);
    assert.deepEqual(saindo.map((e) => [e.to.split('#')[1], e.order]), [['Tela.marcar', 1], ['Tela.outro', 2]]);
  });

  test('função de módulo e arrow function com nome são percorridas; biblioteca é ignorada', () => {
    // EXT-TS-032
    const raiz = criarProjeto({
      'src/util.ts': `
export function somar(a: number, b: number): number { return Math.max(a, b); }
export const dobrar = (n: number) => somar(n, n);`,
      'src/tela.ts': `
import { somar, dobrar } from './util';
export class Tela {
  executar(): void { console.log(somar(1, 2)); dobrar(3); [1].map((x) => x + 1); }
}`,
    });
    const g = rodar(raiz, 'Tela.executar').json.graph;
    assert.deepEqual(
      g.nodes.map((n) => n.label),
      ['Tela.executar', 'somar', 'dobrar'],
    );
    assert.ok(g.edges.some((e) => e.to === 'm:src/util.ts#somar' && e.from === 'm:src/util.ts#dobrar'));
  });

  test('pipe e then: o corpo das funções passadas vira sequência sob o mesmo pai', () => {
    // EXT-TS-033
    const raiz = criarProjeto({
      'src/tela.ts': `
import { Observavel } from '@angular/common/http';
export class Tela {
  executar(o: Observavel<number>): void {
    o.pipe((x: number) => this.primeiro(x), (x: number) => this.segundo(x));
    Promise.resolve(1).then(() => this.terceiro());
  }
  primeiro(x: number): number { return x; }
  segundo(x: number): number { return x; }
  terceiro(): void {}
}`,
    });
    const g = rodar(raiz, 'Tela.executar').json.graph;
    const destinos = g.edges.filter((e) => e.from === g.entryNodeId).map((e) => e.to.split('#')[1]);
    assert.deepEqual(destinos, ['Tela.primeiro', 'Tela.segundo', 'Tela.terceiro']);
  });
});

describe('interface e classe abstrata', () => {
  const base = `
export interface Gateway { cobrar(valor: number): void; }
export abstract class Repositorio { abstract salvar(): void; }
`;

  test('uma só implementação: percorre nela', () => {
    // EXT-TS-040
    const raiz = criarProjeto({
      'src/base.ts': base,
      'src/gateway.ts': `import { Gateway } from './base'; export class GatewayPix implements Gateway { cobrar(valor: number): void { void valor; } }`,
      'src/tela.ts': `import { Gateway } from './base'; export class Tela { constructor(private g: Gateway) {} pagar(): void { this.g.cobrar(1); } }`,
    });
    const g = rodar(raiz, 'Tela.pagar').json.graph;
    assert.ok(g.nodes.some((n) => n.id === 'm:src/gateway.ts#GatewayPix.cobrar' && n.kind === NodeKind.Method));
  });

  test('nenhuma implementação: nó não resolvido NoImplementation', () => {
    // EXT-TS-041
    const raiz = criarProjeto({
      'src/base.ts': base,
      'src/tela.ts': `import { Gateway } from './base'; export class Tela { constructor(private g: Gateway) {} pagar(): void { this.g.cobrar(1); } }`,
    });
    const g = rodar(raiz, 'Tela.pagar').json.graph;
    const n = no(g, (x) => x.kind === NodeKind.Unresolved);
    assert.equal(n.reason, Reason.NoImplementation);
    assert.equal(n.label, 'this.g.cobrar');
    assert.equal(n.detail, 'Gateway.cobrar');
    assert.match(n.id, /^u:src\/tela\.ts:\d+:\d+$/);
  });

  test('mais de uma implementação: MultipleImplementations com os candidatos no detalhe', () => {
    // EXT-TS-042
    const raiz = criarProjeto({
      'src/base.ts': base,
      'src/a.ts': `import { Repositorio } from './base'; export class RepoA extends Repositorio { salvar(): void {} }`,
      'src/b.ts': `import { Repositorio } from './base'; export class RepoB extends Repositorio { salvar(): void {} }`,
      'src/tela.ts': `import { Repositorio } from './base'; export class Tela { constructor(private r: Repositorio) {} gravar(): void { this.r.salvar(); } }`,
    });
    const g = rodar(raiz, 'Tela.gravar').json.graph;
    const n = no(g, (x) => x.kind === NodeKind.Unresolved);
    assert.equal(n.reason, Reason.MultipleImplementations);
    assert.equal(n.detail, 'RepoA, RepoB');
  });
});

describe('HttpClient e rotas', () => {
  function chamadas(corpo, extra = '') {
    const raiz = criarProjeto({
      'src/tela.ts': `
import { HttpClient } from '@angular/common/http';
import { inject } from '@angular/core';
const BASE = '/api/fixa';
export class Tela {
  private http = inject(HttpClient);
  ${extra}
  executar(id: number, url: string): void {
    ${corpo}
  }
}`,
    });
    return rodar(raiz, 'Tela.executar').json;
  }

  test('verbos get, post, put, patch e delete, e request com verbo literal', () => {
    // EXT-TS-050
    const r = chamadas(`
      this.http.get('/api/a');
      this.http.post('/api/b', {});
      this.http.put('/api/c', {});
      this.http.patch('/api/d', {});
      this.http.delete('/api/e');
      this.http.request('delete', '/api/f');`);
    assert.deepEqual(
      r.httpCalls.map((c) => `${c.call.method} ${c.call.route}`),
      ['GET api/a', 'POST api/b', 'PUT api/c', 'PATCH api/d', 'DELETE api/e', 'DELETE api/f'],
    );
  });

  test('rota: esquema, host, query e fragmento saem; letras minúsculas fora de {}', () => {
    // EXT-TS-051
    const r = chamadas('this.http.get(`https://Exemplo.com/Api/Pedidos/${id}?x=1#topo`);');
    assert.equal(r.httpCalls[0].call.route, 'api/pedidos/{}');
  });

  test('rota: concatenação com + e const com literal', () => {
    // EXT-TS-052
    const r = chamadas(`
      this.http.get('/api/pedidos/' + id + '/itens');
      this.http.get(BASE + '/lista');
      this.http.get(BASE);`);
    assert.deepEqual(
      r.httpCalls.map((c) => c.call.route),
      ['api/pedidos/{}/itens', 'api/fixa/lista', 'api/fixa'],
    );
  });

  test('rota montada em tempo de execução: RouteNotResolved e label "GET ?"', () => {
    // EXT-TS-053
    const r = chamadas('this.http.get(url);');
    const n = no(r.graph, (x) => x.id.startsWith('h:'));
    assert.equal(n.label, 'GET ?');
    assert.equal(n.reason, Reason.RouteNotResolved);
    assert.deepEqual(r.httpCalls[0].call, { method: 'GET' });
    const aresta = r.graph.edges.find((e) => e.to === n.id);
    assert.equal(aresta.http.route, undefined);
  });

  test('só o endereço base no começo sai; ${} no começo seguido de texto', () => {
    // EXT-TS-054
    const r = chamadas('this.http.get(`${url}/api/x/${id}`);');
    assert.equal(r.httpCalls[0].call.route, 'api/x/{}');
  });

  test('um método get de outra classe não é HttpClient', () => {
    // EXT-TS-055
    const raiz = criarProjeto({
      'src/tela.ts': `
class Cache { get(chave: string): string { return chave; } }
export class Tela { private c = new Cache(); executar(): void { this.c.get('/api/a'); } }`,
    });
    const r = rodar(raiz, 'Tela.executar').json;
    assert.deepEqual(r.httpCalls, []);
    assert.ok(!r.graph.nodes.some((n) => n.id.startsWith('h:')));
  });

  test('profundidade da chamada: método que chama está mais fundo', () => {
    // EXT-TS-056
    const r = chamadas('this.http.get("/api/a"); this.dentro();', 'dentro(): void { this.http.get("/api/b"); }');
    assert.deepEqual(r.httpCalls.map((c) => [c.call.route, c.depth]), [['api/a', 0], ['api/b', 1]]);
  });
});

describe('guardas do template', () => {
  const componente = (template) => `
import { Component } from '@angular/core';
@Component({ selector: 'app-x', template: \`${template}\` })
export class Tela { salvar(): void {} outra(): void {} }`;

  function guardaDe(template) {
    const raiz = criarProjeto({ 'src/tela.ts': componente(template) });
    const g = rodar(raiz, 'Tela.salvar').json.graph;
    return no(g, (n) => n.kind === NodeKind.Decision);
  }

  test('*ngIf do próprio elemento e [disabled] viram a condição, juntos com &&', () => {
    // EXT-TS-060
    const d = guardaDe('<button *ngIf="pode" [disabled]="bloqueado" (click)="salvar()">Salvar</button>');
    assert.equal(d.label, 'if (pode && !(bloqueado))');
    assert.equal(d.condition, 'pode && !(bloqueado)');
    assert.match(d.id, /^if:src\/tela\.ts:\d+:\d+$/);
  });

  test('guardas dos elementos e dos @if que envolvem, da mais externa para a interna', () => {
    // EXT-TS-061
    const d = guardaDe(`
      <section *ngIf="logado">
        @if (temItens) {
          <div>
            <button (click)="salvar()">Salvar</button>
          </div>
        }
      </section>`);
    assert.equal(d.condition, 'logado && temItens');
  });

  test('um @if que já fechou antes do botão não guarda o botão', () => {
    // EXT-TS-062
    const raiz = criarProjeto({
      'src/tela.ts': componente('@if (outro) { <span>x</span> } <button (click)="salvar()">Salvar</button>'),
    });
    const g = rodar(raiz, 'Tela.salvar').json.graph;
    assert.equal(g.nodes.filter((n) => n.kind === NodeKind.Decision).length, 0);
  });

  test('sem guarda ou sem elemento que chame o método: o fluxo não muda', () => {
    // EXT-TS-063
    const raiz = criarProjeto({ 'src/tela.ts': componente('<button (click)="outra()">Outra</button>') });
    const g = rodar(raiz, 'Tela.salvar').json.graph;
    assert.equal(g.nodes.length, 1);
  });

  test('só o primeiro elemento que dispara o método conta', () => {
    // EXT-TS-064
    const d = guardaDe('<a *ngIf="primeiro" (click)="salvar()">a</a><b *ngIf="segundo" (click)="salvar()">b</b>');
    assert.equal(d.condition, 'primeiro');
  });

  test('guarda com || entre várias vem entre parênteses', () => {
    // EXT-TS-065
    const d = guardaDe('<div *ngIf="a || b"><button [disabled]="c" (click)="salvar()">x</button></div>');
    assert.equal(d.condition, '(a || b) && !(c)');
  });
});

describe('leitor de template (unidade)', () => {
  test('*ngIf com else e alias: fica só a condição', () => {
    // EXT-TS-070
    const g = acharGuardas('<p *ngIf="obs$ | async as valor; else vazio" (click)="ir()">x</p>', 'ir');
    assert.deepEqual(g.map((x) => x.condicao), ['obs$ | async']);
  });

  test('interpolação e comentário não atrapalham os blocos', () => {
    // EXT-TS-071
    const g = acharGuardas('<!-- @if (falso) { --> @if (real) { {{ valor }} <button (click)="ir()">x</button> }', 'ir');
    assert.deepEqual(g.map((x) => x.condicao), ['real']);
  });

  test('método com nome parecido não casa', () => {
    // EXT-TS-072
    assert.equal(acharGuardas('<button (click)="irEmbora()">x</button>', 'ir'), null);
    assert.equal(acharGuardas('<button (click)="outro.ir()">x</button>', 'ir'), null);
  });
});

describe('abertura de modal (MatDialog e Dialog do CDK)', () => {
  const ROTULO = 'ação do usuário no modal';
  const Confianca = { Extracted: 0, Inferred: 1 };

  // Tela que abre o modal e, depois de fechado, recarrega. O modal chama a API no botão.
  const tela = (importacao, tipo, abertura, depois) => `
import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
${importacao}
import { Confirmacao } from './confirmacao';
@Component({ selector: 'app-tela', template: '<button (click)="abrir()">Abrir</button>' })
export class Tela {
  constructor(private dialog: ${tipo}, private http: HttpClient) {}
  abrir(): void {
    ${abertura}.${depois}(ok => { if (ok) { this.recarregar(); } });
  }
  recarregar(): void { this.http.get('/api/lista'); }
}`;

  const modal = (decorador) => `
import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
${decorador}
export class Confirmacao {
  constructor(private http: HttpClient) {}
  confirmar(): void { this.http.post('/api/confirmar', {}); }
  cancelar(): void { this.http.delete('/api/cancelar'); }
  semEvento(): void { this.http.get('/api/nunca'); }
}`;

  const inline = `@Component({ selector: 'app-c', template: \`
    <button (click)="cancelar()">Não</button>
    <form (ngSubmit)="confirmar()"><button>Sim</button></form>
    <button (click)="dialogRef.close()">Fechar</button>\` })`;

  function levantar(arquivos, depth = 10) {
    return rodar(criarProjeto(arquivos), 'Tela.abrir', { depth });
  }

  const rotas = (r) => r.json.httpCalls.map((c) => `${c.call.method} ${c.call.route}`);

  test('MatDialog: galho "abre o modal" com os métodos dos eventos do template inline', () => {
    // EXT-TS-090
    const r = levantar({
      'src/tela.ts': tela("import { MatDialog } from '@angular/material/dialog';", 'MatDialog', 'this.dialog.open(Confirmacao, {})', 'afterClosed().subscribe'),
      'src/confirmacao.ts': modal(inline),
    });
    const g = r.json.graph;
    const galho = no(g, (n) => n.id === 'modal:src/confirmacao.ts#Confirmacao');
    assert.equal(galho.label, 'abre o modal Confirmacao');
    assert.ok(g.edges.some((e) => e.from === g.entryNodeId && e.to === galho.id));
    const acoes = g.edges.filter((e) => e.from === galho.id);
    assert.deepEqual(acoes.map((e) => e.to.split('#')[1]), ['Confirmacao.cancelar', 'Confirmacao.confirmar']);
    assert.ok(!g.nodes.some((n) => n.label === 'Confirmacao.semEvento'));
    assert.ok(!g.nodes.some((n) => n.label.includes('dialogRef')));
  });

  test('o HTTP do botão do modal sai no levantamento, com a ação inferida', () => {
    // EXT-TS-091
    const r = levantar({
      'src/tela.ts': tela("import { MatDialog } from '@angular/material/dialog';", 'MatDialog', 'this.dialog.open(Confirmacao)', 'afterClosed().subscribe'),
      'src/confirmacao.ts': modal(inline),
    });
    assert.deepEqual(rotas(r).sort(), ['DELETE api/cancelar', 'GET api/lista', 'POST api/confirmar']);
    const acoes = r.json.graph.edges.filter((e) => e.from === 'modal:src/confirmacao.ts#Confirmacao');
    assert.ok(acoes.length === 2 && acoes.every((e) => e.confidence === Confianca.Inferred && e.label === ROTULO));
    const aberturas = r.json.graph.edges.filter((e) => e.to === 'modal:src/confirmacao.ts#Confirmacao');
    assert.ok(aberturas.every((e) => e.confidence === Confianca.Extracted));
  });

  test('o afterClosed continua depois do galho, na ordem da tela', () => {
    // EXT-TS-092
    const r = levantar({
      'src/tela.ts': tela("import { MatDialog } from '@angular/material/dialog';", 'MatDialog', 'this.dialog.open(Confirmacao)', 'afterClosed().subscribe'),
      'src/confirmacao.ts': modal(inline),
    });
    const g = r.json.graph;
    const saidas = g.edges.filter((e) => e.from === g.entryNodeId);
    assert.equal(saidas[0].to, 'modal:src/confirmacao.ts#Confirmacao');
    const decisao = no(g, (n) => n.kind === NodeKind.Decision && n.label === 'if (ok)');
    assert.ok(decisao && saidas.some((e) => e.to === decisao.id && e.order > saidas[0].order));
    assert.ok(g.edges.some((e) => e.from === decisao.id && e.to.endsWith('#Tela.recarregar')));
  });

  test('Dialog do CDK com templateUrl e (closed)', () => {
    // EXT-TS-093
    const r = levantar({
      'src/tela.ts': tela("import { Dialog } from '@angular/cdk/dialog';", 'Dialog', 'this.dialog.open(Confirmacao)', 'closed.subscribe'),
      'src/confirmacao.ts': modal("@Component({ selector: 'app-c', templateUrl: './confirmacao.html' })"),
      'src/confirmacao.html': '<button (click)="confirmar()">Sim</button>\n<button (click)="cancelar()">Não</button>',
    });
    assert.deepEqual(rotas(r).sort(), ['DELETE api/cancelar', 'GET api/lista', 'POST api/confirmar']);
    assert.ok(r.json.graph.nodes.some((n) => n.id === 'modal:src/confirmacao.ts#Confirmacao'));
  });

  test('open de outra classe (nome parecido) não abre galho', () => {
    // EXT-TS-094
    const r = levantar({
      'src/tela.ts': `
import { Confirmacao } from './confirmacao';
class Dialog { open(c: unknown): void {} }
export class Tela { private dialog = new Dialog(); abrir(): void { this.dialog.open(Confirmacao); } }`,
      'src/confirmacao.ts': modal(inline),
    });
    assert.ok(!r.json.graph.nodes.some((n) => n.id.startsWith('modal:')));
    assert.deepEqual(r.json.httpCalls, []);
  });

  test('profundidade: no limite o galho mostra o limite e não entra nos métodos do modal', () => {
    // EXT-TS-095
    const r = levantar({
      'src/tela.ts': tela("import { MatDialog } from '@angular/material/dialog';", 'MatDialog', 'this.dialog.open(Confirmacao)', 'afterClosed().subscribe'),
      'src/confirmacao.ts': modal(inline),
    }, 1);
    const g = r.json.graph;
    assert.ok(g.nodes.some((n) => n.id === 'modal:src/confirmacao.ts#Confirmacao'));
    assert.ok(g.edges.some((e) => e.from === 'modal:src/confirmacao.ts#Confirmacao' && e.to.startsWith('u:depth:')));
    assert.ok(!rotas(r).includes('POST api/confirmar'));
  });

  test('modal que abre a si mesmo não entra em laço', () => {
    // EXT-TS-096
    const r = levantar({
      'src/tela.ts': tela("import { MatDialog } from '@angular/material/dialog';", 'MatDialog', 'this.dialog.open(Confirmacao)', 'afterClosed().subscribe'),
      'src/confirmacao.ts': `
import { Component } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
@Component({ selector: 'app-c', template: '<button (click)="outra()">Outra</button>' })
export class Confirmacao {
  constructor(private dialog: MatDialog) {}
  outra(): void { this.dialog.open(Confirmacao); }
}`,
    });
    assert.equal(r.codigo, 0);
    const g = r.json.graph;
    assert.equal(g.nodes.filter((n) => n.id.startsWith('modal:')).length, 1);
    assert.ok(g.edges.some((e) => e.from === 'm:src/confirmacao.ts#Confirmacao.outra' && e.to.startsWith('modal:')));
  });

  test('leitor de eventos (unidade): chamadas diretas e com this., sem repetir', () => {
    // EXT-TS-097
    const nomes = acharMetodosDeEventos(
      '<!-- (click)="velho()" --><a (click)="a(); this.b($event)"></a><f (ngSubmit)="a()" (x.y)="svc.c()"></f>',
    );
    assert.deepEqual(nomes, ['a', 'b']);
  });
});

describe('normalização de rota (mesmo resultado do RouteTemplate.Normalize do C#)', () => {
  const casos = [
    ['api/pedidos', 'api/pedidos'],
    ['/api/Pedidos/', 'api/pedidos'],
    ['https://host:5001/Api/Pedidos?x=1', 'api/pedidos'],
    ['http://host', ''],
    ['~/api/x', 'api/x'],
    ['api/Pedidos/{Id}/Itens#frag', 'api/pedidos/{Id}/itens'],
    ['  /api/x  ', 'api/x'],
    ['api/{}/Cancelamento', 'api/{}/cancelamento'],
  ];
  for (const [entrada, esperado] of casos) {
    test(`"${entrada}" vira "${esperado}"`, () => {
      // EXT-TS-080
      assert.equal(normalizarRota(entrada), esperado);
    });
  }
});
