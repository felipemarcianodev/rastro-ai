using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Api.Cli;
using Rastro.Api.Controllers;
using Xunit;

namespace Rastro.Tests.Api;

// Cenários CLI-PORT-NNN: abrir o "rastro" com a porta ocupada não despeja erro (pedido do dono em 2026-11-02).
public class PortProbeTests
{
    // O servidor do teste é recém-aberto e a suíte roda em paralelo: sem folga, o primeiro /api/health estoura os 2 segundos do produto.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static async Task<(WebApplication App, int Port)> ServeAsync(Func<HttpContext, Task> respond)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(context => respond(context));
        await app.StartAsync();
        return (app, new Uri(app.Urls.Single()).Port);
    }

    [Fact] // CLI-PORT-001
    public async Task CheckAsync_PortaLivre_DevolveLivre()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var check = await PortProbe.CheckAsync(port, NullLogger.Instance);

        Assert.Equal(PortState.Free, check.State);
    }

    [Fact] // CLI-PORT-002
    public async Task CheckAsync_RastroRespondendo_DevolveAbertoComAVersao()
    {
        var (app, port) = await ServeAsync(context =>
            context.Response.WriteAsJsonAsync(new HealthController.HealthResponse("ok", "9.8.7", 2, true)));
        await using (app)
        {
            var check = await PortProbe.CheckAsync(port, NullLogger.Instance, healthTimeout: Patience);

            Assert.Equal(PortState.RastroOpen, check.State);
            Assert.Equal("9.8.7", check.Version);
        }
    }

    [Theory] // CLI-PORT-003
    [InlineData("<html>outro site</html>")]
    [InlineData("{\"status\":\"ok\"}")]
    [InlineData("[]")]
    public async Task CheckAsync_OutroProgramaRespondendo_DevolveOutroPrograma(string body)
    {
        var (app, port) = await ServeAsync(context => context.Response.WriteAsync(body));
        await using (app)
        {
            var check = await PortProbe.CheckAsync(port, NullLogger.Instance, healthTimeout: Patience);

            Assert.Equal(PortState.OtherProgram, check.State);
        }
    }

    [Fact] // CLI-PORT-004
    public async Task CheckAsync_OutroProgramaComErroHttp_DevolveOutroPrograma()
    {
        var (app, port) = await ServeAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
        await using (app)
        {
            var check = await PortProbe.CheckAsync(port, NullLogger.Instance, healthTimeout: Patience);

            Assert.Equal(PortState.OtherProgram, check.State);
        }
    }

    [Fact] // CLI-PORT-005
    public async Task CheckAsync_ProgramaQueNaoRespondeHttp_DevolveOutroPrograma()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var check = await PortProbe.CheckAsync(port, NullLogger.Instance);

        Assert.Equal(PortState.OtherProgram, check.State);
    }

    [Fact] // CLI-PORT-006
    public void Report_RastroAberto_DizOEnderecoESaiComSucesso()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = PortProbe.Report(new PortCheck(PortState.RastroOpen, "9.8.7"), 7450, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("9.8.7", output.ToString());
        Assert.Contains("http://127.0.0.1:7450", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact] // CLI-PORT-007
    public void Report_OutroPrograma_SugereOutraPortaESaiComErro()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = PortProbe.Report(new PortCheck(PortState.OtherProgram), 7450, output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("7450", error.ToString());
        Assert.Contains("--porta", error.ToString());
        Assert.Empty(output.ToString());
    }
}
