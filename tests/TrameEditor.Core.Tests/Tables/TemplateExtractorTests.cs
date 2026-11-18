using PdfSharp.Drawing;
using PdfSharp.Fonts;
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
    private string Invoice(string number, string customer, string total)
    {
        var path = Path.Combine(_dir, $"fattura-{number}.pdf");
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

    private static ExtractionTemplate Template() => new("Fattura fornitore",
    [
        new TemplateField("Numero", 1, 395, 730, 160, 20),
        new TemplateField("Cliente", 1, 55, 670, 300, 20),
        new TemplateField("Totale", 1, 395, 130, 160, 20),
    ]);

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
        var template = new ExtractionTemplate("Due pagine",
        [
            new TemplateField("Numero", 1, 395, 730, 160, 20),
            new TemplateField("Allegato", 2, 60, 700, 200, 20),
        ]);

        var result = TemplateExtractor.Apply(template, pdf);

        Assert.Equal("Fattura n. 700", result.Fields["Numero"]);
        Assert.Equal(string.Empty, result.Fields["Allegato"]);
        Assert.Contains("Allegato", result.MissingFields);
    }
}
