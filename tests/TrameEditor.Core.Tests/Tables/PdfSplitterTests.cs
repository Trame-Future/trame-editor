using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Tables;
using UglyToad.PdfPig;

namespace TrameEditor.Core.Tests.Tables;

/// <summary>
/// Divisione delle stampe cumulative: il gestionale stampa duecento fatture in un PDF solo,
/// e chi lo riceve deve ritrovare il singolo documento.
/// </summary>
public class PdfSplitterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-dividi-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static PdfSplitterTests()
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

    /// <summary>Una stampa cumulativa: una pagina per voce, con il numero in alto a destra.
    /// Una voce <c>null</c> fa una pagina senza numero, come la seconda pagina di un
    /// documento lungo.</summary>
    private string Batch(params string?[] numbersPerPage)
    {
        var path = Path.Combine(_dir, "stampa.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var font = new XFont("Arial", 11);
        foreach (var number in numbersPerPage)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            if (number is not null)
                gfx.DrawString($"Doc {number}", font, XBrushes.Black, new XPoint(420, 100));
            gfx.DrawString("Corpo del documento", font, XBrushes.Black, new XPoint(60, 300));
        }
        document.Save(path);
        return path;
    }

    /// <summary>
    /// L'area del numero, ricavata <b>dal documento stesso</b> invece che fissata a mano.
    /// Scriverla a coordinate assolute rendeva i test dipendenti da dove il font di sistema
    /// posiziona il testo: verdi in locale e rossi sul runner, dove bastava qualche punto di
    /// differenza per far cadere la scritta fuori dall'area.
    /// </summary>
    private static TemplateField NumberArea(string source)
    {
        using var inspector = new PdfTextInspector(source);
        var line = inspector.GetLines(1).FirstOrDefault(l => l.Text.StartsWith("Doc"));
        if (line is null)
            return new TemplateField("Numero", 1, 410, 725, 160, 25); // pagina muta: area qualsiasi

        // Un margine attorno alla riga trovata: l'area deve prendere la scritta su tutte le
        // pagine, non solo su quella misurata.
        return new TemplateField("Numero", 1,
            line.Left - 10, line.Bottom - 6, line.Width + 40, line.Height + 12);
    }

    private string Output(string name = "divisi") => Path.Combine(_dir, name);

    [Fact]
    public void SplitsWhenTheValueChanges()
    {
        var source = Batch("001", "002", "003");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(3, result.Parts.Count);
        Assert.Equal(["Doc 001", "Doc 002", "Doc 003"], result.Parts.Select(p => p.Value));
        Assert.All(result.Parts, p => Assert.True(File.Exists(p.OutputPath)));
        Assert.All(result.Parts, p => Assert.Equal(1, p.PageCount));
    }

    [Fact]
    public void KeepsConsecutivePagesOfTheSameDocumentTogether()
    {
        var source = Batch("001", "001", "002");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(2, result.Parts[0].PageCount);
        using var first = PdfDocument.Open(result.Parts[0].OutputPath);
        Assert.Equal(2, first.NumberOfPages);
    }

    /// <summary>Le fatture di più pagine ripetono il numero solo sulla prima: una pagina muta
    /// continua il documento di prima, non ne comincia uno nuovo.</summary>
    [Fact]
    public void APageWithoutTheValue_ContinuesThePreviousDocument()
    {
        var source = Batch("001", null, "002");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(2, result.Parts[0].PageCount);
        Assert.Equal(1, result.PagesWithoutValue);
        Assert.Contains(result.Warnings, w => w.Contains("non avevano nessun testo"));
    }

    [Fact]
    public void TheNameOfTheFileComesFromTheValue()
    {
        var source = Batch("412", "413");

        var result = PdfSplitter.Split(source, NumberArea(source), Output(), namePrefix: "fattura-");

        Assert.Contains(result.Parts, p => Path.GetFileName(p.OutputPath) == "fattura-Doc 412.pdf");
    }

    /// <summary>Il valore arriva dal documento, cioè da fuori: con una barra dentro
    /// scriverebbe in un'altra cartella.</summary>
    [Theory]
    [InlineData("2026/412", "2026_412")]
    // Le barre diventano trattini bassi e i punti iniziali cadono: niente risalita di cartelle.
    [InlineData("..\\..\\altrove", "_.._altrove")]
    [InlineData("   ", "documento")]
    public void TheValueIsCleanedBeforeBecomingAFileName(string value, string expected)
    {
        Assert.Equal(expected, PdfSplitter.SafeName(value));
    }

    /// <summary>Lo stesso valore che ricompare staccato: non si può sapere se è lo stesso
    /// documento interrotto o un altro, quindi si numerano e lo si dice.</summary>
    [Fact]
    public void AValueThatComesBackLater_IsNumberedAndReported()
    {
        var source = Batch("001", "002", "001");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(3, result.Parts.Count);
        Assert.Contains(result.Warnings, w => w.Contains("ricompare"));
        Assert.Contains(result.Parts, p => Path.GetFileName(p.OutputPath).Contains("-2"));
    }

    [Fact]
    public void DoesNotOverwriteByItself()
    {
        var source = Batch("001");
        var folder = Output();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Doc 001.pdf"), "roba da non perdere");

        var error = Assert.Throws<InvalidOperationException>(
            () => PdfSplitter.Split(source, NumberArea(source), folder));

        Assert.Contains("Esiste già", error.Message);
        Assert.Equal("roba da non perdere", File.ReadAllText(Path.Combine(folder, "Doc 001.pdf")));
    }

    /// <summary>
    /// Se ogni pagina diventa un documento, l'area scelta non contiene un identificativo ma
    /// testo che cambia da sé. Il risultato è coerente con quanto chiesto, e proprio per
    /// questo va detto: nessuno divide una stampa cumulativa per avere una pagina per file.
    /// Successo provando sul manuale, dove sono usciti 15 file su 15 pagine.
    /// </summary>
    [Fact]
    public void WhenEveryPageBecomesADocument_ItSaysSo()
    {
        var source = Batch("001", "002", "003", "004", "005");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(5, result.Parts.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Ogni pagina è diventata un documento"));
    }

    /// <summary>Su una stampa vera, invece, l'avviso non deve comparire.</summary>
    [Fact]
    public void ARealBatch_DoesNotGetThatWarning()
    {
        var source = Batch("001", "001", "002", "002", "003");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(3, result.Parts.Count);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Ogni pagina"));
    }

    [Fact]
    public void PagesWithoutAnyValueAtTheStart_GoIntoTheirOwnFile()
    {
        var source = Batch(null, "001");

        var result = PdfSplitter.Split(source, NumberArea(source), Output());

        Assert.Equal(2, result.Parts.Count);
        Assert.Contains(result.Parts, p => Path.GetFileName(p.OutputPath) == "senza-valore.pdf");
    }
}
