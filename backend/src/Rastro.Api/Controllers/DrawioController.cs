using Microsoft.AspNetCore.Mvc;
using Rastro.Api.Editor;

namespace Rastro.Api.Controllers;

// Se o draw.io está nesta instalação (ADR-011): a tela só oferece o desenho draw.io quando ele está.
[ApiController]
[Route("api/drawio")]
public class DrawioController(IConfiguration configuration) : ControllerBase
{
    public record DrawioStatus(bool Available, string? Version);

    [HttpGet]
    public ActionResult<DrawioStatus> Get()
    {
        var folder = DrawioHosting.Folder(configuration);
        var available = DrawioHosting.IsAvailable(folder);
        return Ok(new DrawioStatus(available, available ? DrawioHosting.Version(folder) : null));
    }
}
