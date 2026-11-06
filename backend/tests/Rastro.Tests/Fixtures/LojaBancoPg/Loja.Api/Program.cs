// Loja fictícia dos testes da visão do banco no PostgreSQL (Fase 42), a mesma loja da LojaBanco no
// provedor Npgsql. Só a árvore de sintaxe é lida: nada compila.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LojaDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Loja"))
    .UseSnakeCaseNamingConvention());
builder.Build().Run();
