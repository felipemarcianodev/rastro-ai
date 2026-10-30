using Microsoft.AspNetCore.Mvc;
using Rastro.Core;
using Rastro.Infrastructure.Analysis;

namespace Rastro.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(INodeRuntime node) : ControllerBase
{
    // Angular: o Node está na máquina, então a tela pode oferecer o levantamento de um tsconfig (Fase 26).
    public record HealthResponse(string Status, string Version, int SchemaVersion, bool Angular);

    [HttpGet]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("ok", RastroVersion.Current, RastroSchema.Version, node.Available));
}
