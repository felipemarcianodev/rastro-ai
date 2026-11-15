---
name: traduzir-texto
description: Traduz texto do Rastro AI do português do Brasil para o inglês (en-US) e o espanhol (es) pelo glossário do projeto, e grava nos arquivos de tradução da tela (JSON), nos .resx do backend ou nos README traduzidos. Use SEMPRE que criar ou mudar texto de tela, mensagem da API, saída do MCP, texto da CLI ou documentação para quem usa (README, README do pacote, SECURITY.md), e quando o dono pedir para traduzir ou conferir uma tradução.
---

# Traduzir texto do Rastro

O Rastro existe em inglês, português e espanhol ([ADR-009](../../../docs/adr/adr-009-multilingue.md)). Texto novo nasce em português e entra nos três idiomas no mesmo PR.

## Antes de traduzir

1. Leia o [glossário](../../../docs/i18n/glossario.md). Termo do produto que não está lá entra no glossário primeiro, nos três idiomas.
2. Revise o português com a skill `revisar-texto-ptbr`. Tradução de texto errado espalha o erro por três arquivos.

## Onde o texto vive

| Texto | Arquivos | Chave |
|---|---|---|
| Tela | `frontend/src/i18n/pt-BR.json`, `en-US.json`, `es.json` | por área e por função: `settings.language.label`, `survey.status.running` |
| Rótulo de enum | os mesmos JSON | `enum.<nomeDoEnum>.<valor>`, lida pelo `Record` do enum |
| Erro da API | os mesmos JSON | `error.<CODIGO_DO_ERRO>` |
| Mensagem e prosa do servidor | `Resources/Messages.resx` (inglês), `Messages.pt-BR.resx`, `Messages.es.resx` do projeto que gera o texto | `Area_Situacao`, com parâmetros `{0}`, `{1}` |
| Documentação para quem usa | `README.md` (inglês), `README.pt-BR.md`, `README.es.md`; o mesmo para `SECURITY.md` | as mesmas seções nos três |

O arquivo neutro do `.resx` (`Messages.resx`) é o inglês, que é a reserva quando o idioma pedido não existe.

## Como traduzir

1. **Mesmo sentido, não palavra por palavra.** O texto de tela em inglês costuma ser mais curto; em espanhol, cerca de 25% mais longo que o inglês. Se o espanhol estourar o espaço do botão, encurte mantendo o verbo.
2. **Marcadores intactos.** `{count}`, `{name}`, `{0}` e a sintaxe ICU (`{count, plural, one {# step} other {# steps}}`) ficam iguais nos três; só o texto em volta muda. O inglês e o espanhol têm `one` e `other`.
3. **Estilo do glossário:** "you" no inglês, "tú" no espanhol; botão com verbo (imperativo no inglês, infinitivo no espanhol); só a primeira letra maiúscula.
4. **Nunca traduza** código, identificador, nome de produto, caminho ou valor técnico (`extracted`, `inferred`, `confirmed`).
5. **Mesma ordem de chaves** nos três JSON, para o diff do PR mostrar a tradução lado a lado.

## Conferir

- Frontend: `npm run test:ci` roda o teste de paridade (mesmas chaves e mesmos marcadores nos três JSON).
- Backend: `dotnet test` roda o teste de paridade dos `.resx`.
- Documentação: `node scripts/verificar-traducoes.mjs` (os três README com as mesmas seções).
- Termine dizendo, em uma linha, quantas chaves entraram ou mudaram e em quais arquivos.
