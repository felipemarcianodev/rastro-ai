# Design system do Rastro AI

> Decidido na Fase 3 (2026-10-15). Tokens em `frontend/src/theme/_tokens.scss`.

## Ideia

O Rastro mostra o rastro de uma funcionalidade como uma trilha num mapa de levantamento de campo. A interface é papel e tinta, quieta. **A única cor forte é o marcador da trilha (âmbar)**, que aparece só no caminho da entrada até o nó selecionado. Todo o resto apaga quando a trilha acende.

## Cor

| Token | Claro | Escuro | Uso |
|---|---|---|---|
| `--paper` | `#f2f4f1` | `#12191c` | fundo (cinza esverdeado frio, não creme) |
| `--surface` | `#ffffff` | `#1a2327` | barras, painel, nós |
| `--ink` | `#1d2a2e` | `#e3e9e6` | texto, botão primário, nó de entrada |
| `--ink-soft` | `#5c6a6e` | `#9aa8a4` | texto secundário |
| `--line` / `--line-strong` | `#d4dbd7` / `#b7c1bc` | `#2c383d` / `#3d4b51` | bordas, arestas extraídas |
| `--trail` | `#d99a00` | `#f0b429` | **só a trilha** e o foco do teclado |
| `--inferred` | `#6a4fb8` | `#a48ef0` | aresta inferida (tracejada) |
| `--confirmed` | `#0e7a6b` | `#3cc2ad` | aresta confirmada (mais grossa) |
| `--unresolved` | `#a25a00` | `#e39a3b` | nó não resolvido (borda pontilhada) |
| `--danger` | `#b42318` | `#f07167` | erro, exceção |

Nenhuma informação depende só da cor: confiança da aresta = cor **e** traço (sólido, tracejado, grosso); tipo do nó = cor **e** letra no selo (E, M, ?, ↻, T, ↩, !, ↗, …, ⤳).

### Paletas alternativas (seletor na barra)

Pedido do dono: escolher entre três paletas na própria tela, para decidir depois qual fica. A escolha vai em `data-palette` no `<html>` e no navegador.

| Paleta | Tinta | Fundo | Trilha | Inferida | Não resolvido |
|---|---|---|---|---|---|
| Papel (padrão) | `#1d2a2e` ardósia | `#f2f4f1` | `#d99a00` âmbar | `#6a4fb8` violeta | `#a25a00` âmbar escuro |
| Carta náutica | `#0f2438` azul-marinho | `#edf1f4` | `#f26b1d` laranja sinal | `#7a4fd6` violeta | `#b0306a` framboesa |
| Planta técnica | `#1a1f36` índigo | `#f5f6f8` | `#6c3bf5` violeta elétrico | `#0e8fb0` ciano | `#b5651d` cobre |

Cada paleta tem variante escura. Regra que vale para todas: a cor forte é só da trilha, e não resolvido e inferida nunca colidem com ela.

## Tipo

- **Instrument Sans** (400, 500, 600) para a interface.
- **JetBrains Mono** (400, 500) só para o que é código: símbolos, `arquivo:linha`, condições, trechos.
- Escala a partir de 14 px, razão ~1,2 (`--text-xs` 12 a `--text-2xl` 28). Sem caixa alta em rótulos.

## Componentes base (`_controls.scss`)

- `.btn-p` (um por tela), `.btn-g` (secundário), `.btn-icon` (só ícone, sempre com `aria-label`).
- `.field`, `.field-label`, `.field-help`, `.field-error`, `.input` (`.code` para campos de código).
- `.pill` (estado: `is-running`, `is-completed`, `is-failed`, `is-stale`), `.notice` (`is-error`, `is-warning`).

## Grafo (`_flow.scss`, Foblex Flow)

- Layout em camadas **da esquerda para a direita**: cada passo é uma coluna; irmãos empilhados na ordem de execução.
- Estilos do grafo são globais (o SVG das conexões não recebe estilo de componente) e sempre com escopo `.flow-host` / `.flow-node.kind-*`, para não vazar para o resto da tela.
- Tamanho do nó vai por `[fNodeSize]` (o Foblex reescreve o `style` do nó).
- Seleção chega pelo `(fSelectionChange)` do Foblex (ele consome o clique do mouse).

## Movimento

Um único movimento automático: a trilha tracejada "andando" enquanto a análise roda. Fora isso, só transições curtas que respondem a uma ação (seleção, foco). `prefers-reduced-motion` desliga tudo.

## Orçamento de CSS por componente

Aviso a partir de 6 kB e erro a partir de 10 kB por componente (o padrão do Angular é 4 kB). Decidido na Fase 6: o painel do nó é a área mais densa da ferramenta de desktop. Antes de subir de novo, mover para o tema o que for padrão repetido (como foi feito com `.kind`, `.conf`, `.btn-small` e `.steps`).
