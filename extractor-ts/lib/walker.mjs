// Percorre um método TypeScript e tudo o que ele chama, gerando o mesmo FlowGraph do extrator C#.
// Determinístico: só o que o TypeChecker prova vira aresta; o resto vira nó "unresolved" com o
// motivo (ADR-001). Os ids, os rótulos e a ordem das arestas seguem o FlowWalker do C#.

import fs from 'node:fs';
import path from 'node:path';
import { EdgeConfidence, GraphBuilder, NodeKind, UnresolvedReason, truncar } from './grafo.mjs';
import { rotaDosPedacos } from './rota.mjs';
import { acharGuardas, acharMetodosDeEventos, linhaEColuna } from './template.mjs';

// Erro com mensagem já escrita para o usuário (pt-BR). Qualquer outro erro é defeito do extrator.
export class ErroDeUsuario extends Error {}

const MAX_SUGESTOES = 5;

// Métodos do HttpClient que viram chamada HTTP, com o verbo.
const VERBOS_HTTP = new Map([
  ['get', 'GET'],
  ['post', 'POST'],
  ['put', 'PUT'],
  ['patch', 'PATCH'],
  ['delete', 'DELETE'],
]);
const VERBOS_ACEITOS = new Set(['GET', 'POST', 'PUT', 'PATCH', 'DELETE']);
const VERBO_DESCONHECIDO = 'SEND';

const MODULO_HTTP = '@angular/common/http';
const NOME_HTTP_CLIENT = 'HttpClient';
const MODULO_MAT_DIALOG = '@angular/material/dialog';
const MODULO_CDK_DIALOG = '@angular/cdk/dialog';
const ROTULO_ACAO_NO_MODAL = 'ação do usuário no modal';

const limpar = (texto) => texto.replace(/\s+/g, ' ').trim();

export function extrair(ts, { root, project, entry, depth, projectName }) {
  const raiz = path.resolve(root);
  const grafo = new GraphBuilder();
  const httpCalls = [];

  // ---------- programa ----------

  const tsconfig = path.resolve(raiz, project);
  if (!fs.existsSync(tsconfig)) {
    throw new ErroDeUsuario(`O arquivo ${project} não foi encontrado no repositório.`);
  }
  const lido = ts.readConfigFile(tsconfig, ts.sys.readFile);
  if (lido.error) {
    throw new ErroDeUsuario(
      `Não foi possível ler o ${project}: ${ts.flattenDiagnosticMessageText(lido.error.messageText, ' ')}`,
    );
  }
  const configuracao = ts.parseJsonConfigFileContent(lido.config, ts.sys, path.dirname(tsconfig), undefined, tsconfig);
  if (configuracao.fileNames.length === 0) {
    throw new ErroDeUsuario(`O ${project} não inclui nenhum arquivo TypeScript.`);
  }
  const programa = ts.createProgram({
    rootNames: configuracao.fileNames,
    options: { ...configuracao.options, noEmit: true },
    projectReferences: configuracao.projectReferences,
  });
  const checker = programa.getTypeChecker();

  const relativo = (arquivo) => path.relative(raiz, path.resolve(arquivo)).split(path.sep).join('/');

  // Só o código do projeto é percorrido: nada de node_modules, de declaração (.d.ts) nem de teste.
  // O que está fora vira, no máximo, chamada externa ignorada.
  const ehDoProjeto = (sf) =>
    !sf.isDeclarationFile &&
    !/[\\/]node_modules[\\/]/.test(sf.fileName) &&
    !/\.spec\.tsx?$/.test(sf.fileName);
  const arquivosDoProjeto = programa.getSourceFiles().filter(ehDoProjeto);

  function posicao(no) {
    const sf = no.getSourceFile();
    const p = sf.getLineAndCharacterOfPosition(no.getStart(sf));
    return { file: relativo(sf.fileName), line: p.line + 1, col: p.character + 1 };
  }

  // ---------- índice de classes ----------

  const classes = [];
  for (const sf of arquivosDoProjeto) {
    const visitar = (no) => {
      if (ts.isClassDeclaration(no) && no.name) classes.push(no);
      ts.forEachChild(no, visitar);
    };
    visitar(sf);
  }

  const textoDoNome = (nome) =>
    nome && (ts.isIdentifier(nome) || ts.isStringLiteralLike(nome) || ts.isPrivateIdentifier(nome))
      ? nome.text
      : (nome?.getText() ?? null);

  const nomeDaClasse = (no) =>
    ts.isClassLike(no) ? (no.name?.text ?? '(anônima)') : null;

  function simbolo(no) {
    const s = checker.getSymbolAtLocation(no);
    return s && s.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(s) : s;
  }

  // ---------- descrição dos métodos ----------

  const descritores = new Map();

  // Transforma uma declaração com corpo em { id, label, corpo, nomeNo }. Nulo se não for percorrível.
  function descrever(decl) {
    if (descritores.has(decl)) return descritores.get(decl);
    const d = montarDescritor(decl);
    descritores.set(decl, d);
    return d;
  }

  function montarDescritor(decl) {
    let label;
    let nomeNo;
    if (ts.isMethodDeclaration(decl)) {
      if (!decl.body) return null;
      const nome = textoDoNome(decl.name);
      const dono = nomeDaClasse(decl.parent);
      label = dono ? `${dono}.${nome}` : nome;
      nomeNo = decl.name;
    } else if (ts.isFunctionDeclaration(decl)) {
      if (!decl.body || !decl.name) return null;
      label = decl.name.text;
      nomeNo = decl.name;
    } else if (ts.isArrowFunction(decl) || ts.isFunctionExpression(decl)) {
      const pai = decl.parent;
      if (ts.isVariableDeclaration(pai) && ts.isIdentifier(pai.name)) {
        label = pai.name.text;
        nomeNo = pai.name;
      } else if (ts.isPropertyDeclaration(pai) && pai.initializer === decl) {
        const dono = nomeDaClasse(pai.parent);
        label = dono ? `${dono}.${textoDoNome(pai.name)}` : textoDoNome(pai.name);
        nomeNo = pai.name;
      } else if (ts.isPropertyAssignment(pai) && pai.initializer === decl) {
        label = textoDoNome(pai.name);
        nomeNo = pai.name;
      } else {
        return null;
      }
    } else {
      return null;
    }

    const p = posicao(nomeNo);
    return { id: `m:${p.file}#${label}`, label, corpo: decl.body, location: { file: p.file, line: p.line } };
  }

  // Declaração da chamada -> descritor do corpo. Uma sobrecarga sem corpo procura a implementação.
  function descritorDe(decl) {
    const direto = descrever(decl);
    if (direto) return direto;
    if ((ts.isMethodDeclaration(decl) || ts.isFunctionDeclaration(decl)) && !decl.body && decl.name) {
      const s = checker.getSymbolAtLocation(decl.name);
      const comCorpo = s?.declarations?.find((d) => d !== decl && (ts.isMethodDeclaration(d) || ts.isFunctionDeclaration(d)) && d.body);
      if (comCorpo) return descrever(comCorpo);
    }
    return null;
  }

  // ---------- implementações de interface e de classe abstrata ----------

  const ancestraisCache = new Map();

  // Símbolos de tudo o que a classe estende ou implementa, direta ou indiretamente.
  function ancestrais(decl, visitados = new Set()) {
    if (ancestraisCache.has(decl)) return ancestraisCache.get(decl);
    const resultado = new Set();
    if (visitados.has(decl)) return resultado;
    visitados.add(decl);
    for (const clausula of decl.heritageClauses ?? []) {
      for (const tipo of clausula.types) {
        const s = simbolo(tipo.expression);
        if (!s) continue;
        resultado.add(s);
        for (const d of s.declarations ?? []) {
          if (ts.isClassDeclaration(d) || ts.isInterfaceDeclaration(d)) {
            for (const a of ancestrais(d, visitados)) resultado.add(a);
          }
        }
      }
    }
    ancestraisCache.set(decl, resultado);
    return resultado;
  }

  // Método `nome` com corpo na classe ou, subindo pelo extends, numa classe base do projeto.
  function metodoNaClasse(classe, nome, visitadas = new Set()) {
    if (visitadas.has(classe)) return null;
    visitadas.add(classe);
    for (const membro of classe.members) {
      if (textoDoNome(membro.name) !== nome) continue;
      if (ts.isMethodDeclaration(membro) && membro.body) return descrever(membro);
      if (ts.isPropertyDeclaration(membro) && membro.initializer &&
          (ts.isArrowFunction(membro.initializer) || ts.isFunctionExpression(membro.initializer))) {
        return descrever(membro.initializer);
      }
    }
    const extende = classe.heritageClauses?.find((c) => c.token === ts.SyntaxKind.ExtendsKeyword);
    const base = extende && simbolo(extende.types[0].expression);
    const declBase = base?.declarations?.find((d) => ts.isClassDeclaration(d) && ehDoProjeto(d.getSourceFile()));
    return declBase ? metodoNaClasse(declBase, nome, visitadas) : null;
  }

  function implementacoesDe(container, nome) {
    const alvo = container.name && checker.getSymbolAtLocation(container.name);
    if (!alvo) return [];
    const achados = new Map();
    for (const classe of classes) {
      if (classe === container) continue;
      if (classe.modifiers?.some((m) => m.kind === ts.SyntaxKind.AbstractKeyword)) continue;
      if (!ancestrais(classe).has(alvo)) continue;
      const d = metodoNaClasse(classe, nome);
      if (d) achados.set(d.id, d);
    }
    return [...achados.values()].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
  }

  // ---------- ponto de entrada ----------

  const pontos = entry.split('.');
  if (pontos.length < 2 || pontos.some((p) => p.length === 0)) {
    throw new ErroDeUsuario(`Ponto de entrada inválido: "${entry}". Use Classe.metodo.`);
  }
  const nomeMetodo = pontos[pontos.length - 1];
  const nomeClasse = pontos[pontos.length - 2];

  const candidatas = classes.filter((c) => c.name.text === nomeClasse);
  if (candidatas.length === 0 || !candidatas.some((c) => metodoNaClasse(c, nomeMetodo))) {
    const mesmoNome = [];
    for (const c of classes) {
      if (metodoNaClasse(c, nomeMetodo)) mesmoNome.push(`${c.name.text}.${nomeMetodo}`);
    }
    mesmoNome.sort();
    if (mesmoNome.length === 0) {
      throw new ErroDeUsuario(
        `Método "${entry}" não encontrado no projeto: nenhum método chamado ${nomeMetodo}, com corpo, existe nele.`,
      );
    }
    const mostrados = mesmoNome.slice(0, MAX_SUGESTOES).join(', ');
    const mais = mesmoNome.length > MAX_SUGESTOES ? ` e mais ${mesmoNome.length - MAX_SUGESTOES}` : '';
    throw new ErroDeUsuario(
      `Método "${entry}" não encontrado no projeto. Com o nome ${nomeMetodo} existe: ${mostrados}${mais}. Use Classe.metodo.`,
    );
  }
  const comMetodo = candidatas.filter((c) => metodoNaClasse(c, nomeMetodo));
  if (comMetodo.length > 1) {
    const arquivos = comMetodo.map((c) => relativo(c.getSourceFile().fileName)).sort().join(', ');
    throw new ErroDeUsuario(
      `O ponto de entrada "${entry}" é ambíguo: a classe ${nomeClasse} existe em mais de um arquivo (${arquivos}).`,
    );
  }
  const classeDaEntrada = comMetodo[0];

  // ---------- template do componente (bifurcações da tela) ----------

  // Guardas do primeiro elemento do template que dispara o método: viram uma decisão no começo do fluxo.
  function guardaDoTemplate(classe, metodo) {
    const modelo = lerTemplate(classe);
    if (!modelo) return null;
    const { texto, arquivo, linhaBase, colunaBase } = modelo;

    const guardas = acharGuardas(texto, metodo);
    if (!guardas || guardas.length === 0) return null;

    const juntas = guardas.length === 1
      ? guardas[0].condicao
      : guardas.map((g) => (/\|\||\?/.test(g.condicao) ? `(${g.condicao})` : g.condicao)).join(' && ');
    const p = linhaEColuna(texto, guardas[0].deslocamento);
    const linha = linhaBase + p.linha - 1;
    const coluna = p.linha === 1 ? colunaBase + p.coluna - 1 : p.coluna;
    return { condicao: juntas, arquivo, linha, coluna };
  }

  // Texto do template do @Component (inline ou templateUrl) e onde ele começa. Nulo se não houver.
  function lerTemplate(classe) {
    const decorador = (ts.canHaveDecorators(classe) ? (ts.getDecorators(classe) ?? []) : []).find(
      (d) =>
        ts.isCallExpression(d.expression) &&
        ts.isIdentifier(d.expression.expression) &&
        d.expression.expression.text === 'Component',
    );
    const config = decorador?.expression.arguments[0];
    if (!config || !ts.isObjectLiteralExpression(config)) return null;

    let texto = null;
    let arquivo = null;
    let linhaBase = 1;
    let colunaBase = 1;
    for (const prop of config.properties) {
      if (!ts.isPropertyAssignment(prop)) continue;
      const nome = textoDoNome(prop.name);
      if (nome === 'templateUrl' && ts.isStringLiteralLike(prop.initializer)) {
        const caminho = path.resolve(path.dirname(classe.getSourceFile().fileName), prop.initializer.text);
        try {
          texto = fs.readFileSync(caminho, 'utf8');
          arquivo = relativo(caminho);
        } catch (erro) {
          // Template ausente não derruba o levantamento: o fluxo sai sem as guardas da tela.
          process.stderr.write(`Aviso: template ${caminho} não pôde ser lido (${erro.message}).\n`);
        }
        break;
      }
      if (nome === 'template' && ts.isStringLiteralLike(prop.initializer)) {
        texto = prop.initializer.text;
        const p = posicao(prop.initializer);
        arquivo = p.file;
        linhaBase = p.line;
        colunaBase = p.col + 1;
      }
    }
    if (texto === null) return null;
    return { texto, arquivo, linhaBase, colunaBase };
  }

  // ---------- percurso ----------

  const ehFuncao = (no) =>
    ts.isArrowFunction(no) || ts.isFunctionExpression(no) || ts.isFunctionDeclaration(no) ||
    ts.isMethodDeclaration(no) || ts.isGetAccessor(no) || ts.isSetAccessor(no) ||
    ts.isConstructorDeclaration(no) || ts.isClassLike(no);

  function percorrerMetodo(desc, nivel, ehEntrada, guarda) {
    if (grafo.temNo(desc.id)) return desc.id;

    grafo.addNo({
      id: desc.id,
      kind: ehEntrada ? NodeKind.Entry : NodeKind.Method,
      label: desc.label,
      symbol: desc.label,
      location: desc.location,
      project: projectName,
    });

    if (nivel >= depth) {
      const limite = `u:depth:${desc.id}`;
      grafo.addNo({
        id: limite,
        kind: NodeKind.Unresolved,
        label: 'limite de profundidade',
        reason: UnresolvedReason.DepthLimit,
        detail: `MaxDepth = ${depth}`,
      });
      grafo.addAresta(desc.id, limite, undefined);
      return desc.id;
    }

    let pai = desc.id;
    let ramo;
    if (guarda) {
      const idGuarda = `if:${guarda.arquivo}:${guarda.linha}:${guarda.coluna}`;
      grafo.addNo({
        id: idGuarda,
        kind: NodeKind.Decision,
        label: truncar(`if (${guarda.condicao})`),
        location: { file: guarda.arquivo, line: guarda.linha },
        condition: truncar(guarda.condicao),
      });
      grafo.addAresta(desc.id, idGuarda, undefined);
      pai = idGuarda;
      ramo = 'then';
    }

    const ctx = { nivel };
    if (ts.isBlock(desc.corpo)) percorrerComando(desc.corpo, pai, ramo, false, ctx);
    else percorrerExpressao(desc.corpo, pai, ramo, ctx);
    return desc.id;
  }

  function adicionarEstrutura(prefixo, no, kind, label, pai, ramo, condicao) {
    const p = posicao(no);
    const id = `${prefixo}:${p.file}:${p.line}:${p.col}`;
    grafo.addNo({
      id,
      kind,
      label: truncar(label),
      location: { file: p.file, line: p.line },
      condition: condicao === undefined ? undefined : truncar(condicao),
    });
    grafo.addAresta(pai, id, ramo);
    return id;
  }

  const texto = (no) => limpar(no.getText(no.getSourceFile()));

  function percorrerComando(comando, pai, ramo, aninhado, ctx) {
    if (ts.isBlock(comando)) {
      for (const interno of comando.statements) percorrerComando(interno, pai, ramo, aninhado, ctx);
      return;
    }

    if (ts.isIfStatement(comando)) {
      percorrerExpressao(comando.expression, pai, ramo, ctx);
      const condicao = texto(comando.expression);
      const id = adicionarEstrutura('if', comando, NodeKind.Decision, `if (${condicao})`, pai, ramo, condicao);
      percorrerComando(comando.thenStatement, id, 'then', true, ctx);
      if (comando.elseStatement) percorrerComando(comando.elseStatement, id, 'else', true, ctx);
      return;
    }

    if (ts.isSwitchStatement(comando)) {
      percorrerExpressao(comando.expression, pai, ramo, ctx);
      const expressao = texto(comando.expression);
      const id = adicionarEstrutura('switch', comando, NodeKind.Decision, `switch (${expressao})`, pai, ramo, expressao);
      // "case 1: case 2: ..." sem corpo entre eles é um ramo só, com os rótulos juntos (como no C#).
      let rotulos = [];
      for (const clausula of comando.caseBlock.clauses) {
        rotulos.push(ts.isDefaultClause(clausula) ? 'default' : `case ${texto(clausula.expression)}`);
        if (clausula.statements.length === 0) continue;
        const rotulo = rotulos.join(' | ');
        rotulos = [];
        for (const interno of clausula.statements) percorrerComando(interno, id, rotulo, true, ctx);
      }
      return;
    }

    if (ts.isTryStatement(comando)) {
      const id = adicionarEstrutura('try', comando, NodeKind.Try, 'try', pai, ramo);
      percorrerComando(comando.tryBlock, id, 'try', true, ctx);
      if (comando.catchClause) {
        const variavel = comando.catchClause.variableDeclaration;
        const rotulo = variavel ? `catch (${texto(variavel.name)})` : 'catch';
        percorrerComando(comando.catchClause.block, id, rotulo, true, ctx);
      }
      if (comando.finallyBlock) percorrerComando(comando.finallyBlock, id, 'finally', true, ctx);
      return;
    }

    if (ts.isForOfStatement(comando) || ts.isForInStatement(comando)) {
      percorrerExpressao(comando.expression, pai, ramo, ctx);
      const variavel = ts.isVariableDeclarationList(comando.initializer)
        ? texto(comando.initializer.declarations[0].name)
        : texto(comando.initializer);
      const id = adicionarEstrutura(
        'loop', comando, NodeKind.Loop, `foreach (${variavel} in ${texto(comando.expression)})`, pai, ramo, texto(comando.expression),
      );
      percorrerComando(comando.statement, id, 'each', true, ctx);
      return;
    }

    if (ts.isForStatement(comando)) {
      const condicao = comando.condition ? texto(comando.condition) : '';
      const id = adicionarEstrutura('loop', comando, NodeKind.Loop, `for (${condicao})`, pai, ramo, condicao);
      percorrerComando(comando.statement, id, 'each', true, ctx);
      return;
    }

    if (ts.isWhileStatement(comando)) {
      percorrerExpressao(comando.expression, pai, ramo, ctx);
      const condicao = texto(comando.expression);
      const id = adicionarEstrutura('loop', comando, NodeKind.Loop, `while (${condicao})`, pai, ramo, condicao);
      percorrerComando(comando.statement, id, 'each', true, ctx);
      return;
    }

    if (ts.isDoStatement(comando)) {
      const condicao = texto(comando.expression);
      const id = adicionarEstrutura('loop', comando, NodeKind.Loop, `do … while (${condicao})`, pai, ramo, condicao);
      percorrerComando(comando.statement, id, 'each', true, ctx);
      percorrerExpressao(comando.expression, id, 'each', ctx);
      return;
    }

    if (ts.isReturnStatement(comando)) {
      if (comando.expression) percorrerExpressao(comando.expression, pai, ramo, ctx);
      // Só o return dentro de uma bifurcação ou laço é saída antecipada; o do fim do método é só o fim.
      if (aninhado) {
        const rotulo = comando.expression ? `return ${texto(comando.expression)}` : 'return';
        adicionarEstrutura('return', comando, NodeKind.Return, rotulo, pai, ramo);
      }
      return;
    }

    if (ts.isThrowStatement(comando)) {
      if (comando.expression) percorrerExpressao(comando.expression, pai, ramo, ctx);
      const rotulo = comando.expression && ts.isNewExpression(comando.expression)
        ? `throw new ${texto(comando.expression.expression)}`
        : `throw ${texto(comando.expression)}`;
      adicionarEstrutura('throw', comando, NodeKind.Throw, rotulo, pai, ramo);
      return;
    }

    if (ts.isLabeledStatement(comando)) {
      percorrerComando(comando.statement, pai, ramo, aninhado, ctx);
      return;
    }

    if (ts.isContinueStatement(comando)) {
      adicionarEstrutura('jump', comando, NodeKind.Jump, 'continue', pai, ramo);
      return;
    }

    if (ts.isBreakStatement(comando)) {
      // break de case de switch é só sintaxe; break de laço é desvio.
      if (!comando.label && quemRecebeOBreak(comando) === 'switch') return;
      adicionarEstrutura('jump', comando, NodeKind.Jump, 'break', pai, ramo);
      return;
    }

    // Declaração de função ou de classe dentro do método só entra no fluxo quando é chamada.
    if (ts.isFunctionDeclaration(comando) || ts.isClassDeclaration(comando)) return;

    percorrerExpressao(comando, pai, ramo, ctx);
  }

  function quemRecebeOBreak(no) {
    for (let atual = no.parent; atual; atual = atual.parent) {
      if (ts.isSwitchStatement(atual)) return 'switch';
      if (ts.isIterationStatement(atual, false)) return 'laco';
      if (ehFuncao(atual)) return null;
    }
    return null;
  }

  // Chamadas dentro de uma expressão, na ordem de execução (a interna antes da externa).
  // Funções passadas como argumento são percorridas por quem as recebe, não aqui.
  function percorrerExpressao(no, pai, ramo, ctx) {
    const chamadas = [];
    const visitar = (n) => {
      if (ehFuncao(n)) return;
      if (ts.isCallExpression(n)) chamadas.push(n);
      ts.forEachChild(n, visitar);
    };
    visitar(no);

    chamadas.sort((a, b) => a.getEnd() - b.getEnd() || b.getStart() - a.getStart());
    for (const chamada of chamadas) tratarChamada(chamada, pai, ramo, ctx);
  }

  function tratarChamada(chamada, pai, ramo, ctx) {
    const declaracao = checker.getResolvedSignature(chamada)?.declaration;
    const textoDaChamada = texto(chamada.expression);

    if (declaracao && tratarHttp(chamada, declaracao, textoDaChamada, pai, ramo, ctx)) return;
    if (declaracao) tratarAberturaDeModal(chamada, declaracao, pai, ramo, ctx);

    if (declaracao && ehDoProjeto(declaracao.getSourceFile())) {
      const container = declaracao.parent;
      const semCorpo =
        ts.isMethodSignature(declaracao) ||
        (ts.isMethodDeclaration(declaracao) && !declaracao.body &&
          ts.isClassLike(container) && container.modifiers?.some((m) => m.kind === ts.SyntaxKind.AbstractKeyword));

      if (semCorpo && (ts.isInterfaceDeclaration(container) || ts.isClassDeclaration(container))) {
        const nome = textoDoNome(declaracao.name);
        const implementacoes = implementacoesDe(container, nome);
        if (implementacoes.length === 1) {
          const alvo = percorrerMetodo(implementacoes[0], ctx.nivel + 1, false);
          grafo.addAresta(pai, alvo, ramo);
        } else if (implementacoes.length === 0) {
          adicionarNaoResolvido(chamada, UnresolvedReason.NoImplementation, textoDaChamada, `${container.name.text}.${nome}`, pai, ramo);
        } else {
          const candidatos = implementacoes.map((i) => i.label.split('.')[0]).join(', ');
          adicionarNaoResolvido(chamada, UnresolvedReason.MultipleImplementations, textoDaChamada, candidatos, pai, ramo);
        }
      } else {
        const desc = descritorDe(declaracao);
        if (desc) {
          const alvo = percorrerMetodo(desc, ctx.nivel + 1, false);
          grafo.addAresta(pai, alvo, ramo);
        }
      }
    }

    // Funções passadas como argumento (subscribe, pipe, then...) rodam no lugar, em sequência.
    for (const argumento of chamada.arguments) percorrerArgumento(argumento, pai, ramo, ctx);
  }

  function percorrerArgumento(argumento, pai, ramo, ctx) {
    if (ts.isArrowFunction(argumento) || ts.isFunctionExpression(argumento)) {
      percorrerCorpoDeFuncao(argumento, pai, ramo, ctx);
    } else if (ts.isObjectLiteralExpression(argumento)) {
      // subscribe({ next: () => ..., error: (e) => ... }): cada função do objeto, na ordem escrita.
      for (const prop of argumento.properties) {
        if (ts.isPropertyAssignment(prop) && (ts.isArrowFunction(prop.initializer) || ts.isFunctionExpression(prop.initializer))) {
          percorrerCorpoDeFuncao(prop.initializer, pai, ramo, ctx);
        } else if (ts.isMethodDeclaration(prop)) {
          percorrerCorpoDeFuncao(prop, pai, ramo, ctx);
        }
      }
    }
  }

  // O return de dentro de um callback não é saída do método: o corpo entra como se não estivesse aninhado.
  function percorrerCorpoDeFuncao(funcao, pai, ramo, ctx) {
    if (!funcao.body) return;
    if (ts.isBlock(funcao.body)) percorrerComando(funcao.body, pai, ramo, false, ctx);
    else percorrerExpressao(funcao.body, pai, ramo, ctx);
  }

  function adicionarNaoResolvido(chamada, motivo, rotulo, detalhe, pai, ramo) {
    const p = posicao(chamada);
    const id = `u:${p.file}:${p.line}:${p.col}`;
    grafo.addNo({
      id,
      kind: NodeKind.Unresolved,
      label: truncar(rotulo),
      location: { file: p.file, line: p.line },
      reason: motivo,
      detail: truncar(detalhe),
    });
    grafo.addAresta(pai, id, ramo);
  }

  // ---------- modal (MatDialog e Dialog do CDK) ----------

  // Classe `nome` declarada no módulo `modulo` (ou no pacote do Angular em node_modules).
  function ehClasseDoModulo(declaracao, nome, modulo, pacote) {
    const dono = declaracao.parent;
    if (!dono || !(ts.isClassDeclaration(dono) || ts.isInterfaceDeclaration(dono))) return false;
    if (dono.name?.text !== nome) return false;
    if (pacote.test(dono.getSourceFile().fileName)) return true;
    for (let atual = dono.parent; atual; atual = atual.parent) {
      if (ts.isModuleDeclaration(atual) && ts.isStringLiteral(atual.name) && atual.name.text === modulo) return true;
    }
    return false;
  }

  function ehOpenDeDialogo(declaracao) {
    return (
      declaracao.name && textoDoNome(declaracao.name) === 'open' &&
      (ehClasseDoModulo(declaracao, 'MatDialog', MODULO_MAT_DIALOG, /[\\/]@angular[\\/]material[\\/]/) ||
        ehClasseDoModulo(declaracao, 'Dialog', MODULO_CDK_DIALOG, /[\\/]@angular[\\/]cdk[\\/]/))
    );
  }

  // `dialog.open(Componente)`: abre o galho do modal, com os métodos que o template do modal liga a
  // eventos. O vínculo vem da leitura do template (ação do usuário), então as arestas ficam como
  // inferidas. O `afterClosed()` que vem depois segue como continuação normal, no mesmo pai.
  function tratarAberturaDeModal(chamada, declaracao, pai, ramo, ctx) {
    if (!ehOpenDeDialogo(declaracao) || chamada.arguments.length === 0) return;

    const s = simbolo(chamada.arguments[0]);
    const classe = s?.declarations?.find((d) => ts.isClassDeclaration(d) && d.name && ehDoProjeto(d.getSourceFile()));
    if (!classe) return; // TemplateRef, variável ou serviço próprio: não dá para provar a classe.

    const p = posicao(classe.name);
    const idModal = `modal:${p.file}#${classe.name.text}`;
    const jaExiste = grafo.temNo(idModal);
    if (!jaExiste) {
      grafo.addNo({
        id: idModal,
        kind: NodeKind.Method,
        label: `abre o modal ${classe.name.text}`,
        symbol: classe.name.text,
        location: { file: p.file, line: p.line },
        project: projectName,
      });
    }
    grafo.addAresta(pai, idModal, ramo);
    if (jaExiste) return; // modal já aberto antes (ou que abre a si mesmo): não repete nem entra em laço.

    const nivelDoModal = ctx.nivel + 1;
    if (nivelDoModal >= depth) {
      const limite = `u:depth:${idModal}`;
      grafo.addNo({
        id: limite,
        kind: NodeKind.Unresolved,
        label: 'limite de profundidade',
        reason: UnresolvedReason.DepthLimit,
        detail: `MaxDepth = ${depth}`,
      });
      grafo.addAresta(idModal, limite, undefined);
      return;
    }

    const modelo = lerTemplate(classe);
    if (!modelo) return;
    for (const nome of acharMetodosDeEventos(modelo.texto)) {
      const desc = metodoNaClasse(classe, nome);
      if (!desc) continue;
      const alvo = percorrerMetodo(desc, nivelDoModal + 1, false);
      grafo.addAresta(idModal, alvo, ROTULO_ACAO_NO_MODAL, undefined, undefined, EdgeConfidence.Inferred);
    }
  }

  // ---------- HttpClient ----------

  function ehHttpClient(declaracao) {
    const dono = declaracao.parent;
    if (!dono || !(ts.isClassDeclaration(dono) || ts.isInterfaceDeclaration(dono))) return false;
    if (dono.name?.text !== NOME_HTTP_CLIENT) return false;
    if (/[\\/]@angular[\\/]common[\\/]/.test(dono.getSourceFile().fileName)) return true;
    for (let atual = dono.parent; atual; atual = atual.parent) {
      if (ts.isModuleDeclaration(atual) && ts.isStringLiteral(atual.name) && atual.name.text === MODULO_HTTP) return true;
    }
    return false;
  }

  function tratarHttp(chamada, declaracao, textoDaChamada, pai, ramo, ctx) {
    if (!ehHttpClient(declaracao) || !ts.isPropertyAccessExpression(chamada.expression)) return false;

    const nome = chamada.expression.name.text;
    const argumentos = chamada.arguments;
    let metodo;
    let rota = null;

    if (VERBOS_HTTP.has(nome)) {
      metodo = VERBOS_HTTP.get(nome);
      rota = argumentos.length > 0 ? resolverRota(argumentos[0]) : null;
    } else if (nome === 'request') {
      const verbo = argumentos[0] && ts.isStringLiteralLike(argumentos[0]) ? argumentos[0].text.toUpperCase() : null;
      if (verbo && VERBOS_ACEITOS.has(verbo)) {
        metodo = verbo;
        rota = argumentos.length > 1 ? resolverRota(argumentos[1]) : null;
      } else {
        metodo = VERBO_DESCONHECIDO;
      }
    } else {
      return false;
    }

    const p = posicao(chamada);
    const id = `h:${p.file}:${p.line}:${p.col}`;
    const location = { file: p.file, line: p.line };
    const call = rota === null ? { method: metodo } : { method: metodo, route: rota };
    grafo.addNo({
      id,
      kind: NodeKind.Unresolved,
      label: truncar(`${metodo} ${rota ?? '?'}`),
      location,
      reason: rota === null ? UnresolvedReason.RouteNotResolved : UnresolvedReason.NoMatchingEndpoint,
      detail: truncar(textoDaChamada),
    });
    grafo.addAresta(pai, id, ramo, location, call);
    httpCalls.push({ nodeId: id, call, depth: ctx.nivel });
    return true;
  }

  // Rota da chamada: literal, template, concatenação ou const com literal. Pedaço não constante vira {};
  // os que aparecem antes do primeiro texto são o endereço base (vem de configuração) e saem.
  // Qualquer outra forma (variável, método) devolve nulo: a rota é montada em tempo de execução.
  function resolverRota(expressao) {
    const pedacos = [];
    if (!coletarRota(expressao, pedacos, false, 0)) return null;
    return rotaDosPedacos(pedacos);
  }

  function coletarRota(expressao, pedacos, aninhado, nivel) {
    if (nivel > 8) return false;

    let atual = expressao;
    while (ts.isParenthesizedExpression(atual) || ts.isAsExpression(atual) || ts.isNonNullExpression(atual) || ts.isSatisfiesExpression(atual)) {
      atual = atual.expression;
    }

    if (ts.isStringLiteralLike(atual)) {
      if (atual.text) pedacos.push({ texto: atual.text, buraco: false });
      return true;
    }
    if (ts.isNumericLiteral(atual)) {
      pedacos.push({ texto: atual.text, buraco: false });
      return true;
    }

    if (ts.isTemplateExpression(atual)) {
      if (atual.head.text) pedacos.push({ texto: atual.head.text, buraco: false });
      for (const trecho of atual.templateSpans) {
        coletarRota(trecho.expression, pedacos, true, nivel + 1);
        if (trecho.literal.text) pedacos.push({ texto: trecho.literal.text, buraco: false });
      }
      return true;
    }

    if (ts.isBinaryExpression(atual) && atual.operatorToken.kind === ts.SyntaxKind.PlusToken) {
      return coletarRota(atual.left, pedacos, true, nivel + 1) && coletarRota(atual.right, pedacos, true, nivel + 1);
    }

    if (ts.isIdentifier(atual)) {
      const decl = checker.getSymbolAtLocation(atual)?.valueDeclaration;
      if (decl && ts.isVariableDeclaration(decl) && decl.initializer &&
          ts.isVariableDeclarationList(decl.parent) && decl.parent.flags & ts.NodeFlags.Const) {
        return coletarRota(decl.initializer, pedacos, aninhado, nivel + 1);
      }
    }

    // Só dentro de uma concatenação ou de um template um pedaço qualquer vira buraco; sozinho, a rota é desconhecida.
    if (!aninhado) return false;
    pedacos.push({ texto: '', buraco: true });
    return true;
  }

  // ---------- execução ----------

  const metodoDaEntrada = metodoNaClasse(classeDaEntrada, nomeMetodo);
  const guarda = guardaDoTemplate(classeDaEntrada, nomeMetodo);
  percorrerMetodo(metodoDaEntrada, 0, true, guarda);

  return { graph: grafo.build(entry, metodoDaEntrada.id), httpCalls };
}
