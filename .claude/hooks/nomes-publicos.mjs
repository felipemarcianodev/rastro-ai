// Nome de repositório ou de empresa só entra no repositório público com autorização do dono, e
// nenhum commit leva coautor nem identidade de fora.
//
// PreToolUse de Bash/PowerShell e da criação de PR pelo MCP. Olha `git commit` e `gh pr create`
// neste repositório: a mensagem, o título e o corpo, e as linhas adicionadas. Recusa o comando se
// achar um nome proibido, uma linha Co-authored-by ou um e-mail do git que não seja o noreply.
//
// De onde vêm os nomes, sempre de fora do repositório, porque citá-los aqui já os exporia:
// - as pastas vizinhas a este repositório (os outros projetos do dono);
// - termos.txt, a mesma lista do scripts/verificar-publico.mjs (clientes e empresas);
// - autorizados.txt libera um nome, um por linha. O próprio repositório já vem liberado.
// As listas ficam em ~/.rastro-publico (ou em RASTRO_PUBLICO_DIR).
//
// Nunca trava o trabalho por defeito próprio: erro interno libera o comando e avisa no stderr.

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, realpathSync } from 'node:fs';
import { homedir } from 'node:os';
import { basename, dirname, isAbsolute, join, resolve } from 'node:path';

const RAIZ = process.env.CLAUDE_PROJECT_DIR || join(dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', '..');
const LISTAS = process.env.RASTRO_PUBLICO_DIR || join(homedir(), '.rastro-publico');
const COAUTOR = /co-authored-by\s*:/i;
const EMAIL_PERMITIDO = /@users\.noreply\.github\.com$/i;

function lerEntrada() {
  try {
    return JSON.parse(readFileSync(0, 'utf8') || '{}');
  } catch {
    return null;
  }
}

function lista(arquivo) {
  const caminho = join(LISTAS, arquivo);
  if (!existsSync(caminho)) return [];
  return readFileSync(caminho, 'utf8')
    .split(/\r?\n/)
    .map((linha) => linha.trim().toLowerCase())
    .filter((linha) => linha && !linha.startsWith('#'));
}

function git(pasta, args) {
  return execFileSync('git', ['-C', pasta, ...args], { encoding: 'utf8', timeout: 8000, maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'ignore'] });
}

function caminhoReal(caminho) {
  // A versão nativa expande os nomes curtos do Windows (ADMINI~1), como o git faz.
  return realpathSync.native(caminho).replace(/\\/g, '/').toLowerCase();
}

// A pasta onde o comando roda: `git -C <pasta>`, um `cd <pasta>` antes dele ou a da sessão.
function pastaDoComando(comando, cwd) {
  const alvo = comando.match(/\bgit\s+-C\s+("[^"]+"|'[^']+'|\S+)/) ?? comando.match(/\bcd\s+("[^"]+"|'[^']+'|[^\s;&]+)/);
  const texto = alvo ? alvo[1].replace(/^["']|["']$/g, '').replace(/^\/([a-zA-Z])\//, '$1:/') : null;
  if (!texto) return cwd;
  return isAbsolute(texto) ? texto : resolve(cwd, texto);
}

function ehEsteRepositorio(pasta) {
  try {
    return caminhoReal(git(pasta, ['rev-parse', '--show-toplevel']).trim()) === caminhoReal(RAIZ);
  } catch {
    return false;
  }
}

export function nomesProibidos() {
  const proprio = basename(RAIZ).toLowerCase();
  const autorizados = new Set([proprio, ...lista('autorizados.txt')]);
  const vizinhas = readdirSync(dirname(RAIZ), { withFileTypes: true })
    .filter((item) => item.isDirectory() && !item.name.startsWith('.'))
    .map((item) => item.name.toLowerCase());
  return [...new Set([...vizinhas, ...lista('termos.txt')])].filter((nome) => !autorizados.has(nome));
}

function contem(texto, nome) {
  const escapado = nome.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(?<![\\p{L}\\p{N}_-])${escapado}(?![\\p{L}\\p{N}_-])`, 'iu').test(texto);
}

// Só as linhas adicionadas: o que já está no histórico é assunto do verificador público.
function linhasAdicionadas(diff) {
  const achadas = [];
  let arquivo = '';
  for (const linha of diff.split(/\r?\n/)) {
    if (linha.startsWith('+++ ')) arquivo = linha.slice(4).replace(/^b\//, '');
    else if (linha.startsWith('+')) achadas.push({ arquivo, texto: linha.slice(1) });
  }
  return achadas;
}

// Mensagem ou corpo em arquivo: -F e --file no commit; -F e --body-file no PR.
function textoEmArquivo(comando, pasta) {
  const achado = comando.match(/(?:\s-F|--file|--body-file)(?:\s+|=)("[^"]+"|'[^']+'|\S+)/);
  if (!achado || achado[1] === '-') return '';
  const nome = achado[1].replace(/^["']|["']$/g, '');
  const caminho = isAbsolute(nome) ? nome : resolve(pasta, nome);
  return existsSync(caminho) ? readFileSync(caminho, 'utf8') : '';
}

function negar(motivos) {
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason:
        'Repositório público: o comando leva o que não pode ir sem autorização do dono.\n' +
        motivos.map((m) => `  - ${m}`).join('\n') +
        '\nTire o nome ou peça a autorização ao dono. Autorizado, o nome entra em ~/.rastro-publico/autorizados.txt.',
    },
  }));
}

function conferirTexto(rotulo, texto, nomes, motivos) {
  if (COAUTOR.test(texto)) motivos.push(`${rotulo}: linha Co-authored-by`);
  for (const nome of nomes) if (contem(texto, nome)) motivos.push(`${rotulo}: "${nome}"`);
}

function conferirLinhas(diff, nomes, motivos) {
  for (const { arquivo, texto } of linhasAdicionadas(diff)) {
    for (const nome of nomes) if (contem(texto, nome)) motivos.push(`${arquivo}: "${nome}"`);
  }
}

function conferir(entrada) {
  const motivos = [];

  if (String(entrada.tool_name || '').includes('create_pull_request')) {
    conferirTexto('PR', `${entrada.tool_input?.title ?? ''}\n${entrada.tool_input?.body ?? ''}`, nomesProibidos(), motivos);
    if (motivos.length > 0) negar(motivos);
    return;
  }

  const comando = String(entrada.tool_input?.command || '');
  const ehCommit = /\bgit\b[^\n]*\bcommit\b/.test(comando);
  const ehPr = /\bgh\b[^\n]*\bpr\b[^\n]*\bcreate\b/.test(comando);
  if (!ehCommit && !ehPr) return;

  const pasta = pastaDoComando(comando, entrada.cwd || process.cwd());
  if (!ehEsteRepositorio(pasta)) return;

  const nomes = nomesProibidos();
  conferirTexto(ehCommit ? 'mensagem do commit' : 'PR', `${comando}\n${textoEmArquivo(comando, pasta)}`, nomes, motivos);

  if (ehCommit) {
    const email = git(pasta, ['config', 'user.email']).trim();
    if (!EMAIL_PERMITIDO.test(email)) motivos.push(`e-mail do git fora do noreply do GitHub: ${email}`);
    conferirLinhas(git(pasta, ['diff', '--cached', '-U0', '--no-color', '--no-ext-diff']), nomes, motivos);
  } else {
    conferirLinhas(git(pasta, ['diff', '-U0', '--no-color', '--no-ext-diff', 'origin/main...HEAD']), nomes, motivos);
  }

  if (motivos.length > 0) negar([...new Set(motivos)]);
}

try {
  const entrada = lerEntrada();
  if (entrada) conferir(entrada);
} catch (erro) {
  process.stderr.write(`nomes-publicos: conferência não rodou (${erro?.message ?? erro}); o comando segue.\n`);
}
process.exit(0);
