namespace Rastro.Infrastructure.Configuration;

// Seção "Rastro" do appsettings.
public class RastroOptions
{
    public const string Section = "Rastro";

    // Repositórios que a API pode ler. A requisição só cita o nome; o caminho fica aqui.
    public List<RepositoryOptions> Repositories { get; set; } = [];

    public AnalysisOptions Analysis { get; set; } = new();

    public bool MigrateOnStartup { get; set; } = true;

    // Idioma gravado no config.json ("pt-BR", "en-US" ou "es"). Ausente = segue o sistema operacional (ADR-009).
    public string? Language { get; set; }

    // Visão de negócio (Fase 55, ADR-010): o que conta como notificação e como recusa, e o limite de passos.
    public BusinessViewSettings BusinessView { get; set; } = new();

    // Editor do "Abrir no editor" (Fase 56): sem modelo, o VS Code (vscode://file/...).
    public EditorSettings Editor { get; set; } = new();
}

// UrlTemplate com {path} (caminho absoluto do arquivo, com "/") e {line}, para o Visual Studio, o Rider ou outro
// editor que abre por URL. Só da configuração, nunca da requisição.
public class EditorSettings
{
    public string? UrlTemplate { get; set; }
}

// As listas começam nulas: o binder do .NET acrescenta à lista que já existe, então um padrão preenchido aqui
// somaria com o do config.json em vez de ser trocado por ele. Nula ou vazia, vale o padrão do núcleo.
public class BusinessViewSettings
{
    public List<string>? NotificationPatterns { get; set; }
    public List<string>? RejectionReturnTypes { get; set; }
    public int? MaxStepsPerFlow { get; set; }

    public Rastro.Core.Business.BusinessViewOptions ToOptions()
    {
        var defaults = Rastro.Core.Business.BusinessViewOptions.Default;
        return new(
            NotificationPatterns is { Count: > 0 } patterns ? patterns : defaults.NotificationPatterns,
            RejectionReturnTypes is { Count: > 0 } types ? types : defaults.RejectionReturnTypes,
            MaxStepsPerFlow is > 0 and var max ? max : defaults.MaxStepsPerFlow);
    }
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

    // Tempo máximo da leitura de um repositório Angular pelo Node (Fase 26).
    public int TypeScriptTimeoutSeconds { get; set; } = 120;
}
