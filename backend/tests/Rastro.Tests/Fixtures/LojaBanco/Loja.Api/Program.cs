// Loja fictícia dos testes da visão do banco (Fase 37). Só a árvore de sintaxe é lida: nada compila.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LojaDbContext>(o => o
    .UseSqlServer(builder.Configuration.GetConnectionString("Loja"))
    .UseSnakeCaseNamingConvention());
builder.Build().Run();
