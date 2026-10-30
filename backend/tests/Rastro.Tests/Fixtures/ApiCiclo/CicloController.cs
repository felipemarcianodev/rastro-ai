using Microsoft.AspNetCore.Mvc;

namespace Fixture.ApiCiclo;

// Dois endpoints do mesmo sistema que se chamam por HTTP: A chama B e B chama A.
[ApiController]
[Route("api/[controller]")]
public class CicloController(HttpClient http) : ControllerBase
{
    [HttpPost("ida")]
    public async Task Ida()
    {
        await http.PostAsync("api/ciclo/volta", null);
    }

    [HttpPost("volta")]
    public async Task Volta()
    {
        await http.PostAsync("api/ciclo/ida", null);
    }
}
