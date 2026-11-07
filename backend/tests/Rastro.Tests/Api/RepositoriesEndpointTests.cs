using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Rastro.Api.Controllers;
using Rastro.Infrastructure.Storage;

namespace Rastro.Tests.Api;

// Cenários API-REP-NNN e API-REPO-NNN: lista, cadastro e remoção de repositórios pela tela (ADR-003),
// com a proteção por Origin e por Host. O cadastro não leva comando; o build e os testes se editam pela
// rota de comandos (ADR-008).
[Collection(ApiCollection.Name)]
public class RepositoriesEndpointTests(ApiFixture fixture)
{
    private static string NewFolder(string name, bool withGit = true)
    {
        var path = Path.Combine(Path.GetTempPath(), "rastro-tests", "repos-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(withGit ? Path.Combine(path, ".git") : path);
        return path;
    }

    private static string UniqueName() => "repo" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())?["code"]?.GetValue<string>();

    private static HttpRequestMessage Request(HttpMethod method, string url, object? body = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    [Fact] // API-REP-001
    public async Task Listar_RetornaORepositorioConfiguradoComPasta()
    {
        var client = fixture.Factory.CreateClient();

        var items = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        var fixtureItem = Assert.Single(items!, i => i.Name == ApiFixture.RepositoryName);
        Assert.Equal(Path.GetFullPath(fixture.RepositoryRoot), fixtureItem.Path);
        Assert.True(fixtureItem.HasGit);
        Assert.False(fixtureItem.HasBuildCommand);
        Assert.False(fixtureItem.HasTestCommand);
    }

    [Fact] // API-REP-002
    public async Task Solutions_RetornaCaminhosRelativos()
    {
        var client = fixture.Factory.CreateClient();

        var solutions = await client.GetFromJsonAsync<List<string>>($"/api/repositories/{ApiFixture.RepositoryName}/solutions");

        Assert.Contains(ApiFixture.SolutionFile, solutions!);
        Assert.All(solutions!, s => Assert.False(Path.IsPathRooted(s)));
    }

    [Fact] // API-REP-003
    public async Task Solutions_RepositorioDesconhecido_Retorna404()
    {
        var client = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/repositories/outro/solutions")).StatusCode);
    }

    [Fact] // API-REPO-001
    public async Task Adicionar_PastaValida_Retorna201EGravaSemComando()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        var folder = NewFolder(name);

        var response = await client.PostAsJsonAsync("/api/repositories", new { name, path = folder });
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"api/repositories/{name}", response.Headers.Location!.OriginalString.TrimStart('/'));
        var created = await response.Content.ReadFromJsonAsync<RepositoryItem>();
        Assert.Equal(new RepositoryItem(name, Path.GetFullPath(folder), true, false, false), created);
        // O GET logo depois do POST já enxerga o repositório (a API recarrega a configuração ao gravar).
        Assert.Contains(listed!, i => i.Name == name);

        var config = JsonNode.Parse(await File.ReadAllTextAsync(RastroHome.ConfigPath))!;
        var entry = config["Rastro"]!["Repositories"]!.AsArray().Single(e => (string?)e!["Name"] == name)!.AsObject();
        Assert.Equal(Path.GetFullPath(folder), (string?)entry["Path"]);
        Assert.False(entry.ContainsKey("BuildCommand"));
        Assert.False(entry.ContainsKey("TestCommand"));
        Assert.False(entry.ContainsKey("Environment"));

        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Fact] // API-REPO-002
    public async Task Adicionar_PastaSemGit_RetornaHasGitFalso()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();

        var response = await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name, withGit: false) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<RepositoryItem>())!.HasGit);
        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Theory] // API-REPO-003
    [InlineData("")]
    [InlineData("../fora")]
    [InlineData("com espaço")]
    [InlineData(".comeca-com-ponto")]
    public async Task Adicionar_NomeInvalido_Retorna400(string name)
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder("x") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryNameInvalid, await CodeOf(response));
    }

    [Theory] // API-REPO-004
    [InlineData("")]
    [InlineData("pasta/relativa")]
    [InlineData("..\\fora")]
    public async Task Adicionar_CaminhoVazioOuRelativo_Retorna400(string path)
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/repositories", new { name = UniqueName(), path });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryPathInvalid, await CodeOf(response));
    }

    [Fact] // API-REPO-005
    public async Task Adicionar_PastaInexistente_Retorna400()
    {
        var client = fixture.Factory.CreateClient();
        var missing = Path.Combine(Path.GetTempPath(), "rastro-tests", "nao-existe-" + Guid.NewGuid().ToString("N"));

        var response = await client.PostAsJsonAsync("/api/repositories", new { name = UniqueName(), path = missing });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryFolderNotFound, await CodeOf(response));
    }

    [Fact] // API-REPO-006
    public async Task Adicionar_NomeRepetidoSemDiferenciarMaiuscula_Retorna409()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });

        var response = await client.PostAsJsonAsync("/api/repositories", new { name = name.ToUpperInvariant(), path = NewFolder("outra") });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryExists, await CodeOf(response));
        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Fact] // API-REPO-007
    public async Task Remover_Cadastrado_Retorna204ESomeDoGet()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });

        var response = await client.DeleteAsync($"/api/repositories/{name}");
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(listed!, i => i.Name == name);
        Assert.Contains(listed!, i => i.Name == ApiFixture.RepositoryName);
    }

    [Fact] // API-REPO-008
    public async Task Remover_Inexistente_Retorna404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.DeleteAsync($"/api/repositories/{UniqueName()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryNotFound, await CodeOf(response));
    }

    [Theory] // API-REPO-009
    [InlineData("http://evil.example")]
    [InlineData("null")]
    [InlineData("http://localhost.evil.example:4300")]
    public async Task Post_OriginDeFora_Retorna403SemGravar(string origin)
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/api/repositories", new { name, path = NewFolder(name) }, origin));
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.OriginNotAllowed, await CodeOf(response));
        Assert.DoesNotContain(listed!, i => i.Name == name);
    }

    [Theory] // API-REPO-010
    [InlineData("http://localhost:4300")]
    [InlineData("http://127.0.0.1:7450")]
    [InlineData("http://[::1]:7450")]
    public async Task Post_OriginLocal_Passa(string origin)
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/api/repositories", new { name, path = NewFolder(name) }, origin));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Fact] // API-REPO-011
    public async Task Delete_SemOrigin_Passa_ComOriginDeForaRetorna403()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });

        var blocked = await client.SendAsync(Request(HttpMethod.Delete, $"/api/repositories/{name}", origin: "http://evil.example"));
        var allowed = await client.SendAsync(Request(HttpMethod.Delete, $"/api/repositories/{name}"));

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact] // API-REPO-012
    public async Task Get_OriginDeFora_NaoEBloqueado()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/api/repositories", origin: "http://evil.example"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // API-REPO-013
    public async Task Get_HostDeFora_Retorna400()
    {
        var client = fixture.Factory.CreateClient();
        var request = Request(HttpMethod.Get, "/api/repositories");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // API-REPO-014
    public async Task Comandos_Gravados_VoltamNaListaENoConfig()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });

        var response = await client.PutAsJsonAsync($"/api/repositories/{name}/commands",
            new { buildCommand = "  dotnet build Loja.slnx ", testCommand = "dotnet test" });
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<RepositoryItem>();
        Assert.Equal("dotnet build Loja.slnx", item!.BuildCommand);
        Assert.True(item.HasTestCommand);
        var fromList = Assert.Single(listed!, i => i.Name == name);
        Assert.Equal("dotnet build Loja.slnx", fromList.BuildCommand);
        Assert.Equal("dotnet test", fromList.TestCommand);

        var config = JsonNode.Parse(await File.ReadAllTextAsync(RastroHome.ConfigPath))!;
        var entry = config["Rastro"]!["Repositories"]!.AsArray().Single(e => (string?)e!["Name"] == name)!.AsObject();
        Assert.Equal("dotnet test", (string?)entry["TestCommand"]);

        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Fact] // API-REPO-015
    public async Task Comandos_Vazios_TiramOComandoDaEntrada()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });
        await client.PutAsJsonAsync($"/api/repositories/{name}/commands", new { buildCommand = "dotnet build", testCommand = "dotnet test" });

        var response = await client.PutAsJsonAsync($"/api/repositories/{name}/commands", new { buildCommand = "", testCommand = (string?)null });

        var item = await response.Content.ReadFromJsonAsync<RepositoryItem>();
        Assert.False(item!.HasBuildCommand);
        Assert.Null(item.TestCommand);
        var config = JsonNode.Parse(await File.ReadAllTextAsync(RastroHome.ConfigPath))!;
        var entry = config["Rastro"]!["Repositories"]!.AsArray().Single(e => (string?)e!["Name"] == name)!.AsObject();
        Assert.False(entry.ContainsKey("BuildCommand"));
        Assert.False(entry.ContainsKey("TestCommand"));

        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Theory] // API-REPO-016
    [InlineData("dotnet build\ndel arquivo")]
    [InlineData("dotnet build\r")]
    [InlineData("comprido")]
    public async Task Comandos_Invalidos_Retorna400SemGravar(string command)
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });
        var text = command == "comprido" ? new string('a', 501) : command;

        var response = await client.PutAsJsonAsync($"/api/repositories/{name}/commands", new { buildCommand = text, testCommand = "dotnet test" });
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryCommandInvalid, await CodeOf(response));
        Assert.False(Assert.Single(listed!, i => i.Name == name).HasTestCommand);

        await client.DeleteAsync($"/api/repositories/{name}");
    }

    [Fact] // API-REPO-017
    public async Task Comandos_RepositorioInexistente_Retorna404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/repositories/{UniqueName()}/commands", new { buildCommand = "dotnet build" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.RepositoryNotFound, await CodeOf(response));
    }

    [Fact] // API-REPO-018
    public async Task Comandos_OriginDeFora_Retorna403SemGravar()
    {
        var client = fixture.Factory.CreateClient();
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/repositories", new { name, path = NewFolder(name) });

        var response = await client.SendAsync(Request(HttpMethod.Put, $"/api/repositories/{name}/commands",
            new { buildCommand = "calc" }, "http://evil.example"));
        var listed = await client.GetFromJsonAsync<List<RepositoryItem>>("/api/repositories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Assert.Single(listed!, i => i.Name == name).BuildCommand);

        await client.DeleteAsync($"/api/repositories/{name}");
    }
}
