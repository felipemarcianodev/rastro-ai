# Segurança

## Como relatar uma falha

Não abra issue pública para falha de segurança. Use o relato privado do GitHub: aba **Security**, botão **Report a vulnerability** neste repositório.

Inclua a versão (`rastro --version`), o passo a passo para reproduzir e o impacto que você enxerga. A resposta inicial vem em até 7 dias.

## O que o Rastro promete

- **Só na sua máquina:** a API escuta em `127.0.0.1` e recusa `Host` que não seja a própria máquina.
- **Pedido de outro site:** POST, PUT, PATCH e DELETE com `Origin` de fora são recusados.
- **Disco:** só lê as pastas cadastradas; caminho absoluto e `..` vindos da requisição são recusados.
- **Processos:** o build e os testes da validação rodam sem shell, com argumentos separados, tempo limite e o comando que está na configuração (nunca o que vem da requisição).
- **Chave de API dos provedores de IA:** fica cifrada no banco local e nunca volta na resposta nem no log.
- **IA:** só envia ao provedor o trecho de código necessário, e só quando você liga o modo provedor. Na pergunta livre sobre o fluxo, vão a pergunta, o mapa do fluxo (nomes de métodos e `arquivo:linha`), as regras e até 8 trechos de código em volta das entradas e das decisões. A pergunta e o código vão marcados como dado, e a IA é instruída a ignorar instruções dentro deles.
- **Banco de dados** ([ADR-006](docs/adr/adr-006-visao-do-banco.md)): o esquema sai do código do repositório. A conexão com um banco vivo (em construção, ainda fora do pacote) é opcional e só de leitura: o Rastro **nunca lê dados das tabelas**, só o catálogo (tabelas, colunas, chaves, índices, views e procedures) e o plano estimado de uma consulta, sem executá-la. As consultas ao catálogo são fixas no código, e a consulta do plano passa antes pelo leitor de SQL (uma instrução só, sem `SET`, `EXEC` nem DDL). A string de conexão fica cifrada no banco local, como a chave da IA, e nunca volta na resposta, no log nem para a IA. Recomendamos um usuário de banco com permissão mínima (no SQL Server, `VIEW DEFINITION` e `SHOWPLAN`).

Uma falha em qualquer um desses pontos é um relato bem-vindo.
