using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rastro.Core.Localization;

namespace Rastro.Tests.Localization;

// Cenários CORE-LANG-NNN: textos do servidor nos três idiomas (Fase 49, ADR-009).
public partial class ResourceParityTests
{
    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    private static IEnumerable<string> NeutralFiles() =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.BackendRoot(), "src"), "Messages.resx", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "Resources");

    private static string[] Markers(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal)];

    [Fact] // CORE-LANG-001
    public void Resx_TodoProjetoComTextoTemOsTresIdiomas()
    {
        var files = NeutralFiles().ToList();
        Assert.NotEmpty(files);
        foreach (var neutral in files)
        {
            var folder = Path.GetDirectoryName(neutral)!;
            Assert.True(File.Exists(Path.Combine(folder, "Messages.pt-BR.resx")), $"Falta Messages.pt-BR.resx em {folder}");
            Assert.True(File.Exists(Path.Combine(folder, "Messages.es.resx")), $"Falta Messages.es.resx em {folder}");
        }
    }

    [Fact] // CORE-LANG-002
    public void Resx_MesmasChavesEMesmosMarcadoresNosTresIdiomas()
    {
        foreach (var neutral in NeutralFiles())
        {
            var folder = Path.GetDirectoryName(neutral)!;
            var english = Read(neutral);
            foreach (var suffix in new[] { "pt-BR", "es" })
            {
                var other = Read(Path.Combine(folder, $"Messages.{suffix}.resx"));
                Assert.Equal(english.Keys.Order(StringComparer.Ordinal), other.Keys.Order(StringComparer.Ordinal));
                foreach (var (key, text) in english)
                {
                    Assert.False(string.IsNullOrWhiteSpace(other[key]), $"{suffix}: {key} vazia");
                    Assert.True(Markers(text).SequenceEqual(Markers(other[key])), $"{suffix}: marcadores de {key} diferem");
                }
            }
        }
    }

    [Fact] // CORE-LANG-003
    public void LocalizedText_CulturaExplicita_DaOTextoDoIdiomaPedido()
    {
        Assert.StartsWith("Invalid language", LocalizedText.Get("Language.Invalid", UiLanguage.English));
        Assert.StartsWith("Idioma inválido", LocalizedText.Get("Language.Invalid", UiLanguage.Portuguese));
        Assert.StartsWith("Idioma no válido", LocalizedText.Get("Language.Invalid", UiLanguage.Spanish));
        Assert.Equal("Chave.Inexistente", LocalizedText.Get("Chave.Inexistente", UiLanguage.Spanish));
    }

    [Theory] // CORE-LANG-004
    [InlineData("pt-BR", UiLanguage.Portuguese)]
    [InlineData("pt-PT", UiLanguage.Portuguese)]
    [InlineData("es-MX", UiLanguage.Spanish)]
    [InlineData("es", UiLanguage.Spanish)]
    [InlineData("en-GB", UiLanguage.English)]
    public void UiLanguages_FromCode_AceitaOPrefixoDoIdioma(string code, UiLanguage expected) =>
        Assert.Equal(expected, UiLanguages.FromCode(code));

    [Fact] // CORE-LANG-005
    public void UiLanguages_IdiomaDoSistemaDesconhecido_CaiNoIngles()
    {
        Assert.Equal(UiLanguage.English, UiLanguages.FromSystem(new CultureInfo("de-DE")));
        Assert.Equal(UiLanguage.Spanish, UiLanguages.FromSystem(new CultureInfo("es-AR")));
        Assert.Null(UiLanguages.FromCode("zh-CN"));
        Assert.Equal(["en-US", "pt-BR", "es"], UiLanguages.All.Select(l => l.Code()));
    }
}
