using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rastro.Infrastructure.Database.Live;
using Rastro.Infrastructure.Storage;

namespace Rastro.Mcp;

// Banco vivo no modo agente (Fase 44): o get_query_plan e o custo da propose_query_rewrite usam o mesmo
// LiveDatabaseService da API. O cadastro da conexão continua só pela tela. O processo do MCP só LÊ as chaves
// do DataProtection que a API guardou no ~/.rastro (a tabela ai_data_protection_keys, mesmo nome de
// aplicação e mesmo propósito da conexão): não cria chave, não escreve e não referencia o Rastro.Ai (ADR-001).
public static class McpLiveDatabase
{
    public static IServiceCollection AddRastroMcpLiveDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var protection = services.AddDataProtection().SetApplicationName("Rastro").DisableAutomaticKeyGeneration();
        protection.Services.AddOptions<KeyManagementOptions>().Configure<ILoggerFactory>((options, loggers) =>
            options.XmlRepository = new ReadOnlyKeyRepository(RastroDatabase.ConnectionString(configuration), loggers.CreateLogger<ReadOnlyKeyRepository>()));
        return services.AddRastroLiveDatabase();
    }
}

// As chaves que a API gravou, só leitura. Com a tabela ausente (a API nunca subiu) ou sem chave, devolve
// vazio: decifrar falha com CryptographicException, que a ferramenta trata. Gravar é recusado.
internal sealed class ReadOnlyKeyRepository(string connectionString, ILogger<ReadOnlyKeyRepository> logger) : IXmlRepository
{
    private const string Query = "SELECT xml FROM ai_data_protection_keys ORDER BY id";

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var keys = new List<XElement>();
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = Query;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) keys.Add(XElement.Parse(reader.GetString(0)));
        }
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "As chaves do DataProtection não puderam ser lidas do banco do Rastro (código {SqliteCode}).", ex.SqliteErrorCode);
        }
        catch (System.Xml.XmlException ex)
        {
            logger.LogWarning(ex, "Uma chave do DataProtection do banco do Rastro não pôde ser lida.");
        }
        return keys;
    }

    public void StoreElement(XElement element, string friendlyName) =>
        throw new NotSupportedException("O servidor MCP não cria chaves: elas são da API do Rastro.");
}
