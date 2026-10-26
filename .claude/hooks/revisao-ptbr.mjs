// Governança de texto em português do Brasil.
//
// Todo texto que a IA escreve (documento novo, revisão de documento, texto de tela,
// mensagem de commit) passa por uma revisão de ortografia, gramática, pontuação e
// acentuação antes de a sessão encerrar. A revisão em si é feita pelo Claude com a
// skill `revisar-texto-ptbr`; este hook garante que ela aconteça e adianta o que dá
// para pegar sem leitura (palavra sem acento, cedilha ou til).
//
// Modos (primeiro argumento):
//   pos-edicao  PostToolUse de Edit/Write/MultiEdit. Registra o arquivo de texto tocado
//               e, em prosa (.md, .txt...), devolve na hora as palavras sem acento.
//   commit      PreToolUse de Bash/PowerShell. Recusa `git commit` cuja mensagem tenha
//               palavra sem acento, cedilha ou til.
//   parada      Stop. Se algum texto tocado nesta sessão mudou desde a última revisão,
//               bloqueia o encerramento uma vez e pede a revisão completa.
//
// Falso positivo: acrescentar a palavra em `.claude/hooks/revisao-ptbr.permitidas.txt`
// (uma por linha). Nunca falha a sessão: erro interno sai em silêncio.

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, extname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const RAIZ = process.env.CLAUDE_PROJECT_DIR || join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const EXT_PROSA = new Set(['.md', '.mdx', '.txt', '.adoc', '.rst']);
// Arquivos com texto de tela: entram na revisão do encerramento, mas não na varredura
// mecânica, porque misturam texto com identificador.
const EXT_COPIA = new Set(['.html', '.htm', '.ftl', '.hbs']);
const IGNORAR = /(^|[\\/])(node_modules|dist|bin|obj|\.git|\.angular|coverage)[\\/]/;

// ---------------------------------------------------------------------------
// Dicionário: grafia sem acento -> grafia correta. Só entra o que é inequívoco;
// o ambíguo (esta/está, e/é, publica/pública, analise/análise) fica para a leitura.

const CERTAS = new Map();
const pares = (texto) => {
  for (const par of texto.trim().split(/\s+/)) {
    const [errada, certa] = par.split(':');
    CERTAS.set(errada, certa);
  }
};
// Radical de adjetivo/substantivo em -o: gera o, a, os, as.
const flexoes = (texto) => {
  for (const par of texto.trim().split(/\s+/)) {
    const [errada, certa] = par.split(':');
    for (const s of ['o', 'a', 'os', 'as']) CERTAS.set(errada + s, certa + s);
  }
};

pares(`
  nao:não entao:então voce:você voces:vocês tambem:também ja:já ate:até so:só alem:além
  porem:porém apos:após atraves:através ha:há tres:três mes:mês meses:meses ingles:inglês
  portugues:português ninguem:ninguém alguem:alguém atras:atrás ola:olá sao:são
  sera:será serao:serão estara:estará estarao:estarão tera:terá terao:terão podera:poderá
  poderao:poderão devera:deverá deverao:deverão havera:haverá fara:fará farao:farão
  dara:dará darao:darão ficara:ficará precisara:precisará funcionara:funcionará
  codigo:código codigos:códigos numero:número numeros:números paginas:páginas
  metodo:método metodos:métodos periodo:período periodos:períodos indice:índice indices:índices
  titulo:título titulos:títulos capitulo:capítulo capitulos:capítulos modulo:módulo modulos:módulos
  calculo:cálculo calculos:cálculos vinculo:vínculo vinculos:vínculos veiculo:veículo veiculos:veículos
  obstaculo:obstáculo obstaculos:obstáculos area:área areas:áreas conteudo:conteúdo conteudos:conteúdos
  saida:saída saidas:saídas serie:série series:séries especie:espécie especies:espécies
  midia:mídia midias:mídias sintese:síntese hipotese:hipótese hipoteses:hipóteses
  parenteses:parênteses historia:história historias:histórias memoria:memória memorias:memórias
  materia:matéria materias:matérias inicio:início inicios:inícios negocio:negócio negocios:negócios
  comercio:comércio exercicio:exercício exercicios:exercícios agua:água raizes:raízes ruido:ruído
  servico:serviço servicos:serviços endereco:endereço enderecos:endereços preco:preço precos:preços
  comeco:começo esforco:esforço esforcos:esforços espaco:espaço espacos:espaços
  lancamento:lançamento lancamentos:lançamentos cabecalho:cabeçalho cabecalhos:cabeçalhos
  facil:fácil faceis:fáceis dificil:difícil dificeis:difíceis util:útil uteis:úteis
  inutil:inútil agil:ágil ageis:ágeis movel:móvel moveis:móveis imovel:imóvel nivel:nível
  niveis:níveis maximo:máximo minimo:mínimo
  incluido:incluído incluida:incluída incluidos:incluídos incluidas:incluídas
  excluido:excluído excluida:excluída excluidos:excluídos excluidas:excluídas
  construido:construído construida:construída distribuido:distribuído distribuida:distribuída
  concluido:concluído concluida:concluída atribuido:atribuído atribuida:atribuída
  substituido:substituído substituida:substituída contribuido:contribuído
`);
flexoes(`
  tecnic:técnic logic:lógic basic:básic automatic:automátic dinamic:dinâmic historic:históric
  grafic:gráfic rapid:rápid especific:específic generic:genéric critic:crític pratic:prátic
  unic:únic ultim:últim proxim:próxim otim:ótim obvi:óbvi classic:clássic
  academic:acadêmic economic:econômic tipic:típic solid:sólid liquid:líquid fisic:físic
  quimic:químic magic:mágic eletronic:eletrônic politic:polític matematic:matemátic
  cientific:científic didatic:didátic estatic:estátic semantic:semântic sistematic:sistemátic
  analitic:analític sintatic:sintátic hierarquic:hierárquic periodic:periódic metric:métric
  publicitari:publicitári
`);
CERTAS.set('proprio', 'próprio'); CERTAS.set('propria', 'própria');
CERTAS.set('proprios', 'próprios'); CERTAS.set('proprias', 'próprias');
// Flexões que também são verbo (ele pratica, ele critica, ele especifica): ficam para a leitura.
for (const ambigua of ['pratica', 'critica', 'especifica']) {
  CERTAS.delete(ambigua);
}

// Terminações: [regex, troca, exceções].
const TERMINACOES = [
  [/^(.+)cao$/, '$1ção', ['cacao']],
  [/^(.+)coes$/, '$1ções', []],
  [/^(.+)sao$/, '$1são', []],
  [/^(.+)soes$/, '$1sões', []],
  [/^(.+(?:i|lh|nh|r|t))oes$/, '$1ões', ['heroes', 'vetoes', 'potatoes', 'tomatoes']],
  [/^(.{2,})ao$/, '$1ão', ['ciao', 'macao', 'callao']],
  [/^(.+)anca(s?)$/, '$1ança$2', ['branca', 'brancas', 'franca', 'francas', 'banca', 'bancas', 'tranca', 'trancas',
    'manca', 'alavanca', 'alavancas', 'arranca', 'estanca', 'barranca', 'blanca', 'casablanca', 'panca']],
  [/^(.+)enca(s?)$/, '$1ença$2', ['penca']],
  [/^(.+)avel$/, '$1ável', ['travel', 'gravel', 'unravel', 'ravel', 'javel']],
  [/^(.+)ivel$/, '$1ível', []],
  [/^(.+)aveis$/, '$1áveis', []],
  [/^(.+)iveis$/, '$1íveis', []],
  [/^(.+)ario(s?)$/, '$1ário$2', ['scenario', 'scenarios', 'mario', 'dario', 'ontario', 'rosario']],
  [/^(.+)orio(s?)$/, '$1ório$2', ['gregorio', 'osorio']],
  // Verbos que coincidem com o substantivo (ele referencia, ele influencia) ficam de fora.
  [/^(.+)encia(s?)$/, '$1ência$2', ['referencia', 'influencia', 'evidencia', 'sequencia', 'diferencia', 'licencia',
    'silencia', 'vivencia', 'presencia', 'potencia', 'agencia', 'vencia', 'convencia', 'pertencia']],
  [/^(.+)ancia(s?)$/, '$1ância$2', ['financia', 'distancia', 'substancia', 'circunstancia', 'instancia']],
];

function permitidas() {
  const arquivo = join(RAIZ, '.claude', 'hooks', 'revisao-ptbr.permitidas.txt');
  if (!existsSync(arquivo)) return new Set();
  return new Set(readFileSync(arquivo, 'utf8').split(/\r?\n/).map((l) => l.trim().toLowerCase()).filter(Boolean));
}

function sugestao(palavra) {
  if (CERTAS.has(palavra)) return CERTAS.get(palavra);
  for (const [regex, troca, excecoes] of TERMINACOES) {
    if (regex.test(palavra) && !excecoes.includes(palavra)) return palavra.replace(regex, troca);
  }
  return null;
}

// Tira o que não é prosa: blocos e trechos de código, URLs, destino de link, tags e
// interpolações. O que sobra é o texto que alguém lê.
function limparProsa(texto) {
  return texto
    .replace(/^(```|~~~)[\s\S]*?^\1\s*$/gm, ' ')
    .replace(/`[^`\n]*`/g, ' ')
    .replace(/\]\([^)]*\)/g, '] ')
    .replace(/https?:\/\/\S+/g, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<[^>\n]+>/g, ' ')
    .replace(/\{\{[\s\S]*?\}\}/g, ' ')
    .replace(/\$\{[^}]*\}/g, ' ');
}

function textoVisivel(html) {
  return html
    .replace(/<(script|style)[\s\S]*?<\/\1>/gi, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<\/?[^>]*>/g, ' ')
    .replace(/<#[^>]*>|\$\{[^}]*\}/g, ' ')
    .replace(/\{\{[\s\S]*?\}\}/g, ' ')
    .replace(/@\w+\s*(\([^)]*\))?\s*\{?/g, ' ')
    .replace(/[{}]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

// Texto majoritariamente em inglês não passa pela varredura de português.
function pareceIngles(texto) {
  const en = (texto.match(/\b(the|and|of|to|is|with|that|for|this|are)\b/gi) || []).length;
  const pt = (texto.match(/\b(de|que|para|com|uma|um|do|da|no|na|em|os|as)\b/gi) || []).length;
  return en >= 3 && en > pt * 1.5;
}

function verificar(textoBruto) {
  const texto = limparProsa(textoBruto);
  if (pareceIngles(texto)) return [];
  const liberadas = permitidas();
  const achados = new Map();
  for (const m of texto.matchAll(/\p{L}+/gu)) {
    const palavra = m[0];
    const antes = texto[m.index - 1] || ' ';
    const depois = texto[m.index + palavra.length] || ' ';
    const depoisDepois = texto[m.index + palavra.length + 1] || ' ';
    // Parte de identificador, caminho, slug ou e-mail: não é prosa.
    if (/[-_/\\.@#$=]/.test(antes)) continue;
    if (/[-_/\\@#=]/.test(depois)) continue;
    if (depois === '.' && /[\p{L}\d]/u.test(depoisDepois)) continue;
    if (palavra.length > 1 && palavra === palavra.toUpperCase()) continue;
    if (/\p{Lu}/u.test(palavra.slice(1))) continue;
    if (/[^\x00-\x7F]/.test(palavra)) continue;
    const minuscula = palavra.toLowerCase();
    if (liberadas.has(minuscula)) continue;
    const certa = sugestao(minuscula);
    if (!certa || certa === minuscula) continue;
    const exibida = palavra[0] === palavra[0].toUpperCase() ? certa[0].toUpperCase() + certa.slice(1) : certa;
    achados.set(palavra, exibida);
  }
  return [...achados].map(([errada, certa]) => `"${errada}" -> "${certa}"`);
}

// ---------------------------------------------------------------------------
// Estado da sessão: arquivos de texto tocados e o hash da última versão revisada.
// Fica dentro do diretório do git, para nunca ir parar num commit.

function git(args) {
  return execFileSync('git', args, { cwd: RAIZ, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
}

function dirEstado() {
  let base;
  try {
    base = resolve(RAIZ, git(['rev-parse', '--git-dir']).trim());
  } catch {
    base = tmpdir();
  }
  const dir = join(base, 'revisao-ptbr');
  mkdirSync(dir, { recursive: true });
  const limite = Date.now() - 7 * 24 * 3600 * 1000;
  for (const nome of readdirSync(dir)) {
    try {
      if (statSync(join(dir, nome)).mtimeMs < limite) unlinkSync(join(dir, nome));
    } catch { /* outro processo limpou */ }
  }
  return dir;
}

function arquivoEstado(sessao) {
  return join(dirEstado(), `${String(sessao || 'sem-sessao').replace(/[^\w-]/g, '_')}.json`);
}

function lerEstado(sessao) {
  try {
    return JSON.parse(readFileSync(arquivoEstado(sessao), 'utf8'));
  } catch {
    return { tocados: {}, bloqueado: false };
  }
}

function gravarEstado(sessao, estado) {
  writeFileSync(arquivoEstado(sessao), JSON.stringify(estado, null, 2));
}

const ehProsa = (caminho) => EXT_PROSA.has(extname(caminho).toLowerCase());
const ehTexto = (caminho) => ehProsa(caminho) || EXT_COPIA.has(extname(caminho).toLowerCase());

// O que conta para "mudou": em prosa, o arquivo todo; em template, só o texto visível
// (mexer num atributo ou binding não pede revisão de texto).
function impressao(conteudo, caminho) {
  const base = ehProsa(caminho) ? conteudo : textoVisivel(conteudo);
  return createHash('sha1').update(base).digest('hex');
}

function versaoNoHead(relativo) {
  try {
    return git(['show', `HEAD:${relativo.replace(/\\/g, '/')}`]);
  } catch {
    return null;
  }
}

function relativoDaRaiz(caminho) {
  return relative(RAIZ, isAbsolute(caminho) ? caminho : resolve(RAIZ, caminho));
}

// Linhas acrescentadas em relação ao HEAD (arquivo novo: todas).
function linhasNovas(relativo) {
  const head = versaoNoHead(relativo);
  const atual = readFileSync(join(RAIZ, relativo), 'utf8');
  if (head === null) return atual;
  try {
    return git(['diff', '-U0', 'HEAD', '--', relativo])
      .split(/\r?\n/)
      .filter((l) => l.startsWith('+') && !l.startsWith('+++'))
      .map((l) => l.slice(1))
      .join('\n');
  } catch {
    return atual;
  }
}

// ---------------------------------------------------------------------------

function lerEntrada() {
  try {
    return JSON.parse(readFileSync(0, 'utf8') || '{}');
  } catch {
    return null;
  }
}

function textoEscrito(entrada) {
  const ti = entrada.tool_input || {};
  if (typeof ti.content === 'string') return ti.content;
  const edicoes = Array.isArray(ti.edits) ? ti.edits : [{ old_string: ti.old_string, new_string: ti.new_string }];
  // Só as linhas que a edição trouxe de novo; o contexto copiado do original não conta.
  return edicoes
    .map((e) => {
      const antigas = new Set(String(e.old_string || '').split(/\r?\n/));
      return String(e.new_string || '').split(/\r?\n/).filter((l) => !antigas.has(l)).join('\n');
    })
    .join('\n');
}

function posEdicao(entrada) {
  const caminho = entrada.tool_input?.file_path;
  if (!caminho || IGNORAR.test(caminho) || !ehTexto(caminho)) return;
  const relativo = relativoDaRaiz(caminho).replace(/\\/g, '/');
  if (relativo.startsWith('..')) return;

  const estado = lerEstado(entrada.session_id);
  if (!(relativo in estado.tocados)) {
    // Ponto de partida: a versão do HEAD. Se a sessão desfizer o que escreveu, não há o que revisar.
    const head = versaoNoHead(relativo);
    estado.tocados[relativo] = head === null ? null : impressao(head, relativo);
    gravarEstado(entrada.session_id, estado);
  }

  if (!ehProsa(caminho)) return;
  const achados = verificar(textoEscrito(entrada));
  if (achados.length === 0) return;
  process.stdout.write(JSON.stringify({
    decision: 'block',
    reason:
      `Revisão de texto pt-BR: ${relativo} ficou com palavra sem acento, cedilha ou til.\n` +
      achados.map((a) => `  - ${a}`).join('\n') +
      '\nCorrija no arquivo agora (skill revisar-texto-ptbr). Se algum item for nome próprio, ' +
      'identificador ou termo de outro idioma, acrescente a palavra em .claude/hooks/revisao-ptbr.permitidas.txt.',
  }));
}

function extrairMensagem(comando) {
  const partes = [];
  for (const m of comando.matchAll(/<<-?\s*['"]?(\w+)['"]?[^\n]*\n([\s\S]*?)\n\s*\1\b/g)) partes.push(m[2]);
  for (const m of comando.matchAll(/@(['"])\r?\n([\s\S]*?)\r?\n\1@/g)) partes.push(m[2]);
  for (const m of comando.matchAll(/(?:^|\s)(?:-m|--message)(?:\s+|=)(["'])([\s\S]*?)\1/g)) partes.push(m[2]);
  const arquivo = comando.match(/(?:^|\s)(?:-F|--file)(?:\s+|=)(?!-\s)(["']?)([^\s"']+)\1/);
  if (arquivo && arquivo[2] !== '-') {
    const caminho = resolve(RAIZ, arquivo[2]);
    if (existsSync(caminho)) partes.push(readFileSync(caminho, 'utf8'));
  }
  // O escopo do commit semântico (chore(governanca):) é identificador, e não prosa.
  return partes.join('\n').replace(/^\s*[a-z]+(\([^)\n]*\))?!?:/gm, ' ');
}

function commit(entrada) {
  const comando = String(entrada.tool_input?.command || '');
  if (!/\bgit\b[^\n]*\bcommit\b/.test(comando)) return;
  const mensagem = extrairMensagem(comando);
  if (!mensagem.trim()) return;
  const achados = verificar(mensagem);
  if (achados.length === 0) return;
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason:
        'Revisão de texto pt-BR: a mensagem de commit tem palavra sem acento, cedilha ou til.\n' +
        achados.map((a) => `  - ${a}`).join('\n') +
        '\nReescreva a mensagem com a grafia correta e confira também gramática e pontuação ' +
        '(skill revisar-texto-ptbr). Falso positivo: .claude/hooks/revisao-ptbr.permitidas.txt.',
    },
  }));
}

function parada(entrada) {
  const estado = lerEstado(entrada.session_id);
  const pendentes = [];
  for (const [relativo, revisado] of Object.entries(estado.tocados)) {
    const caminho = join(RAIZ, relativo);
    if (!existsSync(caminho)) {
      delete estado.tocados[relativo];
      continue;
    }
    const atual = impressao(readFileSync(caminho, 'utf8'), relativo);
    if (atual !== revisado) pendentes.push([relativo, atual]);
  }

  // Segunda parada depois do nosso bloqueio: a revisão foi feita, então o que está no
  // disco vira a versão revisada. Bloquear de novo aqui prenderia a sessão em laço.
  if (entrada.stop_hook_active && estado.bloqueado) {
    for (const [relativo, atual] of pendentes) estado.tocados[relativo] = atual;
    estado.bloqueado = false;
    gravarEstado(entrada.session_id, estado);
    return;
  }
  if (pendentes.length === 0) {
    gravarEstado(entrada.session_id, estado);
    return;
  }

  const linhas = pendentes.map(([relativo]) => {
    let dicas = [];
    try {
      const novas = linhasNovas(relativo);
      dicas = verificar(ehProsa(relativo) ? novas : textoVisivel(novas));
    } catch { /* a dica é só adiantamento; a leitura cobre */ }
    return `  - ${relativo}${dicas.length ? `  (já visto: ${dicas.join(', ')})` : ''}`;
  });

  estado.bloqueado = true;
  gravarEstado(entrada.session_id, estado);
  process.stdout.write(JSON.stringify({
    decision: 'block',
    reason:
      'Governança de texto pt-BR: antes de encerrar, revise o texto que você escreveu ou alterou nesta sessão.\n' +
      linhas.join('\n') +
      '\nAplique a skill revisar-texto-ptbr: leia só o trecho novo (git diff HEAD -- <arquivo>; arquivo novo, inteiro; ' +
      'em template, só o texto visível), corrija ortografia, gramática, pontuação e acentuação direto no arquivo e não ' +
      'toque em código, identificador, caminho, URL nem citação literal. Termine com uma linha dizendo o que corrigiu ' +
      '(ou que não havia o que corrigir).',
  }));
}

try {
  const entrada = lerEntrada();
  if (entrada) {
    const modo = process.argv[2];
    if (modo === 'pos-edicao') posEdicao(entrada);
    else if (modo === 'commit') commit(entrada);
    else if (modo === 'parada') parada(entrada);
  }
} catch {
  // Governança que derruba a sessão vira governança desligada. Falha interna sai em silêncio.
}
process.exit(0);
