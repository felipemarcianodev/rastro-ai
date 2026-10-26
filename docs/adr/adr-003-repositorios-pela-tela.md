# ADR-003: repositórios cadastrados pela tela, com a API protegida contra pedido de outro site

**Data:** 2026-10-17
**Status:** Accepted
**Contexto:** Complementa a [ADR-002](adr-002-produto-dotnet-tool.md). Muda a regra "a requisição cita o nome do repositório, nunca um caminho" só para o cadastro de repositórios.

---

## Contexto

Com o Rastro como produto, a tela de Configurações mostrava o que só servia ao dono:
- o repositório só se cadastrava pelo terminal (`rastro repo add`);
- o "Abrir no VS Code" pedia, de novo e no navegador, a pasta que o Rastro já tinha no `config.json`, porque a API não devolvia caminho de disco;
- o placeholder e a mensagem de erro citavam o repositório do dono.

Para um cliente, cadastrar pela tela é o esperado. Só que, para isso, a requisição passa a levar um caminho, e a API, que escuta em `127.0.0.1`, fica exposta a um site qualquer aberto no navegador: uma página pode mandar um POST para `http://127.0.0.1:7450` sem o usuário saber (CSRF), ou apontar um domínio dela para `127.0.0.1` e ler as respostas (DNS rebinding). Esse risco já existia para as rotas que mudam dado, como a validação, que roda build e testes.

## Decisão

1. **Cadastro pela tela.** A API ganha `POST api/repositories` (nome e pasta) e `DELETE api/repositories/{nome}`, e a lista passa a devolver a pasta de cada repositório. Valem as regras do `rastro repo add`: nome com letras, números, ponto, hífen e sublinhado; pasta absoluta e existente; nome sem repetir.
2. **Comando só pelo terminal.** O comando de build e o de testes da validação não entram pela tela: a regra "o comando vem só da configuração, nunca da requisição" continua. Remover pela tela tira a entrada inteira, comandos inclusive.
3. **Um lugar só grava o `config.json`:** um serviço da infraestrutura, usado pela CLI e pela API.
4. **A pasta volta para a tela.** O "Abrir no VS Code" usa a pasta cadastrada. A API só escuta na máquina do usuário e a pasta é dele; a regra que protege é a da entrada (caminho de solution e de arquivo continuam relativos e canonicalizados pelo `RepositoryCatalog`).
5. **Proteção da API, em todas as rotas:**
   - `AllowedHosts` só com `localhost`, `127.0.0.1` e `[::1]`: pedido com outro `Host` é recusado, o que fecha o DNS rebinding. No Docker, quem publica em outro nome configura o `AllowedHosts`.
   - Pedido que muda dado (POST, PUT, PATCH, DELETE) com `Origin` de fora da máquina é recusado com 403. Sem `Origin` (curl, testes, o próprio MCP) passa, porque não vem de navegador.

## Consequências

- O cliente cadastra e remove repositórios sem abrir o terminal; o terminal continua valendo para os comandos da validação.
- O `config.json` entra por último na configuração, e a lista se mescla por posição: o primeiro repositório do `config.json` se sobrepõe ao primeiro do appsettings ou das variáveis de ambiente. No Docker, onde o compose cadastra por `Rastro__Repositories__0`, cadastrar pela tela pode se misturar com eles; lá, cadastre por um lado só.
- No Docker, a pasta cadastrada é a do container, e o "Abrir no VS Code" leva a um caminho que não existe no Windows. O Docker é opcional e para servidor, onde abrir o editor não se aplica.
- A pasta digitada no navegador (`localStorage`) deixa de ser usada.
- Um site aberto no navegador deixa de conseguir disparar levantamento, validação ou cadastro no Rastro local.
