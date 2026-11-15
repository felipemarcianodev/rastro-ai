using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Rastro.Infrastructure.Configuration;
using Rastro.Mcp;
using Rastro.Tests.Api;

namespace Rastro.Tests.Mcp;

// Cenários MCP-LANG-NNN: o MCP no idioma do config.json (Fase 51, ADR-009). As instruções e as descrições das
// ferramentas ficam em inglês fixo; o que as ferramentas devolvem segue o idioma gravado, lido a cada chamada.
[Collection(ApiCollection.Name)]
public partial class McpLanguageTests(ApiFixture fixture)
{
    // Letra acentuada ou cedilha: sinal de português (ou espanhol) onde só inglês é permitido.
    [GeneratedRegex("[áàâãéêíóôõúçñ¿¡]", RegexOptions.IgnoreCase)]
    private static partial Regex Accented();

    [GeneratedRegex(@"\b(você|usuário|ferramenta|português|levantamento|repositório|não)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseWords();

    private static string[] AllDescriptions()
    {
        var list = new List<string>();
        foreach (var method in typeof(RastroTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            list.Add(method.GetCustomAttribute<DescriptionAttribute>()!.Description);
            list.AddRange(method.GetParameters().Select(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description).OfType<string>());
        }
        foreach (var type in new[] { typeof(QueryChangeInput), typeof(QueryIndexInput) })
            list.AddRange(type.GetProperties().Select(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description).OfType<string>());
        return [.. list];
    }

    // Opções que a troca de idioma pode mudar com o servidor ligado (no produto, o config.json recarregado).
    private sealed class MutableOptions(RastroOptions value) : IOptionsMonitor<RastroOptions>
    {
        public RastroOptions Current { get; set; } = value;
        public RastroOptions CurrentValue => Current;
        public RastroOptions Get(string? name) => Current;
        public IDisposable? OnChange(Action<RastroOptions, string?> listener) => null;
    }

    private (ServiceProvider Provider, MutableOptions Options) Session(string? language)
    {
        var options = new MutableOptions(new RastroOptions { Language = language });
        var provider = fixture.McpServices(configure: services =>
        {
            // O repositório de teste fica no catálogo, que também lê o monitor.
            options.Current.Repositories.Add(new RepositoryOptions { Name = ApiFixture.RepositoryName, Path = fixture.RepositoryRoot });
            services.AddSingleton<IOptionsMonitor<RastroOptions>>(options);
        });
        return (provider, options);
    }

    [Fact] // MCP-LANG-001
    public void Instrucoes_EmInglesFixo_SemPortuguesEComOIdiomaDoUsuario()
    {
        Assert.DoesNotMatch(Accented(), McpInstructions.Text);
        Assert.DoesNotMatch(PortugueseWords(), McpInstructions.Text);
        Assert.DoesNotContain("em português", McpInstructions.Text);
        Assert.Contains("language", McpInstructions.Text);
        // As regras que não se quebram continuam: o agente só propõe e o usuário confirma, na tela.
        Assert.Contains("Only the user confirms or dismisses it", McpInstructions.Text);
    }

    [Fact] // MCP-LANG-002
    public void Descricoes_DasFerramentasEDosParametros_EmInglesSemPortugues()
    {
        var descriptions = AllDescriptions();
        Assert.True(descriptions.Length > 60);
        foreach (var text in descriptions)
        {
            Assert.DoesNotMatch(Accented(), text);
            Assert.DoesNotMatch(PortugueseWords(), text);
        }
        var describe = typeof(RastroTools).GetMethod(nameof(RastroTools.DescribeNode))!.GetCustomAttribute<DescriptionAttribute>()!.Description;
        Assert.Contains("user's language", describe);
    }

    [Theory] // MCP-LANG-003
    [InlineData("pt-BR", "Repositório não cadastrado. Use list_repositories.")]
    [InlineData("en-US", "Repository not registered. Use list_repositories.")]
    [InlineData("es", "Repositorio no registrado. Usa list_repositories.")]
    public async Task Ferramenta_DevolveOTextoNoIdiomaDoConfig(string language, string expected)
    {
        var (provider, _) = Session(language);
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();

        var text = await scope.ServiceProvider.GetRequiredService<RastroTools>().GetDbSchema("repositorio-que-nao-existe");

        Assert.Equal(expected, text);
    }

    [Fact] // MCP-LANG-004
    public async Task TrocarOIdiomaNoConfig_MudaARespostaSeguinteSemReiniciar()
    {
        var (provider, options) = Session("pt-BR");
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var before = await tools.GetDbSchema("repositorio-que-nao-existe");
        options.Current = new RastroOptions { Language = "es", Repositories = options.Current.Repositories };
        var after = await tools.GetDbSchema("repositorio-que-nao-existe");
        options.Current = new RastroOptions { Language = "en-US", Repositories = options.Current.Repositories };
        var last = await tools.GetDbSchema("repositorio-que-nao-existe");

        Assert.StartsWith("Repositório não cadastrado", before);
        Assert.StartsWith("Repositorio no registrado", after);
        Assert.StartsWith("Repository not registered", last);
    }

    [Fact] // MCP-LANG-005
    public async Task FalhaDaAnalise_SaiNoIdiomaDoUsuarioPeloCodigo_EOLogContinuaEmPortugues()
    {
        var (provider, _) = Session("es");
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var text = await tools.AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "Nada.Existe");

        Assert.StartsWith("El análisis falló: ", text);
        Assert.Contains("solution", text);
        Assert.DoesNotContain("não encontrado", text);
    }

    [Fact] // MCP-LANG-006
    public async Task FluxoEmEspanhol_ListaOsBuracosEOMapaNoIdioma()
    {
        var (provider, _) = Session("es");
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var analyzed = await tools.AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        Assert.StartsWith("Rastreo ", analyzed);
        var id = Guid.Parse(analyzed.Split(' ')[1]);

        var holes = await tools.ListUnresolved(id);
        var flow = await tools.GetFlow(id);

        Assert.Contains("llamada `notifier.Notify`", holes);
        Assert.Contains("más de una implementación", holes);
        Assert.StartsWith("Flujo de AlertJob.ExecuteAsync", flow);
        Assert.Contains("entrada: AlertJob.ExecuteAsync", flow);
    }
}
