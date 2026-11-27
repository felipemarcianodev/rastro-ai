using System.Xml;
using System.Xml.Linq;

namespace Rastro.Core.Business;

// Desenho de origem do .drawio aberto no editor (Fases 65 e 66). Enum com valor explícito: viaja como int no JSON.
public enum DrawioSource
{
    Graph = 0,
    Map = 1,
    Business = 2
}

// O que o draw.io guarda por levantamento e por origem (Fases 65 e 66): as posições dos balões que têm rastroId e as
// anotações (células sem rastroId). Não se guarda o XML inteiro: ao abrir, o Rastro gera o .drawio do código de novo e
// aplica isto por cima. Chave (levantamento, origem); some junto com o levantamento (cascata).
public class DrawioLayout
{
    public Guid SurveyId { get; private set; }
    public DrawioSource Source { get; private set; }
    public string CellsJson { get; private set; } = "[]";
    public string? AnnotationsXml { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private DrawioLayout() { }

    public static DrawioLayout Create(Guid surveyId, DrawioSource source, string cellsJson, string? annotationsXml, DateTimeOffset now) =>
        new() { SurveyId = surveyId, Source = source, CellsJson = cellsJson, AnnotationsXml = annotationsXml, UpdatedAt = now };

    // Troca tudo o que havia para a origem.
    public void Replace(string cellsJson, string? annotationsXml, DateTimeOffset now)
    {
        CellsJson = cellsJson;
        AnnotationsXml = annotationsXml;
        UpdatedAt = now;
    }
}

// Posição e tamanho de um balão do desenho (rastroId é o id do Rastro gravado na célula).
public record DrawioCellDto(string RastroId, double X, double Y, double Width, double Height);

// Formato do PUT (corpo) e do GET (UpdatedAt vem preenchido só no GET, nulo quando nada foi guardado).
public record DrawioLayoutDto(IReadOnlyList<DrawioCellDto> Cells, string? Annotations, DateTimeOffset? UpdatedAt = null);

// Limites da gravação. As anotações são XML do draw.io que a tela vai devolver ao editor: aqui só se confere que é
// bem formado e que só traz células soltas (mxCell), nunca balão do fluxo (rastroId).
public static class DrawioLayoutLimits
{
    public const int MaxCells = 2000;
    public const int MaxIdLength = 128;
    public const double MaxCoordinate = 100000;
    public const double MaxSize = 10000;
    public const int MaxAnnotationsBytes = 512 * 1024;

    public static bool IsValid(DrawioLayoutDto? layout)
    {
        if (layout is null) return false;
        var cells = layout.Cells ?? [];
        if (cells.Count > MaxCells) return false;
        foreach (var cell in cells)
        {
            if (cell is null || string.IsNullOrWhiteSpace(cell.RastroId) || cell.RastroId.Length > MaxIdLength) return false;
            if (!Coordinate(cell.X) || !Coordinate(cell.Y)) return false;
            if (!Size(cell.Width) || !Size(cell.Height)) return false;
        }
        return ValidAnnotations(layout.Annotations);
    }

    public static bool IsDefined(int source) => Enum.IsDefined(typeof(DrawioSource), source);

    // Também usada pelas anotações do quadro de fluxos (Fase 68): mesmas regras.
    public static bool ValidAnnotations(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return true;
        if (System.Text.Encoding.UTF8.GetByteCount(xml) > MaxAnnotationsBytes) return false;
        try
        {
            // Sem DTD e sem resolver entidade externa: o texto vem da requisição.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "root") return false;
            return root.Elements().All(e => e.Name.LocalName == "mxCell" && e.Attribute("rastroId") is null)
                && !root.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
        }
        catch (XmlException)
        {
            // XML malformado é entrada inválida do usuário, não falha do servidor: vira o 400 do contrato.
            return false;
        }
    }

    private static bool Coordinate(double value) => double.IsFinite(value) && value is >= -MaxCoordinate and <= MaxCoordinate;

    private static bool Size(double value) => double.IsFinite(value) && value > 0 && value <= MaxSize;
}
