# ADR-002: Rastro como produto distribuído por `dotnet tool`, com SQLite e licença

**Data:** 2026-10-16
**Status:** Accepted; os itens 3 (licença) e 4 (ofuscação) foram revogados pela [ADR-004](adr-004-codigo-aberto.md)
**Contexto:** Troca a decisão "ferramenta interna, não produto" da [`visao.md`](../arquitetura/visao.md) e define como o Rastro chega ao cliente.

---

## Contexto

O Rastro nasceu como ferramenta interna porque não havia um jeito simples de comercializá-lo. O que existe hoje para rodar:
- Docker Compose com Postgres, API e nginx;
- um executor no Windows (`Rastro.Runner`), para o build e os testes rodarem fora do container;
- user-secrets para os repositórios e o MCP por `dotnet run --project`.

Isso serve ao dono, mas não a um cliente: Docker na máquina do dev é atrito, e o código-fonte do cliente não pode sair da máquina dele, o que descarta um site hospedado.

O público é dev .NET e todo dev .NET já tem o SDK instalado. O próprio Rastro precisa do SDK para carregar a solution com o MSBuild.

## Decisão

1. **Distribuição como `dotnet tool`** (pacote NuGet `rastro-ai`, comando `rastro`). O cliente instala com `dotnet tool install -g rastro-ai`, sem Docker. Um executável só faz tudo:
   - `rastro` sobe a API com a tela embutida, só em `127.0.0.1`;
   - `rastro mcp` é o servidor MCP (stdio);
   - `rastro repo` cadastra os repositórios;
   - `rastro licenca` cuida da licença.
2. **SQLite no lugar do Postgres.** Os dados ficam num arquivo na pasta do usuário (`~/.rastro/rastro.db`, ou `RASTRO_HOME`), junto com a configuração dos repositórios (`~/.rastro/config.json`). O grafo continua como JSON numa coluna de texto: o código nunca consultou dentro do `jsonb`.
3. **Licença por chave assinada.**
   - **Chave:** ECDsa P-256. A chave privada fica só com o dono, fora do repositório; o programa traz apenas a chave pública e confere a assinatura sem internet. Ninguém gera chave falsa; quem quiser piratear precisa alterar o programa.
   - **Avaliação:** 14 dias.
   - **Ambientes:** a checagem vale no build de release (o pacote) e fica de fora no build de desenvolvimento e nos testes.
4. **Ofuscação com o Obfuscar** (gratuito, MIT) nos assemblies do Rastro dentro do pacote. A API pública fica com os nomes, porque ASP.NET, EF Core e JSON dependem deles. O que é interno, a licença inclusive, é renomeado.
5. **O executor no Windows é aposentado, e o Docker deixa de ser o caminho principal.** Com o Rastro rodando na própria máquina, o build e os testes da validação rodam direto, e o motivo do executor deixa de existir. O Docker continua opcional, para quem quer o Rastro num servidor: um container só (`docker/`), com o SQLite num volume.

## Consequências

- O cliente precisa do SDK do .NET 10. É o mesmo requisito de antes, porque o MSBuild vem dele.
- **Pirataria é aceita como custo.** Código .NET se descompila; a ofuscação e a assinatura só encarecem a cópia. Quem paga é a empresa, que não quer risco jurídico.
- As migrations do Postgres são substituídas por migrations novas do SQLite. Os dados do dono são copiados uma vez do Postgres para o SQLite.
- **SQLite tem um escritor por vez.** A API e o MCP podem abrir o mesmo arquivo; o modo WAL e a espera de bloqueio resolvem o uso de uma pessoa.
- **Fica para depois:**
  - a página de venda (pt, en e es), a identidade visual do produto e o pagamento que emite a licença sozinho;
  - o plano enterprise, com a IA usando os tokens contratados pela empresa.
