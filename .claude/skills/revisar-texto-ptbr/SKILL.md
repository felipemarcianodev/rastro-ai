---
name: revisar-texto-ptbr
description: Revisa ortografia, gramática, pontuação e acentuação em português do Brasil de todo texto que a IA escreveu ou alterou (documentação, README, ADR, plano, skill, texto de tela, template de e-mail, mensagem de commit) e corrige no próprio arquivo. Use SEMPRE depois de escrever ou revisar texto em pt-BR, quando o hook revisao-ptbr pedir a revisão no fim da sessão, e quando o usuário pedir para revisar, corrigir ou conferir o português de um texto.
---

# Revisão de texto em português do Brasil

Texto escrito não é texto pronto. Todo texto que a IA produz neste repositório, seja criação do zero, seja revisão de um texto existente, passa por esta revisão antes de a tarefa ser dada por concluída.

## Como a governança funciona

| Camada | O que faz | Quando age |
|---|---|---|
| `CLAUDE.md` (seção "Governança de texto") | a norma | toda sessão |
| esta skill | o método da revisão | ao terminar de escrever texto |
| `.claude/hooks/revisao-ptbr.mjs pos-edicao` | aponta na hora palavra sem acento, cedilha ou til em `.md`, `.mdx`, `.txt`, `.adoc` e `.rst` | depois de cada Edit/Write |
| `.claude/hooks/revisao-ptbr.mjs commit` | recusa `git commit` com palavra sem acento na mensagem | antes do commit |
| `.claude/hooks/revisao-ptbr.mjs parada` | bloqueia o encerramento uma vez e pede esta revisão nos textos tocados na sessão (prosa e texto visível de template `.html`, `.ftl`, `.hbs`) | no fim do turno |

A varredura do hook só pega o mecânico (palavra conhecida sem acento). Gramática, pontuação, crase, concordância e regência dependem de leitura: é esta skill.

## Passo a passo

1. **Delimite o trecho.** Revise só o que foi escrito ou alterado: `git diff HEAD -- <arquivo>` (arquivo novo: inteiro). Em template de tela, só o texto visível. Ao editar um documento antigo sem acentos, corrija também o trecho tocado.
2. **Leia o trecho inteiro uma vez**, como leitor, antes de corrigir qualquer coisa.
3. **Passe pela lista de conferência abaixo**, na ordem.
4. **Corrija direto no arquivo**, com Edit, sem mudar o sentido, o tom nem a estrutura. Revisão de língua não é reescrita: não troque palavra correta por sinônimo de gosto pessoal.
5. **Feche com uma linha** dizendo o que foi corrigido (ex.: "Revisão pt-BR: 3 acentos, 1 crase e 2 vírgulas em docs/x.md") ou que não havia o que corrigir.

## Lista de conferência

### Acentuação (Acordo Ortográfico de 1990, em vigor desde 2009)

- Oxítonas em -a, -e, -o, -em, -ens: está, você, após, também, parabéns.
- Paroxítonas em -l, -n, -r, -x, -ps, -ão, -ãos, -ei, -um, -us, -i, -is e ditongo crescente: disponível, útil, hífen, caráter, órfão, vírus, série, histórico, área, código, número, último.
- Proparoxítonas, todas: técnico, lógica, método, período, índice, página, título.
- Til e cedilha: versão, padrões, configuração, serviço, segurança, começo.
- Sem acento pelo acordo: ideia, assembleia, heroico, voo, leem, creem, para (verbo), pelo, polo.
- Acento diferencial que continua: pôde (passado) e pode, pôr (verbo) e por, têm e vêm (plural).
- Pares que só a leitura resolve: está/esta, é/e, público/publica, análise/analise, prática/pratica, crítica/critica, específica/especifica, média/media, contém/contêm.

### Crase

- Antes de palavra feminina que admite artigo, quando o termo regente pede "a": "acesso à API", "volta à tela inicial", "às 10h".
- Sem crase antes de verbo, de palavra masculina, de pronome pessoal e em "a partir de", "a respeito de", "a ela".
- Locuções femininas: à vista, às vezes, à medida que, à direita.

### Gramática

- Concordância verbal e nominal, inclusive com sujeito posposto ("faltam dois testes", "foram feitas as alterações").
- Regência: "implica a mudança" (não "implica na"), "visar a", "assistir a", "preferir X a Y".
- "Haver" no sentido de existir não vai ao plural ("houve erros"); "fazer" de tempo também não ("faz dois dias").
- Colocação pronominal no padrão brasileiro escrito: sem pronome átono iniciando frase ("Retorna-se", não "Se retorna").
- Porque, por que, porquê, por quê; mas e mais; mal e mau; onde (lugar) e em que.

### Pontuação

- Sem vírgula entre sujeito e verbo nem entre verbo e complemento.
- Vírgula no aposto, no vocativo, no adjunto adverbial deslocado longo e antes de "mas", "porém", "pois" (explicativo).
- Oração adjetiva explicativa entre vírgulas; restritiva, sem.
- Dois-pontos para enumeração e explicação; ponto e vírgula para itens longos que já têm vírgula.
- Itens de lista: padrão único no mesmo bloco (todos com ponto final ou nenhum).
- Respeite a regra de escrita do repositório sobre travessão quando houver uma no `CLAUDE.md`.

### Ortografia

- Hífen pelo acordo: autoescola, antissocial, microsserviço, contrarregra, semiaberto; com hífen quando o prefixo termina na mesma vogal que inicia a palavra (micro-ondas, anti-inflamatório) e com "pós", "pré", "pró", "ex", "vice".
- Grafias frequentes em texto técnico: excesso, exceção, aceitação, privilégio, beneficente, empecilho, impressão, ascensão, mexer, enxergar, paralisar, análise, pesquisar.
- Estrangeirismo técnico sem tradução consagrada fica como está (commit, build, endpoint, deploy); não aportuguesar à força.

## O que não se toca

- Código, blocos e trechos entre crases, identificadores, nomes de arquivo, caminhos, URLs, chaves de configuração, placeholders (`{{NOME}}`).
- Nome próprio, de produto e de tecnologia na grafia original.
- Citação literal (mensagem de erro, saída de ferramenta, texto de terceiro).
- Texto em outro idioma: segue a norma daquele idioma, e não é traduzido.
- Arquivo gerado por ferramenta (lock, build, saída de script).

## Falso positivo do hook

Se a varredura mecânica apontar palavra que está certa no contexto (nome próprio, identificador citado sem crase, termo de outro idioma), acrescente a palavra, em minúsculas, uma por linha, em `.claude/hooks/revisao-ptbr.permitidas.txt`. Prefira, quando for identificador, colocá-lo entre crases no texto.
