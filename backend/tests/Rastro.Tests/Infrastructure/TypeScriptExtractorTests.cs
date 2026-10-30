using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-TS-NNN: o extrator Angular de verdade (o script Node do repositório sobre a fixture loja-web).
public sealed class TypeScriptExtractorTests : IAsyncLifetime
{
    private AngularRepo _loja = null!;

    public async Task InitializeAsync()
    {
        AngularRepo.RequireNode();
        _loja = await AngularRepo.CreateAsync();
    }

    public async Task DisposeAsync() => await _loja.DisposeAsync();

    private static TypeScriptExtractor Extractor(string? script = null) =>
        new(Options.Create(new RastroOptions { Analysis = new AnalysisOptions { TypeScriptTimeoutSeconds = 120 } }),
            NullLogger<TypeScriptExtractor>.Instance, script ?? AngularRepo.ScriptPath);

    private ResolvedTarget Target() =>
        new(_loja.Name, _loja.Root, Path.Combine(_loja.Root, AngularRepo.TsConfig), AngularRepo.TsConfig, SourceLanguage.Angular);

    [Fact] // INF-TS-001
    public async Task Extrair_ComponenteDaFixture_DevolveEntradaGuardaDoTemplateEChamadaHttp()
    {
        var result = await Extractor().ExtractAsync(Target(), AngularRepo.Entry, 15, CancellationToken.None);

        var entry = result.Graph.Nodes.Single(n => n.Id == result.Graph.EntryNodeId);
        Assert.Equal(NodeKind.Entry, entry.Kind);
        Assert.Equal("DetalhePedidoComponent.cancelar", entry.Label);
        // A bifurcação da tela: o @if do template que esconde o botão.
        Assert.Contains(result.Graph.Nodes, n => n.Kind == NodeKind.Decision && n.Location!.File.EndsWith(".html", StringComparison.Ordinal));

        var call = Assert.Single(result.HttpCalls);
        Assert.Equal(new HttpCall("POST", "api/pedidos/{}/cancelamento"), call.Call);
        Assert.StartsWith("h:", call.NodeId, StringComparison.Ordinal);
        Assert.Contains(result.Graph.Nodes, n => n.Id == call.NodeId && n.Kind == NodeKind.Unresolved);
    }

    [Fact] // INF-TS-002
    public async Task Extrair_PontoDeEntradaInexistente_LevantaAnalysisExceptionLegivel()
    {
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Extractor().ExtractAsync(Target(), "DetalhePedidoComponent.naoExiste", 15, CancellationToken.None));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.DoesNotContain("Error:", ex.Message, StringComparison.Ordinal); // nada de stack do Node
    }

    [Fact] // INF-TS-003
    public async Task Extrair_ScriptAusente_LevantaAnalysisException()
    {
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Extractor(Path.Combine(_loja.Root, "nao-existe.mjs")).ExtractAsync(Target(), AngularRepo.Entry, 15, CancellationToken.None));

        Assert.Contains("extrator Angular", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // INF-TS-004
    public void SaidaDoBuild_TemOScriptEOsModulosDoExtrator()
    {
        // Rastro.Api.csproj leva o extractor-ts para a saída (e para o pacote).
        var folder = Path.Combine(AppContext.BaseDirectory, "extractor-ts");

        Assert.True(File.Exists(Path.Combine(folder, "extract.mjs")));
        Assert.True(File.Exists(Path.Combine(folder, "package.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "lib"), "*.mjs"));
        Assert.False(Directory.Exists(Path.Combine(folder, "node_modules")));
        Assert.False(Directory.Exists(Path.Combine(folder, "test")));
    }
}
