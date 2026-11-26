using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Rastro.Core.Business;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Business;

// O trecho de código em volta do passo (Fase 59): a janela de 30 linhas que alimenta o hash do "o código mudou" e o
// que o provedor de IA recebe no "sugerir textos". O caminho vem só do RepositoryCatalog (relativo, dentro da raiz,
// só .cs, .ts e .html): nunca lê fora do repositório cadastrado.
public class BusinessCodeReader(RepositoryCatalog catalog, ILogger<BusinessCodeReader> logger)
{
    public const int WindowLines = 30;
    private const int LinesBefore = 3;

    // Chave de leitura: o mesmo arquivo lido de novo só uma vez por chamada.
    public async Task<IReadOnlyDictionary<CodeRef, CodeWindow?>> ReadAsync(IEnumerable<CodeRef> references, CancellationToken ct)
    {
        var files = new Dictionary<(string Repository, string File), string[]?>();
        var result = new Dictionary<CodeRef, CodeWindow?>();
        foreach (var reference in references.Distinct())
        {
            var key = (reference.Repository, reference.File);
            if (!files.TryGetValue(key, out var lines))
            {
                lines = await ReadLinesAsync(reference, ct);
                files[key] = lines;
            }
            result[reference] = lines is null ? null : Window(lines, reference.Line);
        }
        return result;
    }

    private async Task<string[]?> ReadLinesAsync(CodeRef reference, CancellationToken ct)
    {
        var path = catalog.SourceFile(reference.Repository, reference.File);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return await File.ReadAllLinesAsync(path, ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Arquivo do passo ilegível na visão de negócio (repositório {Repository}); o trecho fica de fora.", reference.Repository);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Sem acesso ao arquivo do passo na visão de negócio (repositório {Repository}); o trecho fica de fora.", reference.Repository);
            return null;
        }
    }

    private static CodeWindow? Window(string[] lines, int line)
    {
        if (lines.Length == 0) return null;
        var first = Math.Max(1, line - LinesBefore);
        var last = Math.Min(lines.Length, first + WindowLines - 1);
        if (first > last) return null;

        var plain = new StringBuilder();
        var numbered = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            var text = lines[i - 1].TrimEnd();
            plain.Append(text).Append('\n');
            numbered.AppendLine($"{i,5}  {text}");
        }
        return new CodeWindow(Hash(plain.ToString()), numbered.ToString());
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

// Hash: SHA-256 (hexadecimal, minúsculo) das linhas da janela sem espaço no fim. Numbered: o mesmo trecho com os
// números de linha, para o provedor de IA.
public sealed record CodeWindow(string Hash, string Numbered);
