#!/usr/bin/env node
// Extrator Angular do Rastro AI (ADR-005).
//
//   node extract.mjs --root <raiz do repositório> --project <tsconfig relativo à raiz>
//                    --entry <Componente.metodo> --depth <n>
//
// Saída padrão: UM JSON e nada mais (é o protocolo com a API). Sucesso:
//   { "graph": FlowGraph, "httpCalls": [{ "nodeId", "call": { "method", "route" }, "depth" }] }
// Erro: código de saída 1 e uma linha { "error": "<mensagem em pt-BR para o usuário>" }.
// Diagnóstico e detalhe técnico vão para a saída de erro, nunca para a padrão.

import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { extrair, ErroDeUsuario } from './lib/walker.mjs';

const PROFUNDIDADE_PADRAO = 15;

function ler(argv) {
  const opcoes = {};
  for (let i = 0; i < argv.length; i++) {
    const chave = argv[i];
    if (!chave.startsWith('--')) throw new ErroDeUsuario(`Argumento inesperado: ${chave}.`);
    const valor = argv[i + 1];
    if (valor === undefined || valor.startsWith('--')) throw new ErroDeUsuario(`Falta o valor de ${chave}.`);
    opcoes[chave.slice(2)] = valor;
    i++;
  }

  for (const obrigatoria of ['root', 'project', 'entry']) {
    if (!opcoes[obrigatoria]) throw new ErroDeUsuario(`Falta o argumento --${obrigatoria}.`);
  }

  const depth = opcoes.depth === undefined ? PROFUNDIDADE_PADRAO : Number(opcoes.depth);
  if (!Number.isInteger(depth) || depth < 0) {
    throw new ErroDeUsuario(`A profundidade (--depth) precisa ser um número inteiro, 0 ou maior: "${opcoes.depth}".`);
  }
  return { root: opcoes.root, project: opcoes.project, entry: opcoes.entry, depth };
}

// O compilador vem, nesta ordem: do repositório analisado (a versão que o projeto usa), da variável
// RASTRO_TYPESCRIPT (caminho do pacote) e, por último, do node_modules deste extrator.
function carregarTypeScript(raiz) {
  const tentativas = [
    () => createRequire(path.join(raiz, 'package.json')).resolve('typescript'),
    () => (process.env.RASTRO_TYPESCRIPT ? createRequire(import.meta.url).resolve(path.resolve(process.env.RASTRO_TYPESCRIPT)) : null),
    () => createRequire(import.meta.url).resolve('typescript'),
  ];

  for (const tentar of tentativas) {
    try {
      const caminho = tentar();
      if (caminho) return createRequire(import.meta.url)(caminho);
    } catch (erro) {
      // Esse local não tem o TypeScript: tenta o próximo e registra o motivo só para diagnóstico.
      process.stderr.write(`TypeScript não resolvido por este caminho (${erro.code ?? erro.message}).\n`);
    }
  }
  throw new ErroDeUsuario('O TypeScript não foi encontrado no repositório. Rode o npm install nele.');
}

function nomeDoProjeto(raiz) {
  try {
    const pacote = JSON.parse(fs.readFileSync(path.join(raiz, 'package.json'), 'utf8'));
    if (typeof pacote.name === 'string' && pacote.name) return pacote.name;
  } catch (erro) {
    process.stderr.write(`package.json da raiz não pôde ser lido (${erro.code ?? erro.message}).\n`);
  }
  return path.basename(raiz);
}

function principal() {
  const opcoes = ler(process.argv.slice(2));
  const raiz = path.resolve(opcoes.root);
  if (!fs.existsSync(raiz) || !fs.statSync(raiz).isDirectory()) {
    throw new ErroDeUsuario('A pasta do repositório não foi encontrada.');
  }

  const ts = carregarTypeScript(raiz);
  return extrair(ts, { ...opcoes, root: raiz, projectName: nomeDoProjeto(raiz) });
}

function responder(corpo, codigo) {
  process.stdout.write(JSON.stringify(corpo) + '\n');
  process.exitCode = codigo;
}

try {
  responder(principal(), 0);
} catch (erro) {
  if (erro instanceof ErroDeUsuario) {
    responder({ error: erro.message }, 1);
  } else {
    // Defeito do extrator: o detalhe técnico fica na saída de erro e o usuário recebe uma frase.
    process.stderr.write(`${erro.stack ?? erro}\n`);
    responder({ error: 'Não foi possível ler o código Angular. Veja o log da API para o detalhe.' }, 1);
  }
}
