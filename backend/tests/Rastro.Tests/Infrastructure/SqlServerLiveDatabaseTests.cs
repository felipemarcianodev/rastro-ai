using Microsoft.Data.SqlClient;
using Rastro.Core.Database;
using Rastro.Extractor.Database;
using Rastro.Infrastructure.Database.Live;
using Testcontainers.MsSql;

namespace Rastro.Tests.Infrastructure;

// Só roda com o Docker no ar e com contêiner Linux (a imagem do SQL Server é Linux). O CI de PR roda
// o backend no Windows, cujo Docker só tem contêiner Windows: lá o teste aparece como pulado, com o
// motivo, e a camada fica coberta pelo banco de mentira (API-DBL-NNN).
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Reason = new(() =>
    {
        try
        {
            using var docker = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("docker", "info --format {{.OSType}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (docker is null) return "Docker não encontrado.";
            var output = docker.StandardOutput.ReadToEndAsync();
            if (!docker.WaitForExit(15_000) || docker.ExitCode != 0) return "Docker fora do ar.";
            return output.Result.Trim() == "linux" ? null : "o Docker desta máquina não roda contêiner Linux.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Docker não instalado.";
        }
    });

    public DockerFactAttribute()
    {
        if (Reason.Value is { } reason) Skip = $"{reason} O banco vivo de verdade (SQL Server no Testcontainers) não foi testado.";
    }
}

// Um SQL Server de verdade, descartável, com a loja fictícia (Fase 40b, ADR-006). Nunca um banco do dono.
public sealed class SqlServerContainer : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";
    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (new DockerFactAttribute().Skip is not null) return;
        _container = new MsSqlBuilder(Image).Build();
        await _container.StartAsync();
        var master = _container.GetConnectionString();
        await RunAsync(master, "CREATE DATABASE loja");
        ConnectionString = new SqlConnectionStringBuilder(master) { InitialCatalog = "loja" }.ConnectionString;
        await RunAsync(ConnectionString, """
            CREATE TABLE dbo.clientes (
                id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_clientes PRIMARY KEY,
                nome NVARCHAR(120) NOT NULL,
                email NVARCHAR(200) NOT NULL,
                cpf CHAR(11) NULL,
                criado_em DATETIME2 NOT NULL);
            CREATE UNIQUE INDEX UX_clientes_email ON dbo.clientes (email);
            CREATE TABLE dbo.pedidos (
                id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pedidos PRIMARY KEY,
                cliente_id INT NOT NULL CONSTRAINT FK_pedidos_clientes_cliente_id REFERENCES dbo.clientes (id),
                status NVARCHAR(30) NOT NULL,
                total DECIMAL(10,2) NOT NULL,
                criado_em DATETIME2 NOT NULL);
            CREATE INDEX IX_pedidos_cliente_id ON dbo.pedidos (cliente_id);
            INSERT INTO dbo.clientes (nome, email, criado_em) VALUES (N'Ana (teste)', N'ana@teste.test', SYSUTCDATETIME());
            INSERT INTO dbo.pedidos (cliente_id, status, total, criado_em) VALUES (1, N'aberto', 10, SYSUTCDATETIME());
            """);
        await RunAsync(ConnectionString, "CREATE VIEW dbo.vw_abertos AS SELECT id, total FROM dbo.pedidos WHERE status = N'aberto'");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    public static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<int> CountAsync(string connectionString, string table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM {table}", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}

// Cenários INF-DBL-NNN com SQL Server de verdade: catálogo e plano estimado sem executar a consulta.
public class SqlServerLiveDatabaseTests(SqlServerContainer server) : IClassFixture<SqlServerContainer>
{
    private readonly SqlServerLiveDatabase _live = new();

    [DockerFact] // INF-DBL-010
    public async Task Catalogo_TabelasColunasChavesIndicesEViewComoOCodigoEscreve()
    {
        var fragment = await _live.ReadCatalogAsync(server.ConnectionString, default);

        Assert.Equal(["clientes", "pedidos"], fragment.Tables.Select(t => t.Name));
        var pedidos = fragment.Tables[1];
        Assert.Equal(["id", "cliente_id", "status", "total", "criado_em"], pedidos.Columns.Select(c => c.Name));
        Assert.Equal(["int", "int", "nvarchar(30)", "decimal(10,2)", "datetime2"], pedidos.Columns.Select(c => c.Type));
        Assert.True(pedidos.Columns[0].Identity);
        Assert.Equal(["id"], pedidos.PrimaryKey!.Columns);
        var fk = Assert.Single(pedidos.ForeignKeys);
        Assert.Equal(("clientes", "cliente_id"), (fk.PrincipalTable, fk.Columns[0]));
        Assert.Equal("IX_pedidos_cliente_id", Assert.Single(pedidos.Indexes).Name);
        Assert.True(fragment.Tables[0].Columns.Single(c => c.Name == "cpf").Nullable);
        Assert.All(pedidos.Sources, s => Assert.Equal(SchemaSourceKind.LiveDatabase, s.Kind));
        Assert.Equal(["id", "total"], Assert.Single(fragment.Views).Columns);
        Assert.Equal(2, await _live.CountTablesAsync(server.ConnectionString, default));
    }

    [DockerFact] // INF-DBL-011
    public async Task Plano_DaConsultaDoPrototipo_VarreduraEIndiceFaltandoSemExecutar()
    {
        const string sql = """
            SELECT * FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE YEAR(p.criado_em) = @p0 AND p.status = @p1
            ORDER BY p.criado_em DESC
            """;
        var guard = TSqlPlanGuard.Check(sql);

        var plan = ShowPlanReader.Read(await _live.ShowPlanXmlAsync(server.ConnectionString, sql, guard.Variables, default));

        Assert.True(plan.TotalCost > 0);
        Assert.Contains(plan.Operations, o => o.Scan && o.Object?.StartsWith("pedidos", StringComparison.Ordinal) == true);
        Assert.InRange(plan.Operations.Sum(o => o.Percent), 99, 101);
    }

    [DockerFact] // INF-DBL-012
    public async Task Plano_DeUmDelete_NaoApagaNada()
    {
        var before = await SqlServerContainer.CountAsync(server.ConnectionString, "dbo.pedidos");

        var plan = ShowPlanReader.Read(await _live.ShowPlanXmlAsync(server.ConnectionString, "DELETE FROM pedidos", [], default));

        Assert.NotEmpty(plan.Operations);
        Assert.Equal(before, await SqlServerContainer.CountAsync(server.ConnectionString, "dbo.pedidos"));
        Assert.Equal(1, before);
    }
}
