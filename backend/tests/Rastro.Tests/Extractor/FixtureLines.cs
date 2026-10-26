namespace Rastro.Tests.Extractor;

// Linha (base 1) de um trecho num arquivo da fixture, sem número fixo no teste.
internal static class FixtureLines
{
    public static int LineOf(string relativeFile, string after, string contains)
    {
        var lines = File.ReadAllLines(Path.Combine(FixtureWorkspace.FixturesRoot, relativeFile));
        var start = Array.FindIndex(lines, l => l.Contains(after, StringComparison.Ordinal));
        Assert.True(start >= 0, $"\"{after}\" não está em {relativeFile}.");
        var index = Array.FindIndex(lines, start, l => l.Contains(contains, StringComparison.Ordinal));
        Assert.True(index >= 0, $"\"{contains}\" não está depois de \"{after}\" em {relativeFile}.");
        return index + 1;
    }
}
