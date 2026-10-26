using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Rastro.Ai.Rules;
using Rastro.Ai.Settings;
using Rastro.Infrastructure.Storage;

namespace Rastro.Ai;

// Banco do modo provedor, separado do núcleo (ADR-001): mesmo arquivo SQLite, migrations próprias
// (tabela de histórico __ai_migrations_history). Guarda também as chaves do DataProtection,
// para a chave de API cifrada sobreviver a reinícios e ao Docker.
public class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AiSettings> Settings => Set<AiSettings>();
    public DbSet<AiUsageDay> UsageDays => Set<AiUsageDay>();
    public DbSet<AiRuleCache> RuleCache => Set<AiRuleCache>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiSettings>(settings =>
        {
            settings.ToTable("ai_settings");
            settings.HasKey(s => s.Id);
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.ClaudeModel).HasMaxLength(100);
            settings.Property(s => s.OpenAiModel).HasMaxLength(100);
            settings.Property(s => s.GeminiModel).HasMaxLength(100);
        });

        modelBuilder.Entity<AiUsageDay>(usage =>
        {
            usage.ToTable("ai_usage_days");
            usage.HasKey(u => u.Day);
        });

        modelBuilder.Entity<AiRuleCache>(cache =>
        {
            cache.ToTable("ai_rule_cache");
            cache.HasKey(c => c.CodeHash);
            cache.Property(c => c.CodeHash).HasMaxLength(64);
            cache.Property(c => c.Text).HasMaxLength(2000);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("ai_data_protection_keys");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        RastroDatabase.UseSortableDates(configurationBuilder);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsHistoryTable("__ai_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

// Só para o "dotnet ef" gerar migrations sem subir a API.
public class AiDbContextDesignFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AiDbContext>();
        AiDbContext.Configure(options, "Data Source=design-time.db");
        return new AiDbContext(options.Options);
    }
}
