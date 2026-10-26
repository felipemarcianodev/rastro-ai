using System.Net;
using System.Net.Http.Json;
using Rastro.Core;
using Rastro.Tests.Api;

namespace Rastro.Tests;

[Collection(ApiCollection.Name)]
public class HealthEndpointTests(ApiFixture fixture)
{
    private record HealthResponse(string Status, string Version, int SchemaVersion);

    [Fact]
    public async Task GetHealth_ApiNoAr_RetornaOkComVersaoESchema()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal(RastroVersion.Current, body.Version);
        Assert.Equal(RastroSchema.Version, body.SchemaVersion);
    }
}
