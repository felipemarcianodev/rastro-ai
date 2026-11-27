# ADR-011: draw.io dentro do Rastro

**Data:** 2026-11-27
**Status:** Accepted
**Contexto:** Pedido do dono em 2026-11-27; Fases 62 a 68 do [`plano-mvp.md`](../arquitetura/plano-mvp.md).

---

## Contexto

Desde a Fase 54, o Rastro exporta o grafo, o mapa por sistemas, a visão de negócio e o quadro de fluxos para `.drawio`. Para ver ou mexer no desenho, a pessoa sai do Rastro, abre o arquivo no draw.io e perde a ligação com o código e com o que o Rastro grava (posição, texto e tipo do passo). O dono pediu o draw.io dentro do Rastro: ver o fluxo, mover, editar e criar no próprio draw.io.

O editor do draw.io (jgraph/drawio, licença Apache-2.0) é uma aplicação web inteira, que uma página abre num `iframe` em modo `embed` e com a qual conversa por mensagens (`postMessage`, com `proto=json`): a página manda o XML (`load`), e o editor devolve cada mudança (`autosave`). O Rastro roda na máquina de quem instala e não manda o código para fora ([ADR-002](adr-002-produto-dotnet-tool.md)), então importa de onde o editor carrega.

## Decisão

### 1. O draw.io vem dentro do pacote, servido pela API em 127.0.0.1

Opções avaliadas:
- **Do site diagrams.net** (`embed.diagrams.net`): nada a mais no pacote, mas precisa de internet e põe um script de terceiro numa página que mostra o código do cliente. Descartada pela ADR-002.
- **Dentro do pacote**: funciona sem rede e nada sai da máquina. O pacote cresce.
- **Baixado uma vez para `~/.rastro`**: o pacote não cresce, mas o primeiro uso precisa de rede, e a origem do arquivo vira um ponto a proteger na máquina de cada cliente.

Escolhida a segunda: o draw.io fica dentro do pacote e a API o serve em `/drawio/`, na mesma origem da tela.

- **Versão fixa:** v32.6.10 (2026-11-04), o `draw.war` das releases oficiais (`github.com/jgraph/drawio`), com o SHA-256 `a57b06b3b6a876a351dacfe65d027d9509319cd030e9bacf6ae04a034d66a38f` conferido. Uma versão com algumas semanas de uso, e não a mais nova. Subir a versão é um PR com o hash novo e o teste de que o editor abre sem rede.
- **Fora do repositório:** os arquivos do draw.io não entram no git. O repositório guarda a versão, o hash e a lista do que entra; um script baixa, confere o hash e extrai só essa lista na hora de empacotar (no `scripts/empacotar.ps1` e no workflow de release). Sem o draw.io extraído (como no CI de PR e em quem roda a API do código-fonte sem o script), a API responde que ele não está disponível e a tela não mostra o desenho draw.io.
- **Só o necessário:** o editor, as formas, os ícones, os estilos e os textos em inglês, português e espanhol. Ficam de fora o que serve só ao servidor Java (`WEB-INF`), o código-fonte sem minificar, o visualizador, as integrações com nuvem e os modelos.
- **Modo isolado:** o editor abre com `embed=1`, `proto=json`, `stealth=1` e `offline=1`, e a API manda, para as páginas do draw.io, um cabeçalho de segurança de conteúdo que só permite a própria origem.

### 2. O draw.io é um desenho a mais, e não o substituto dos atuais

O grafo, o mapa por sistemas, a visão de negócio e o Mermaid continuam no Foblex e no SVG próprios, que têm teclado, leitor de tela, busca, trilha, recorte e o painel do nó. O draw.io entra como mais um botão na barra de desenhos (Fase 63), e não como o padrão: ele é forte para mover e anotar, mas fraco em acessibilidade.

### 3. O que a edição no draw.io pode mudar

O código continua sendo a fonte da verdade:
- **mover, redimensionar e dobrar ligações** vale sempre e grava a posição no mesmo lugar que o desenho de origem usa;
- **texto e tipo de um passo** da visão de negócio vão pelas rotas da Fase 59, como texto do usuário;
- **caixa, seta e texto livres**, sem ligação com um passo, viram anotações do desenho, guardadas à parte, que nunca entram no fluxo, no prompt nem na IA;
- **criar passo, criar ligação, apagar ou mover passo entre ramos** só no modo "Editar fluxo", e viram o mesmo rascunho que a edição de hoje gera, com o mesmo prompt e a mesma validação. Nenhuma ligação desenhada vira `extracted`.

Cada balão e cada ligação do `.drawio` levam o id do Rastro como atributo (`rastroId`) do `UserObject`; o que volta pelo `autosave` se compara com o que foi, pelo id.

## Prova de conceito (Fase 62)

Feita fora do produto, com o `draw.war` v32.6.10 servido localmente e aberto num `iframe`, no Chrome, com toda requisição para fora de `127.0.0.1` bloqueada:
- o editor abre em cerca de 1,5 s, recebe o XML pelo `load` e devolve o `autosave` depois de um arraste feito com o mouse, com o `rastroId` preservado e a posição nova;
- nenhuma requisição para fora da máquina, nem ao abrir menus, buscar formas, abrir o painel de estilo, trocar para o tema escuro ou para o espanhol; nenhum arquivo faltando no conjunto escolhido;
- tamanho: a aplicação inteira tem 123 MB aberta (47 MB compactada); o conjunto escolhido tem 47 MB aberto e 18 MB compactado, então o pacote passa de cerca de 35 MB para cerca de 53 MB.

Não testado na prova: a janela Photino (WebView2) e o WebKit do macOS e do Linux; ficam para a Fase 64.

## Consequências

- O pacote cresce cerca de 18 MB.
- O README, o README do pacote e o `SECURITY.md` dizem o que vem no pacote e que o draw.io roda sem rede.
- O `NOTICE` do draw.io (Apache-2.0) vai junto, com os créditos no README.
- Atualizar o draw.io é decisão consciente, com hash e teste, e não acontece sozinho.
- Quem roda a API pelo código-fonte precisa rodar o script do draw.io uma vez para ter o desenho draw.io.
