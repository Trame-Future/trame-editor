using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Righe di tabella disegnate da un solo operatore di testo: è la forma dei documenti dei
/// gestionali — descrizione, calibro e quantità stanno in un unico Tj separati da spazi,
/// mentre l'estrazione le presenta all'utente come colonne distinte. Modificarne una non
/// deve né fallire né portarsi via le altre.
/// Nasce dalla segnalazione del 19/08/2026 su un DDT: "nessun operatore di testo" sulle
/// colonne dopo la prima, e sparizione silenziosa delle altre modificando la prima.
/// </summary>
public class PdfTextEditColumnsTests : IDisposable
{
    /// <summary>Descrizione, calibro e quantità: gli spazi larghi sono ciò che fa leggere
    /// tre colonne dove il PDF ha un solo operatore.</summary>
    private const string Row = "MELINDA 75/80                64                10";

    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-columns-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static PdfTextEditColumnsTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new("Arial#");
    }

    /// <summary>Una riga di tabella e una riga sotto, per accorgersi degli sconfinamenti.</summary>
    private string CreateRowPdf()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        gfx.DrawString(Row, font, XBrushes.Black, new XPoint(50, 100));
        gfx.DrawString("TOTALE", font, XBrushes.Black, new XPoint(50, 130));
        document.Save(path);
        return path;
    }

    private static IReadOnlyList<PdfTextLine> Lines(string path)
    {
        using var inspector = new PdfTextInspector(path);
        return inspector.GetLines(1);
    }

    private static PdfTextLine Line(string path, string text) =>
        Lines(path).Single(l => l.Text == text);

    private string Replace(string source, string text, string newText)
    {
        var target = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        var line = Line(source, text);
        var plan = PdfTextReplacer.PlanFor(source, line, newText);
        PdfTextReplacer.Replace(source, target, line, newText, plan);
        return target;
    }

    [Fact]
    public void Inspector_SplitsTheRowIntoColumns()
    {
        var path = CreateRowPdf();

        var texts = Lines(path).Select(l => l.Text).ToList();

        // Il presupposto del difetto: l'utente vede tre voci separate, il PDF ne disegna una.
        Assert.Contains("MELINDA 75/80", texts);
        Assert.Contains("64", texts);
        Assert.Contains("10", texts);
    }

    [Fact]
    public void Replace_OnFirstColumn_KeepsTheOthers()
    {
        var path = CreateRowPdf();

        var target = Replace(path, "MELINDA 75/80", "GOLDEN 90");

        var texts = Lines(target).Select(l => l.Text).ToList();
        Assert.Contains("GOLDEN 90", texts);
        // Il difetto: sparivano insieme all'operatore, senza un avviso.
        Assert.Contains("64", texts);
        Assert.Contains("10", texts);
        Assert.Contains("TOTALE", texts);
    }

    [Fact]
    public void Replace_OnMiddleColumn_Works()
    {
        var path = CreateRowPdf();

        // Prima rispondeva "nessun operatore di testo": il punto di partenza dell'operatore
        // cade nella prima colonna, non in questa.
        var target = Replace(path, "64", "99");

        var texts = Lines(target).Select(l => l.Text).ToList();
        Assert.Contains("MELINDA 75/80", texts);
        Assert.Contains("99", texts);
        Assert.Contains("10", texts);
    }

    [Fact]
    public void Replace_OnLastColumn_Works()
    {
        var path = CreateRowPdf();

        var target = Replace(path, "10", "25");

        var texts = Lines(target).Select(l => l.Text).ToList();
        Assert.Contains("MELINDA 75/80", texts);
        Assert.Contains("64", texts);
        Assert.Contains("25", texts);
    }

    [Fact]
    public void Replace_LeavesTheOtherColumnsExactlyWhereTheyWere()
    {
        var path = CreateRowPdf();
        var before = Lines(path).ToDictionary(l => l.Text, l => (l.Left, l.BaselineY));

        // Sostituzione più corta dell'originale: se il resto scorresse, si vedrebbe qui.
        var target = Replace(path, "MELINDA 75/80", "X");

        var after = Lines(target).ToDictionary(l => l.Text, l => (l.Left, l.BaselineY));
        foreach (var text in new[] { "64", "10", "TOTALE" })
        {
            Assert.Equal(before[text].Left, after[text].Left, 2);
            Assert.Equal(before[text].BaselineY, after[text].BaselineY, 2);
        }
    }

    [Fact]
    public void Replace_WithEmptyText_ErasesOnlyThatColumn()
    {
        var path = CreateRowPdf();

        var target = Replace(path, "64", "");

        var texts = Lines(target).Select(l => l.Text).ToList();
        Assert.DoesNotContain("64", texts);
        Assert.Contains("MELINDA 75/80", texts);
        Assert.Contains("10", texts);
    }

    [Fact]
    public void ReplaceMany_OnTwoColumnsOfTheSameRow_TouchesOnlyThose()
    {
        var path = CreateRowPdf();
        var target = Path.Combine(_dir, "molte.pdf");
        var edits = new[]
        {
            (Line(path, "MELINDA 75/80"), "GOLDEN 90"),
            (Line(path, "10"), "25"),
        };

        var result = PdfTextReplacer.ReplaceMany(path, target, edits);

        Assert.Empty(result.SkippedLines);
        Assert.Equal(2, result.LinesReplaced);
        var texts = Lines(target).Select(l => l.Text).ToList();
        Assert.Contains("GOLDEN 90", texts);
        Assert.Contains("64", texts);
        Assert.Contains("25", texts);
    }
}
