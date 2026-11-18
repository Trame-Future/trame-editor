using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Cerca e sostituisci su tutto il documento. È la cosa più facile da sbagliare che
/// TrameEditor sappia fare: prima si cerca e si mostra, poi si applica solo quello che è
/// stato scelto.
/// </summary>
public class PdfFindReplaceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-trova-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static PdfFindReplaceTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new("Arial#");
    }

    /// <summary>Un documento con il testo cercato su più pagine.</summary>
    private string Document(params string[][] pages)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var font = new XFont("Arial", 12);
        foreach (var lines in pages)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var y = 100.0;
            foreach (var line in lines)
            {
                gfx.DrawString(line, font, XBrushes.Black, new XPoint(50, y));
                y += 25;
            }
        }
        document.Save(path);
        return path;
    }

    private string Target(string name = "uscita.pdf") => Path.Combine(_dir, name);

    private static List<string> TextsOf(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var texts = new List<string>();
        for (var page = 1; page <= inspector.PageCount; page++)
            texts.AddRange(inspector.GetLines(page).Select(l => l.Text));
        return texts;
    }

    [Fact]
    public void FindsAcrossAllPages()
    {
        var pdf = Document(
            ["Fornitore: Rossi SRL", "Importo 100"],
            ["Nota per Rossi SRL", "Fine"]);

        var matches = PdfFindReplace.Find(pdf, "Rossi SRL", "Bianchi SPA");

        Assert.Equal(2, matches.Count);
        Assert.Equal("Fornitore: Bianchi SPA", matches[0].After);
        Assert.Equal(2, matches[1].Line.PageNumber);
    }

    [Fact]
    public void ReplacesOnlyTheChosenOccurrences()
    {
        var pdf = Document(["Rossi qui", "Rossi anche qui", "Altro"]);
        var matches = PdfFindReplace.Find(pdf, "Rossi", "Bianchi");
        var target = Target();

        // Si applica solo la prima: chi sceglie deve poter scegliere.
        var result = PdfFindReplace.Replace(pdf, target, [matches[0]]);

        Assert.Equal(1, result.LinesChanged);
        var texts = TextsOf(target);
        Assert.Contains("Bianchi qui", texts);
        Assert.Contains("Rossi anche qui", texts);
    }

    [Fact]
    public void CountsEveryOccurrenceInsideTheSameLine()
    {
        var pdf = Document(["Rossi e Rossi insieme"]);

        var matches = PdfFindReplace.Find(pdf, "Rossi", "X");

        Assert.Single(matches);
        Assert.Equal(2, matches[0].Occurrences);
        Assert.Equal("X e X insieme", matches[0].After);
    }

    [Fact]
    public void IgnoresCaseUnlessAsked()
    {
        var pdf = Document(["ROSSI maiuscolo", "rossi minuscolo"]);

        Assert.Equal(2, PdfFindReplace.Find(pdf, "Rossi", "X").Count);
        Assert.Empty(PdfFindReplace.Find(pdf, "Rossi", "X", caseSensitive: true));
    }

    /// <summary>Senza "parola intera", cercare «via» cambierebbe anche «ovvia».</summary>
    [Fact]
    public void WholeWord_DoesNotTouchWordsThatContainIt()
    {
        var pdf = Document(["una cosa ovvia", "via Roma 1"]);

        var all = PdfFindReplace.Find(pdf, "via", "strada");
        var whole = PdfFindReplace.Find(pdf, "via", "strada", wholeWord: true);

        Assert.Equal(2, all.Count);
        Assert.Single(whole);
        Assert.Equal("strada Roma 1", whole[0].After);
    }

    [Fact]
    public void ReplacingWithNothing_EmptiesTheText()
    {
        var pdf = Document(["Riservato: Rossi", "Altro"]);
        var matches = PdfFindReplace.Find(pdf, "Rossi", "");
        var target = Target();

        PdfFindReplace.Replace(pdf, target, matches);

        Assert.DoesNotContain(TextsOf(target), t => t.Contains("Rossi"));
    }

    [Fact]
    public void RefusesAnEmptySearch()
    {
        var pdf = Document(["qualcosa"]);

        Assert.Throws<ArgumentException>(() => PdfFindReplace.Find(pdf, "", "x"));
    }

    [Fact]
    public void RefusesToApplyNothing()
    {
        var pdf = Document(["qualcosa"]);

        var error = Assert.Throws<InvalidOperationException>(
            () => PdfFindReplace.Replace(pdf, Target(), []));

        Assert.Contains("nessuna occorrenza", error.Message);
    }

    /// <summary>La stessa riga due volte in elenco verrebbe riscritta due volte, e la seconda
    /// cancellerebbe la prima: si tiene la prima e lo si dice.</summary>
    [Fact]
    public void TheSameLineTwice_IsWrittenOnce_AndReported()
    {
        var pdf = Document(["Rossi e Rossi"]);
        var matches = PdfFindReplace.Find(pdf, "Rossi", "Bianchi");
        var doubled = new List<FindMatch> { matches[0], matches[0] };

        var result = PdfFindReplace.Replace(pdf, Target(), doubled);

        Assert.Equal(1, result.LinesChanged);
        Assert.Contains(result.Warnings, w => w.Contains("più di una volta"));
        Assert.Contains("Bianchi e Bianchi", TextsOf(Target()));
    }

    [Fact]
    public void NothingFound_IsAnEmptyListNotAnError()
    {
        var pdf = Document(["niente di interessante"]);

        Assert.Empty(PdfFindReplace.Find(pdf, "Rossi", "Bianchi"));
    }
}
