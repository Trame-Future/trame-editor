using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Tables;

namespace TrameEditor.Core.Tests.Tables;

/// <summary>
/// Estrazione per modello: si segna una volta dove stanno i campi su un esemplare, e da lì
/// in poi i documenti fatti allo stesso modo restituiscono quei campi.
/// </summary>
public class TemplateExtractorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-modello-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static TemplateExtractorTests()
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

    /// <summary>Una fattura ridotta all'osso. Le coordinate di PDFsharp hanno l'origine in
    /// alto; quelle dei modelli in basso, come il PDF: su una pagina A4 alta 842 punti, una
    /// scritta disegnata a y=100 sta a 742 punti dal fondo.</summary>
    private string Invoice(string number, string customer, string total, string? folder = null)
    {
        var path = Path.Combine(folder ?? _dir, $"fattura-{number}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 11);
        gfx.DrawString($"Fattura n. {number}", font, XBrushes.Black, new XPoint(400, 100));
        gfx.DrawString(customer, font, XBrushes.Black, new XPoint(60, 160));
        gfx.DrawString($"Totale: {total}", font, XBrushes.Black, new XPoint(400, 700));
        document.Save(path);
        return path;
    }

    /// <summary>
    /// Il modello si costruisce <b>misurando un esemplare</b>, non scrivendo le coordinate a
    /// mano: è anche il modo in cui lo costruirà l'utente, segnando i campi su un documento
    /// vero. Le coordinate assolute legavano il test a dove il font di sistema mette il
    /// testo, e su un'altra macchina i campi uscivano tutti vuoti.
    /// </summary>
    private ExtractionTemplate Template()
    {
        // L'esemplare va fuori dalla cartella di lavoro: i test che la scandiscono per intero
        // conterebbero anche lui.
        var apart = Directory.CreateDirectory(Path.Combine(_dir, "esemplare")).FullName;
        var sample = Invoice("000", "Esemplare", "0,00", apart);
        using var inspector = new PdfTextInspector(sample);
        var lines = inspector.GetLines(1);

        return new ExtractionTemplate("Fattura fornitore",
        [
            Field("Numero", lines, "Fattura n."),
            Field("Cliente", lines, "Esemplare"),
            Field("Totale", lines, "Totale:"),
        ]);
    }

    /// <summary>Un campo largo quanto serve attorno alla riga che contiene quel testo.</summary>
    private static TemplateField Field(string name, IReadOnlyList<PdfTextLine> lines, string contains)
    {
        var line = lines.First(l => l.Text.Contains(contains));
        return new TemplateField(name, 1, line.Left - 8, line.Bottom - 6,
            Math.Max(line.Width + 120, 160), line.Height + 12);
    }

    [Fact]
    public void ReadsTheFieldsMarkedOnTheTemplate()
    {
        var pdf = Invoice("412", "Ortofrutta Melinda SRL", "1.250,00");

        var result = TemplateExtractor.Apply(Template(), pdf);

        Assert.Equal("Fattura n. 412", result.Fields["Numero"]);
        Assert.Equal("Ortofrutta Melinda SRL", result.Fields["Cliente"]);
        Assert.Equal("Totale: 1.250,00", result.Fields["Totale"]);
        Assert.True(result.Complete);
    }

    /// <summary>Un campo vuoto non è un errore — può esserlo davvero — ma va dichiarato,
    /// o chi riceve la tabella crede che il dato non esistesse.</summary>
    [Fact]
    public void AnEmptyFieldIsReported_NotHidden()
    {
        var pdf = Invoice("500", "", "90,00");

        var result = TemplateExtractor.Apply(Template(), pdf);

        Assert.Equal(string.Empty, result.Fields["Cliente"]);
        Assert.Contains("Cliente", result.MissingFields);
        Assert.False(result.Complete);
    }

    [Fact]
    public void AppliesToAWholeFolder_InAStableOrder()
    {
        Invoice("003", "Terzo", "3,00");
        Invoice("001", "Primo", "1,00");
        Invoice("002", "Secondo", "2,00");

        var results = TemplateExtractor.ApplyToFolder(Template(), _dir);

        Assert.Equal(3, results.Count);
        Assert.Equal(["Primo", "Secondo", "Terzo"], results.Select(r => r.Fields["Cliente"]));
    }

    /// <summary>Un file rotto in mezzo a cento non deve fermare gli altri novantanove.</summary>
    [Fact]
    public void ABrokenFile_DoesNotStopTheOthers()
    {
        Invoice("001", "Buona", "1,00");
        File.WriteAllText(Path.Combine(_dir, "zz-rotta.pdf"), "questo non è un PDF");

        var results = TemplateExtractor.ApplyToFolder(Template(), _dir);

        Assert.Equal(2, results.Count);
        Assert.Equal("Buona", results[0].Fields["Cliente"]);
        Assert.False(results[1].Complete);
        Assert.Contains("non leggibile", results[1].Fields["Numero"]);
    }

    [Fact]
    public void BecomesATable_OneRowPerDocument()
    {
        Invoice("001", "Primo", "1,00");
        Invoice("002", "Secondo", "2,00");
        var template = Template();

        var table = TemplateExtractor.ToTable(template, TemplateExtractor.ApplyToFolder(template, _dir));

        Assert.Equal(["File", "Numero", "Cliente", "Totale"], table.Rows[0]);
        Assert.Equal(3, table.RowCount); // intestazione + due documenti
        Assert.Equal("Secondo", table.Rows[2][2]);
    }

    [Fact]
    public void TheTemplateSurvivesSavingAndLoading()
    {
        var path = Path.Combine(_dir, "modello.json");
        Template().Save(path);

        var loaded = ExtractionTemplate.Load(path);

        Assert.Equal("Fattura fornitore", loaded.Name);
        Assert.Equal(3, loaded.Fields.Count);
        Assert.Equal("Numero", loaded.Fields[0].Name);
        // Il file si corregge a mano: deve restare leggibile da una persona.
        Assert.Contains("\"Numero\"", File.ReadAllText(path));
    }

    [Fact]
    public void ATemplateWithoutFields_IsRefusedWithAReason()
    {
        var path = Path.Combine(_dir, "vuoto.json");
        new ExtractionTemplate("Niente", []).Save(path);

        var error = Assert.Throws<InvalidOperationException>(() => ExtractionTemplate.Load(path));

        Assert.Contains("nessun campo", error.Message);
    }

    /// <summary>Documento più corto dell'esemplare: i campi delle pagine che mancano restano
    /// vuoti e dichiarati, invece di far fallire tutto.</summary>
    [Fact]
    public void AShorterDocument_LeavesTheMissingPagesEmpty()
    {
        var pdf = Invoice("700", "Cliente", "7,00");
        // Il campo della prima pagina si misura come gli altri; quello della seconda indica
        // una pagina che in questo documento non c'è.
        var measured = Template().Fields.First(f => f.Name == "Numero");
        var template = new ExtractionTemplate("Due pagine",
        [
            measured,
            new TemplateField("Allegato", 2, 45, 690, 220, 35),
        ]);

        var result = TemplateExtractor.Apply(template, pdf);

        Assert.Equal("Fattura n. 700", result.Fields["Numero"]);
        Assert.Equal(string.Empty, result.Fields["Allegato"]);
        Assert.Contains("Allegato", result.MissingFields);
    }
}
