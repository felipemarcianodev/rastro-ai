namespace Rastro.Infrastructure.Configuration;

// Seção "Rastro" do appsettings.
public class RastroOptions
{
    public const string Section = "Rastro";

    // Repositórios que a API pode ler. A requisição só cita o nome; o caminho fica aqui.
    public List<RepositoryOptions> Repositories { get; set; } = [];

    public AnalysisOptions Analysis { get; set; } = new();

    public bool MigrateOnStartup { get; set; } = true;
}

public class RepositoryOptions
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    // Validação da mudança (Fase 9): comando separado por espaços e executado SEM shell
    // (ex.: "dotnet build backend/App.slnx"; caminhos sem espaço). Roda na raiz do repositório
    // (ou na cópia de trabalho). Vazio = não configurado (a validação mostra como tal).
    public string? BuildCommand { get; set; }
    public string? TestCommand { get; set; }

    // Variáveis de ambiente para o build e os testes (ex.: TestContainers=true para os testes do
    // repositório criarem o próprio Postgres). Só da configuração, nunca da requisição.
    public Dictionary<string, string> Environment { get; set; } = [];
}

public class AnalysisOptions
{
    public int DefaultMaxDepth { get; set; } = 15;
    public int MaxDepthLimit { get; set; } = 40;

    // No Docker o repositório vem montado só para leitura e com o restore feito no Windows:
    // a análise copia o código para uma pasta de trabalho e roda o "dotnet restore" lá.
    public bool CopyAndRestore { get; set; }
    public string WorkDirectory { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rastro-work");
    public int RestoreTimeoutSeconds { get; set; } = 600;
    public int ValidationCommandTimeoutSeconds { get; set; } = 1200;
}
