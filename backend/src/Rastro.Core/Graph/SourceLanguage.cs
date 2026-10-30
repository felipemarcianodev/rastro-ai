namespace Rastro.Core.Graph;

// Linguagem do trecho de um levantamento (Fase 26). Viaja como int no JSON.
public enum SourceLanguage
{
    DotNet = 0,
    Angular = 1
}

public static class SourceLanguages
{
    // O ponto de partida de um trecho é uma solution/projeto (.NET) ou um tsconfig (Angular):
    // a linguagem sai do nome do arquivo, sem guardar mais uma coluna no banco.
    public static SourceLanguage FromPath(string? path)
    {
        var name = Path.GetFileName(path ?? string.Empty);
        var isTsConfig = name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        return isTsConfig ? SourceLanguage.Angular : SourceLanguage.DotNet;
    }
}
