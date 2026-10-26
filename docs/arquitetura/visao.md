# Rastro AI: visão e decisões iniciais

> Decisões tomadas em 2026-10-15, na conversa de concepção.

## Problema

Ao refatorar um sistema várias vezes, falta a visão do todo de uma funcionalidade: por onde ela entra, que métodos percorre, onde bifurca e onde termina. O Rastro existe para dar essa visão e para transformar uma mudança desenhada visualmente num prompt cirúrgico para a IA.

## O que faz

1. **Levanta o fluxo** de uma funcionalidade a partir de um ponto de entrada (controller, job, handler), até o fim, com todas as bifurcações (`if`, `switch`, `catch`) e a implementação real atrás de cada interface do DI.
2. **Mostra o fluxo** como um grafo no front (Angular), com as regras de cada nó em linguagem humana.
3. **Permite editar** o fluxo com drag and drop: mover um passo, criar um método novo e descrever o que ele faz.
4. **Gera o prompt** a partir do diff entre o grafo original e o editado, com âncoras exatas (`arquivo:linha`, método, o que muda).
5. **Valida a alteração** feita pela IA: reextrai o grafo, compara com o desenhado e roda build e testes. A nota sai de critérios objetivos.
6. **Documenta** o fluxo aprovado.
7. **Sugere melhorias:** complexidade ciclomática, SOLID, clean code e dívida técnica, com métricas do Roslyn e sugestões da IA por cima.
8. **Exporta diagramas C4** (C1 a C4) em Structurizr DSL, na hora ou depois, com um clique, para abrir no Structurizr (o Structurizr Lite roda local em Docker, então o código não sai da máquina).

## Decisões

| Decisão | Por quê |
|---|---|
| **Extração determinística com Roslyn** (não a IA lendo o código inteiro) | A IA lendo tudo erra em silêncio e alucina caminhos. O compilador não erra. |
| **A IA completa só os buracos marcados pelo extrator** | Despacho dinâmico, eventos, reflection e LINQ deixam lacunas. O extrator marca o nó como "não resolvido", com o motivo, e a IA recebe só esse trecho. |
| **Toda aresta tem `confidence`:** `extracted` / `inferred` / `confirmed` | O usuário sabe em que confiar. A aresta inferida precisa citar `arquivo:linha` de origem e destino, e o sistema confere antes de desenhar. Sem prova, a aresta não entra. Confirmada pelo usuário, ela fica salva. |
| **O código é a fonte da verdade, o grafo é uma visão** | O grafo é regenerado a cada análise e nunca é mantido em paralelo. |
| **IA opcional e plugável** | O núcleo funciona sem IA. Com IA, dois modos: **agente** (o Claude Code ou outro agente usa o Rastro por MCP, sem chave de API) e **provedor** (Claude, OpenAI ou Gemini configurados numa tela, no padrão do `meu-portfolio`). Os dois passam pelo mesmo validador de prova. |
| **Persistência em SQLite** (até a Fase 13, PostgreSQL + JSONB; trocado na [ADR-002](../adr/adr-002-produto-dotnet-tool.md)) | Um levantamento = metadados (repositório, **hash do commit**, funcionalidade, data) + grafo em JSON. O hash diz se o levantamento ainda vale. Um arquivo na pasta do usuário, sem servidor de banco para instalar. |
| **Só .NET no início** | Multi-linguagem é outro projeto inteiro. |
| **Distribuído como `dotnet tool`** (até 2026-10-16, ferramenta interna; trocado na [ADR-002](../adr/adr-002-produto-dotnet-tool.md)) | O `dotnet tool` instala com um comando e roda na máquina de quem usa: o código analisado não sai dela. |
| **Código aberto, licença MIT** (desde 2026-10-26, [ADR-004](../adr/adr-004-codigo-aberto.md); antes, licença paga por chave) | O Rastro faz parte do portfólio do dono, como o Mirante: repositório público no GitHub e pacote gratuito no NuGet. |

## Distribuição

Decidida na [ADR-002](../adr/adr-002-produto-dotnet-tool.md) e revista na [ADR-004](../adr/adr-004-codigo-aberto.md):

1. **Agora:** código aberto no GitHub (MIT) e pacote `rastro-ai` gratuito no NuGet (`dotnet tool install -g rastro-ai`).
2. **Depois:** a IA com os tokens contratados pela empresa (endpoint próprio no modo provedor).
3. **SaaS:** fora de escopo, porque o código analisado nunca sai da máquina de quem usa.

## Estrutura e execução

O desenho de pastas e a ordem de construção estão no plano faseado: [`plano-mvp.md`](plano-mvp.md).
