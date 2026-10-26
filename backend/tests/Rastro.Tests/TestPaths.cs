namespace Rastro.Tests;

internal static class TestPaths
{
    // Pasta backend/ (onde está o Rastro.slnx), subindo a partir da saída dos testes.
    public static string BackendRoot() => FindUp("Rastro.slnx");

    // Raiz do repositório (onde está o arquivo VERSION).
    public static string RepoRoot() => FindUp("VERSION");

    private static string FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, fileName)))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException($"{fileName} não encontrado acima da pasta de testes.");
    }
}
