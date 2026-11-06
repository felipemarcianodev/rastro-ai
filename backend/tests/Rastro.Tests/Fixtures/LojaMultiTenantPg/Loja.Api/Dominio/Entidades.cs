namespace Loja.Api.Dominio;

public class Anotacao
{
    public string Texto { get; set; } = "";
    public int Prioridade { get; set; }
}

public class Perfil
{
    public string Tema { get; set; } = "";
}

public class Atributo
{
    public string Nome { get; set; } = "";
    public string Valor { get; set; } = "";
}

public class Ficha
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Titulo { get; set; } = "";
    public Perfil Perfil { get; set; } = new();
    public List<Anotacao> Anotacoes { get; set; } = [];
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "json")]
    public List<Atributo> Atributos { get; set; } = [];
    public Perfil SemTipo { get; set; } = new();
}
