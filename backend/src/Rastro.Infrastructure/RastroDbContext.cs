using Microsoft.EntityFrameworkCore;
using Rastro.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Design;
using Rastro.Core.Surveys;
using Rastro.Core.Validation;

namespace Rastro.Infrastructure;

public class RastroDbContext(DbContextOptions<RastroDbContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyProposal> SurveyProposals => Set<SurveyProposal>();
    public DbSet<SurveyDraft> SurveyDrafts => Set<SurveyDraft>();
    public DbSet<SurveyValidation> SurveyValidations => Set<SurveyValidation>();
    public DbSet<SurveyArchitecture> SurveyArchitectures => Set<SurveyArchitecture>();
    public DbSet<NodeNote> NodeNotes => Set<NodeNote>();
    public DbSet<SurveyDiagram> SurveyDiagrams => Set<SurveyDiagram>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Survey>(survey =>
        {
            survey.HasKey(s => s.Id);
            survey.Property(s => s.Repository).HasMaxLength(100).IsRequired();
            survey.Property(s => s.SolutionPath).HasMaxLength(500).IsRequired();
            survey.Property(s => s.EntryPoint).HasMaxLength(500).IsRequired();
            survey.Property(s => s.CommitHash).HasMaxLength(64);
            survey.Property(s => s.Branch).HasMaxLength(200);
            survey.Property(s => s.Error).HasMaxLength(2000);
            survey.HasIndex(s => s.CreatedAt);
            survey.HasIndex(s => new { s.Repository, s.EntryPoint });
        });

        modelBuilder.Entity<SurveyProposal>(proposal =>
        {
            proposal.HasKey(p => p.Id);
            proposal.HasOne<Survey>().WithMany().HasForeignKey(p => p.SurveyId).OnDelete(DeleteBehavior.Cascade);
            proposal.Property(p => p.NodeId).HasMaxLength(600).IsRequired();
            proposal.Property(p => p.Target).HasMaxLength(500);
            proposal.Property(p => p.TargetNodeId).HasMaxLength(600);
            proposal.Property(p => p.EvidenceFile).HasMaxLength(500);
            proposal.Property(p => p.When).HasMaxLength(200);
            proposal.Property(p => p.Text).HasMaxLength(2000).IsRequired();
            proposal.Property(p => p.RejectionReason).HasMaxLength(1000);
            proposal.HasIndex(p => new { p.SurveyId, p.CreatedAt });
        });

        modelBuilder.Entity<SurveyDraft>(draft =>
        {
            draft.HasKey(d => d.SurveyId);
            draft.HasOne<Survey>().WithOne().HasForeignKey<SurveyDraft>(d => d.SurveyId).OnDelete(DeleteBehavior.Cascade);
            draft.Property(d => d.GraphJson).IsRequired();
        });

        modelBuilder.Entity<SurveyArchitecture>(architecture =>
        {
            architecture.HasKey(a => a.SurveyId);
            architecture.HasOne<Survey>().WithOne().HasForeignKey<SurveyArchitecture>(a => a.SurveyId).OnDelete(DeleteBehavior.Cascade);
            architecture.Property(a => a.PeopleJson).IsRequired();
        });

        modelBuilder.Entity<SurveyDiagram>(diagram =>
        {
            diagram.HasKey(d => d.SurveyId);
            diagram.HasOne<Survey>().WithOne().HasForeignKey<SurveyDiagram>(d => d.SurveyId).OnDelete(DeleteBehavior.Cascade);
            diagram.Property(d => d.Mermaid).IsRequired();
            diagram.Property(d => d.CommitHash).HasMaxLength(64);
        });

        modelBuilder.Entity<NodeNote>(note =>
        {
            note.HasKey(n => n.Id);
            note.Property(n => n.Repository).HasMaxLength(100).IsRequired();
            note.Property(n => n.NodeId).HasMaxLength(600).IsRequired();
            note.Property(n => n.Text).HasMaxLength(NodeNote.MaxTextLength).IsRequired();
            note.HasIndex(n => new { n.Repository, n.NodeId }).IsUnique();
        });

        modelBuilder.Entity<SurveyValidation>(validation =>
        {
            validation.HasKey(v => v.Id);
            validation.HasOne<Survey>().WithMany().HasForeignKey(v => v.SurveyId).OnDelete(DeleteBehavior.Cascade);
            validation.HasOne<Survey>().WithMany().HasForeignKey(v => v.NewSurveyId).OnDelete(DeleteBehavior.SetNull);
            validation.Property(v => v.Error).HasMaxLength(2000);
            validation.HasIndex(v => new { v.SurveyId, v.CreatedAt });
        });
    }

    // O SQLite não ordena nem compara DateTimeOffset: grava como número (ticks UTC + offset), que ordena.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        RastroDatabase.UseSortableDates(configurationBuilder);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(RastroDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();
}

// Só para o "dotnet ef" gerar migrations sem subir a API.
public class RastroDbContextDesignFactory : IDesignTimeDbContextFactory<RastroDbContext>
{
    public RastroDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RastroDbContext>();
        RastroDbContext.Configure(options, "Data Source=design-time.db");
        return new RastroDbContext(options.Options);
    }
}
