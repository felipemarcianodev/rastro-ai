// Leitura leve do template de um componente Angular, só para achar as condições que guardam um evento.
//
// LIMITE CONHECIDO: não é o compilador do Angular. É um leitor de tags e de blocos de controle
// (@if, @for, @switch...) feito para uma pergunta só: "o que precisa ser verdade para o usuário
// conseguir disparar este método?". O que ele lê:
//   - blocos @if (cond) { ... } e @else if (cond) { ... } que envolvem o elemento;
//   - *ngIf="cond" do próprio elemento e dos que o envolvem;
//   - [disabled]="cond" do próprio elemento (a guarda vira !(cond)).
// O que ele NÃO faz: negar a condição anterior dentro de um @else, expandir <ng-template>,
// seguir diretivas estruturais customizadas nem avaliar expressões. Uma guarda que ele não
// entende simplesmente não aparece: o fluxo continua certo, só com menos bifurcação na tela.

const ELEMENTOS_VAZIOS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr',
]);

const BLOCO = /^@(else\s+if|if|else|for|switch|case|default|defer|empty|placeholder|loading|error)\b\s*/;

function escaparRegex(texto) {
  return texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function limpar(texto) {
  return texto.replace(/\s+/g, ' ').trim();
}

// Posição (linha e coluna, base 1) de um deslocamento dentro do texto do template.
export function linhaEColuna(texto, deslocamento) {
  let linha = 1;
  let inicioDaLinha = 0;
  for (let i = 0; i < deslocamento && i < texto.length; i++) {
    if (texto[i] === '\n') {
      linha++;
      inicioDaLinha = i + 1;
    }
  }
  return { linha, coluna: deslocamento - inicioDaLinha + 1 };
}

// Índice do ")" que fecha o "(" em `inicio`, respeitando aspas. -1 se não fecha.
function fecharParenteses(texto, inicio) {
  let nivel = 0;
  let aspas = null;
  for (let i = inicio; i < texto.length; i++) {
    const c = texto[i];
    if (aspas) {
      if (c === aspas && texto[i - 1] !== '\\') aspas = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') aspas = c;
    else if (c === '(') nivel++;
    else if (c === ')' && --nivel === 0) return i;
  }
  return -1;
}

// Lê uma tag aberta começando em `<nome`: devolve os atributos e o índice depois do ">".
function lerTag(texto, inicio, nome) {
  let i = inicio + 1 + nome.length;
  let aspas = null;
  const comeco = i;
  while (i < texto.length) {
    const c = texto[i];
    if (aspas) {
      if (c === aspas) aspas = null;
    } else if (c === '"' || c === "'") {
      aspas = c;
    } else if (c === '>') {
      break;
    }
    i++;
  }
  const corpo = texto.slice(comeco, i);
  const autoFechada = corpo.trimEnd().endsWith('/');
  const atributos = [];
  const re = /([^\s=\/>"'][^\s=\/>"']*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g;
  for (const m of corpo.matchAll(re)) {
    atributos.push({ nome: m[1], valor: m[2] ?? m[3] ?? m[4] ?? '' });
  }
  return { atributos, autoFechada, fim: Math.min(i + 1, texto.length) };
}

// Condição de um *ngIf: sem o "; else modelo" e sem o "as alias".
function condicaoDoNgIf(valor) {
  let condicao = valor;
  const ponto = condicao.indexOf(';');
  if (ponto >= 0) condicao = condicao.slice(0, ponto);
  condicao = condicao.replace(/\s+as\s+[\w$]+\s*$/, '');
  return limpar(condicao);
}

// Nomes dos métodos chamados em eventos do template ((click)="salvar()", (ngSubmit)="enviar(f)"...),
// na ordem em que aparecem e sem repetição. Só vale chamada direta ou por `this.`; `servico.x()` e
// `ref.close()` ficam de fora. Também é uma leitura leve, sem o compilador do Angular: serve para
// listar as ações do usuário num modal, que o TypeScript não prova.
export function acharMetodosDeEventos(texto) {
  const semComentarios = texto.replace(/<!--[\s\S]*?-->/g, ' ');
  const nomes = [];
  const evento = /\(([\w.:-]+)\)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
  for (const m of semComentarios.matchAll(evento)) {
    const expressao = (m[2] ?? m[3]).replace(/\bthis\./g, '');
    for (const c of expressao.matchAll(/(^|[^\w.$])([A-Za-z_$][\w$]*)\s*\(/g)) {
      if (!nomes.includes(c[2])) nomes.push(c[2]);
    }
  }
  return nomes;
}

// Procura o primeiro elemento cujo evento chama `metodo(` e devolve as guardas que o cercam,
// da mais externa para a mais interna: [{ condicao, deslocamento }]. Nulo quando nenhum
// elemento do template chama o método.
export function acharGuardas(texto, metodo) {
  const evento = new RegExp(`(^|[^\\w.$])${escaparRegex(metodo)}\\s*\\(`);
  const pilha = [];
  let i = 0;

  while (i < texto.length) {
    const c = texto[i];

    if (texto.startsWith('<!--', i)) {
      const fim = texto.indexOf('-->', i + 4);
      i = fim < 0 ? texto.length : fim + 3;
      continue;
    }

    // Interpolação {{ ... }}: as chaves dela não fecham bloco de controle.
    if (texto.startsWith('{{', i)) {
      const fim = texto.indexOf('}}', i + 2);
      i = fim < 0 ? texto.length : fim + 2;
      continue;
    }

    if (c === '@') {
      const m = BLOCO.exec(texto.slice(i, i + 40));
      if (m) {
        let j = i + m[0].length;
        let condicao = null;
        if (texto[j] === '(') {
          const fim = fecharParenteses(texto, j);
          if (fim < 0) {
            i++;
            continue;
          }
          condicao = limpar(texto.slice(j + 1, fim));
          j = fim + 1;
          while (/\s/.test(texto[j] ?? '')) j++;
        }
        if (texto[j] === '{') {
          const ehIf = m[1] === 'if' || /^else\s+if$/.test(m[1]);
          pilha.push({ tipo: 'bloco', guarda: ehIf && condicao ? condicao : null, deslocamento: i });
          i = j + 1;
          continue;
        }
      }
      i++;
      continue;
    }

    if (c === '}') {
      // Fecha o bloco de controle mais próximo; elementos abertos dentro dele ficam para trás.
      for (let k = pilha.length - 1; k >= 0; k--) {
        if (pilha[k].tipo === 'bloco') {
          pilha.length = k;
          break;
        }
      }
      i++;
      continue;
    }

    if (c === '<') {
      if (texto[i + 1] === '/') {
        const m = /^<\/([\w:.-]+)/.exec(texto.slice(i, i + 80));
        const fim = texto.indexOf('>', i);
        if (m) {
          const nome = m[1].toLowerCase();
          for (let k = pilha.length - 1; k >= 0 && pilha[k].tipo === 'elemento'; k--) {
            if (pilha[k].nome === nome) {
              pilha.length = k;
              break;
            }
          }
        }
        i = fim < 0 ? texto.length : fim + 1;
        continue;
      }

      const m = /^<([A-Za-z][\w:.-]*)/.exec(texto.slice(i, i + 80));
      if (m) {
        const nome = m[1].toLowerCase();
        const tag = lerTag(texto, i, m[1]);
        let ngIf = null;
        let desabilitado = null;
        let chamaMetodo = false;
        for (const { nome: atributo, valor } of tag.atributos) {
          if (atributo === '*ngIf') ngIf = condicaoDoNgIf(valor);
          else if (atributo === '[disabled]') desabilitado = limpar(valor);
          else if (/^\([\w.:-]+\)$/.test(atributo) && evento.test(valor)) chamaMetodo = true;
        }

        if (chamaMetodo) {
          const guardas = pilha
            .filter((p) => p.guarda)
            .map((p) => ({ condicao: p.guarda, deslocamento: p.deslocamento }));
          if (ngIf) guardas.push({ condicao: ngIf, deslocamento: i });
          if (desabilitado) guardas.push({ condicao: `!(${desabilitado})`, deslocamento: i });
          return guardas;
        }

        if (!tag.autoFechada && !ELEMENTOS_VAZIOS.has(nome)) {
          pilha.push({ tipo: 'elemento', nome, guarda: ngIf || null, deslocamento: i });
        }
        i = tag.fim;
        continue;
      }
    }

    i++;
  }
  return null;
}
