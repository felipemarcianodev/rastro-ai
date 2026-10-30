# ADR-005: segunda linguagem no extrator, o Angular, por um processo Node com o compilador do TypeScript

**Data:** 2026-10-30
**Status:** Accepted
**Contexto:** Fase 26 do [plano](../arquitetura/plano-mvp.md). Revoga a decisão "Só .NET no início" da [`visao.md`](../arquitetura/visao.md) e tira "Linguagens além de C#" e "Leitura do frontend" do "Fora de escopo" do plano.

---

## Contexto

A Fase 25 liga num levantamento só as APIs .NET que se chamam por HTTP. O fluxo que o dono quer ver começa antes: no clique da tela, num componente Angular, que chama um service, que chama o `HttpClient`, que chega ao controller. Sem ler o Angular, o primeiro passo do fluxo fica de fora, e as bifurcações da tela (o botão bloqueado, o `@if` que esconde a ação) também.

Ler TypeScript bem exige o que o Roslyn dá ao C#: tipos resolvidos, a injeção pelo construtor e pelo `inject()`, o método certo atrás de `this.pedidosService.cancelar()`. Só o compilador do TypeScript faz isso.

## Decisão

1. **Um processo Node com o compilador do TypeScript.** O extrator Angular é um script (`extractor-ts/extract.mjs`) que roda no Node, usa a API do `typescript` (programa, `TypeChecker`) e devolve na saída padrão o mesmo `FlowGraph` do extrator C#, em JSON, com as chamadas HTTP encontradas. A API chama o script pelo `ProcessRunner`: argumentos separados, sem shell e com timeout, como todo processo externo.
2. **O `typescript` vem do próprio repositório analisado.** Todo projeto Angular já tem o `typescript` em `node_modules` (o build dele precisa). O script resolve o pacote a partir da raiz do repositório; sem ele, o levantamento falha com a mensagem "rode o npm install no repositório". Assim o pacote do Rastro não carrega o compilador (cerca de 20 MB), e a versão do TypeScript é a mesma que o projeto usa.
3. **Opcional, ligado só quando há Node.** A API confere o `node --version` ao subir. Sem Node no PATH, a tela não oferece o Angular (nenhuma opção morta) e a API recusa o levantamento de um `tsconfig` com um código de erro próprio.
4. **O levantamento Angular parte de um `tsconfig`.** No lugar da solution, o caminho relativo de um `tsconfig.json` ou `tsconfig.app.json`, validado pelo `RepositoryCatalog` como os outros caminhos. O ponto de entrada é `Componente.metodo` (ex.: `DetalhePedidoComponent.cancelar`).
5. **A costura é a da Fase 25.** As chamadas do `HttpClient` saem como chamadas HTTP com verbo e rota, e o mesmo índice de rotas liga a chamada ao controller .NET. O trecho Angular é o primeiro sistema do fluxo.

## Alternativa recusada

- **Parser próprio em C#** (ler o `.ts` como texto ou por uma gramática): não resolve tipos nem injeção, então `this.pedidosService.cancelar()` não chega ao método certo, e um service atrás de uma classe abstrata vira palpite. Errar em silêncio é o que o Rastro existe para evitar (ver a decisão "extração determinística" da `visao.md`).
- **Levar o `typescript` dentro do pacote do Rastro:** pesa no `dotnet tool` e pode divergir da versão do projeto.

## Consequências

- O Rastro passa a ter um segundo extrator, em JavaScript, com testes próprios (`node --test`) e um job no workflow de PR.
- O resultado do Angular depende do `npm install` feito no repositório analisado.
- O que só existe em tempo de execução (provider trocado por configuração, URL montada fora do código) vira nó não resolvido com o motivo, como no C#.
- As regras de IA plugável não mudam: o extrator Angular é determinístico e não usa IA.
