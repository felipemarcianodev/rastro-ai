#!/usr/bin/env node
// Extrator Angular do Rastro AI (ADR-005).
//
//   node extract.mjs --root <raiz do repositório> --project <tsconfig relativo à raiz>
//                    --entry <Componente.metodo> --depth <n> [--lang <idioma>]
//
// Saída padrão: UM JSON e nada mais (é o protocolo com a API). Sucesso:
//   { "graph": FlowGraph, "httpCalls": [{ "nodeId", "call": { "method", "route", "fields", "moreFields" }, "depth" }] }
// (route, fields e moreFields só aparecem quando existem; fields são os campos enviados, Fase 34)
// Idioma das mensagens e dos rótulos que vão para a tela: --lang pt-BR, en-US ou es (padrão en-US, lib/mensagens.mjs).
// Erro: código de saída 1 e uma linha { "error": "<mensagem para o usuário, no idioma de --lang>" }.
// Diagnóstico e detalhe técnico vão para a saída de erro, nunca para a padrão.

import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { extrair, ErroDeUsuario } from './lib/walker.mjs';
import { tradutor } from './lib/mensagens.mjs';

const PROFUNDIDADE_PADRAO = 15;

// O idioma das mensagens vem de --lang; é lido antes de tudo para valer até no erro de argumento.
function idiomaPedido(argv) {
  const i = argv.indexOf('--lang');
  return i >= 0 ? argv[i + 1] : undefined;
}

function ler(argv, t) {
  const opcoes = {};
  for (let i = 0; i < argv.length; i++) {
    const chave = argv[i];
    if (!chave.startsWith('--')) throw new ErroDeUsuario(t('unexpectedArgument', chave));
    const valor = argv[i + 1];
    if (valor === undefined || valor.startsWith('--')) throw new ErroDeUsuario(t('missingValue', chave));
    opcoes[chave.slice(2)] = valor;
    i++;
  }

  for (const obrigatoria of ['root', 'project', 'entry']) {
    if (!opcoes[obrigatoria]) throw new ErroDeUsuario(t('missingArgument', obrigatoria));
  }

  const depth = opcoes.depth === undefined ? PROFUNDIDADE_PADRAO : Number(opcoes.depth);
  if (!Number.isInteger(depth) || depth < 0) {
    throw new ErroDeUsuario(t('invalidDepth', opcoes.depth));
  }
  return { root: opcoes.root, project: opcoes.project, entry: opcoes.entry, depth, lang: opcoes.lang };
}

// Pastas do tsconfig até a raiz do repositório, da mais funda para a raiz. Em monorepo o app Angular
// mora numa subpasta (frontend/), com o próprio package.json e node_modules. Nunca sai do repositório.
function pastasAteARaiz(raiz, projeto) {
  const pastas = [];
  let pasta = path.dirname(path.resolve(raiz, projeto));
  const dentro = (p) => p === raiz || p.startsWith(raiz + path.sep);
  while (dentro(pasta)) {
    pastas.push(pasta);
    if (pasta === raiz) break;
    pasta = path.dirname(pasta);
  }
  return pastas.length > 0 ? pastas : [raiz];
}

// O compilador vem, nesta ordem: do repositório analisado (a versão que o projeto usa, procurada da
// pasta do tsconfig até a raiz), da variável RASTRO_TYPESCRIPT (caminho do pacote) e, por último, do
// node_modules deste extrator.
function carregarTypeScript(pastas, t) {
  const doRepositorio = pastas
    .filter((pasta) => fs.existsSync(path.join(pasta, 'node_modules', 'typescript', 'package.json')))
    .map((pasta) => () => createRequire(path.join(pasta, 'package.json')).resolve('typescript'));
  const tentativas = [
    ...doRepositorio,
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
  throw new ErroDeUsuario(t('typescriptNotFound'));
}

// O nome do projeto é o do package.json mais perto do tsconfig; sem nenhum, o nome da pasta da raiz.
function nomeDoProjeto(raiz, pastas) {
  for (const pasta of pastas) {
    const arquivo = path.join(pasta, 'package.json');
    if (!fs.existsSync(arquivo)) continue;
    try {
      const pacote = JSON.parse(fs.readFileSync(arquivo, 'utf8'));
      if (typeof pacote.name === 'string' && pacote.name) return pacote.name;
    } catch (erro) {
      process.stderr.write(`package.json não pôde ser lido em ${pasta} (${erro.code ?? erro.message}).\n`);
    }
  }
  return path.basename(raiz);
}

function principal(t) {
  const opcoes = ler(process.argv.slice(2), t);
  const raiz = path.resolve(opcoes.root);
  if (!fs.existsSync(raiz) || !fs.statSync(raiz).isDirectory()) {
    throw new ErroDeUsuario(t('repositoryFolderNotFound'));
  }

  const pastas = pastasAteARaiz(raiz, opcoes.project);
  const ts = carregarTypeScript(pastas, t);
  return extrair(ts, { ...opcoes, root: raiz, projectName: nomeDoProjeto(raiz, pastas) });
}

function responder(corpo, codigo) {
  process.stdout.write(JSON.stringify(corpo) + '\n');
  process.exitCode = codigo;
}

const traduzir = tradutor(idiomaPedido(process.argv.slice(2)));

try {
  responder(principal(traduzir), 0);
} catch (erro) {
  if (erro instanceof ErroDeUsuario) {
    responder({ error: erro.message }, 1);
  } else {
    // Defeito do extrator: o detalhe técnico fica na saída de erro e o usuário recebe uma frase.
    process.stderr.write(`${erro.stack ?? erro}\n`);
    responder({ error: traduzir('unexpectedFailure') }, 1);
  }
}
