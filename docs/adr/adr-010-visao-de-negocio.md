# ADR-010: visão de negócio, quadro de fluxos e exportação para o draw.io

**Data:** 2026-11-25
**Status:** Accepted
**Contexto:** Proposta em [`proposta-visao-negocio.md`](../arquitetura/proposta-visao-negocio.md); fases 54 a 61 do [`plano-mvp.md`](../arquitetura/plano-mvp.md).

---

## Contexto

O Rastro segue um fluxo do clique na tela até a última chamada externa, mas o resultado serve a quem programa: um cancelamento real chega a 165 passos, com nomes de método nos rótulos. Quem precisa da regra de negócio redesenha o fluxo à mão no draw.io, copiando arquivo e linha de cada balão, e o desenho desatualiza no primeiro commit.

A proposta pede uma visão de negócio (poucos passos, cor por tipo, referência ao código no balão), vários fluxos lado a lado num quadro, uma tela de desenho livre e a exportação para `.drawio`. O dono pediu também, em 2026-11-25, que o grafo e o Mapa por sistemas que já existem saiam em `.drawio`.

Três escolhas mudam o desenho: onde mora a derivação, que biblioteca desenha a tela e onde nasce o arquivo exportado.

## Decisão

1. **Derivação no núcleo, sem IA.** `BusinessFlowBuilder.Derive` fica em `Rastro.Core`, como o `FlowMapBuilder`: função pura e determinística (o mesmo grafo e as mesmas opções dão a mesma visão). A IA e o agente só propõem o texto de negócio e a troca de tipo, e confirmar é sempre do usuário (ADR-001).
2. **Tela com o Foblex Flow, que o Rastro já usa.** A versão instalada (19.4, MIT) já traz o que a tela livre pede: pontos de dobra editáveis com evento de mudança (`f-connection-waypoints`), ligação ortogonal (`SEGMENT`), encaixe na grade (`fCellSize`), zoom com gatilho configurável na roda (`fWheelTrigger`, para exigir Ctrl ou Cmd), pinça e `fitToScreen`.
3. **Layout automático próprio, em camadas, no frontend.** Mesmo caminho do mapa (`flow-map.ts`) e da linha do tempo da Fase 21: camada pela distância da entrada, ordem dos ramos pela ordem das arestas e coordenadas em grade. Com no máximo 25 passos por faixa, o custo é pequeno. Depois do layout automático, vale a posição salva.
4. **Exportação feita na tela, como o Mermaid e o SVG da Fase 12.** O arquivo sai com as posições que a pessoa está vendo, inclusive as arrastadas. No `.drawio`, as ligações levam `edgeStyle=orthogonalEdgeStyle`, e o próprio draw.io as roteia; os pontos de dobra salvos vão em `<Array as="points">`. Não há rota de exportação na API: o agente lê os dados pelo MCP (`get_business_view`).
5. **Cores por tipo como tokens de cada paleta,** com par claro e escuro. O verde volta a valer (decisão do dono em 2026-11-25: basta respeitar os temas). O `.drawio` usa as cores padrão do draw.io, para o arquivo sair igual ao que se desenha à mão.

### Alternativas descartadas

| Opção | Por que não |
|---|---|
| elkjs (layout em camadas com roteamento ortogonal) | Licença EPL-2.0, cerca de 1,4 MB, e o Rastro já o tirou na Fase 21. Só rodaria na tela, então o arquivo exportado por outro caminho sairia diferente. Fica como plano B, num Web Worker carregado sob demanda, se o layout próprio não passar no aceite da fixture. |
| maxGraph (sucessor do mxGraph em TypeScript) | Ainda está na versão 0.x, não é feito para Angular e seria uma segunda biblioteca de tela ao lado do Foblex. |
| Mermaid | Gera uma imagem pronta: não deixa mover balão nem guardar posição. |
| dagre | Não faz roteamento ortogonal; as setas atravessam as caixas. |
| Superfície própria em HTML e SVG | Repetiria em código próprio o que o Foblex já faz (pan, zoom, toque, arrastar). |
| Exportação pela API | Exigiria um segundo layout em C#, e a tela e o arquivo poderiam divergir. |

## Consequências

- Nenhuma dependência nova no pacote nem na máquina de quem instala.
- A qualidade do layout automático é nossa: ligação cruzando caixa se corrige com ponto de dobra, com "Reorganizar" ou, no último caso, com o plano B do elkjs.
- O `.drawio` sai da tela; quem automatiza pela CLI ou pelo agente recebe os dados, não o arquivo.
- As tabelas novas (`boards`, `board_lanes`, `business_labels`, `business_kind_overrides` e o layout) entram por migration no `~/.rastro/rastro.db`, e o README e o `SECURITY.md` mudam no mesmo PR da fase que as cria.
