using Microsoft.EntityFrameworkCore;

namespace Tarefas.Repo;

// Sistema fictício de tarefas, para os testes do repositório genérico (Fase 43b).
public class AppDbContext : DbContext
{
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<Subtarefa> Subtarefas => Set<Subtarefa>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
}

public class Tarefa
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Titulo { get; set; } = "";
    public int Prioridade { get; set; }
    public DateTime? ArquivadaEm { get; set; }
}

public class Subtarefa
{
    public Guid Id { get; set; }
    public Guid TarefaId { get; set; }
}

public class Cliente
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
}
