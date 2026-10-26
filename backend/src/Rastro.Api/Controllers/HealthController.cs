using Microsoft.AspNetCore.Mvc;
using Rastro.Core;

namespace Rastro.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    public record HealthResponse(string Status, string Version, int SchemaVersion);

    [HttpGet]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("ok", RastroVersion.Current, RastroSchema.Version));
}
