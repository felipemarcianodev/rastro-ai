# ADR-004: código aberto, licença MIT

**Data:** 2026-10-26
**Status:** Accepted
**Contexto:** Revoga os itens 3 (licença por chave) e 4 (ofuscação) da [ADR-002](adr-002-produto-dotnet-tool.md). O resto da ADR-002 continua: `dotnet tool`, SQLite e Docker opcional.

---

## Contexto

A ADR-002 previa vender o Rastro: licença por chave assinada, 14 dias de avaliação e ofuscação para encarecer a cópia. O dono decidiu abrir o código e colocar o Rastro no portfólio, no mesmo molde do Mirante: repositório público no GitHub e pacote no NuGet.

Com o código aberto, a licença e a ofuscação perdem o sentido. Qualquer pessoa recompila sem a checagem, e a ofuscação esconde um código que está publicado ao lado.

## Decisão

1. **Licença MIT.** O código e o pacote `rastro-ai` são gratuitos, para uso pessoal e comercial. O arquivo `LICENSE` fica na raiz, e o pacote declara `MIT`.
2. **Sai a licença do programa.** Somem o comando `rastro licenca`, a checagem ao subir a tela e o MCP, os projetos `Rastro.Licensing` e `Rastro.Licensing.Issuer`, e os termos de uso do pacote.
3. **Sai a ofuscação.** O `scripts/empacotar.ps1` faz só o build do front e o `dotnet pack`, sem o Obfuscar e sem o mapa.
4. **Repositório público no GitHub** (`felipemarcianodev/rastro-ai`), com histórico novo: um commit inicial, sem nada privado. O histórico anterior fica num repositório privado, só para consulta.

## Consequências

- **A venda deixa de existir** como estava na Fase 15 (página de venda, preço e emissão automática de licença). O que sobra dela é a página do projeto: o card no portfólio e o README.
- **O plano enterprise** (Fase 16) é revisto: a IA com os tokens da empresa continua possível, sem licença por assentos.
- **Nada privado no repositório.** Testes, exemplos, prints e documentos usam nomes genéricos ou projetos públicos. Nome de cliente não vai nem numa lista de termos proibidos: a lista fica fora do repositório.
- **Contribuição de fora** passa a ser possível. A `main` do GitHub só aceita pull request, e um workflow confere o build e os testes em cada PR.
- A chave privada em `~/.rastro-emissor/` deixa de ser usada. Apagar ou guardar é decisão do dono.
