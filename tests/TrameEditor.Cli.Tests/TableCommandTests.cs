using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Cli.Commands;

namespace TrameEditor.Cli.Tests;

/// <summary>
/// Il comando che estrae le tabelle: il giro che farebbe chi deve tirare fuori le righe di
/// un documento di trasporto senza ricopiarle a mano.
/// </summary>
public class TableCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-tabella-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static TableCommandTests()
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

    /// <summary>Un documento di trasporto ridotto all'osso: un'intestazione e tre righe.</summary>
    private string DeliveryNote()
    {
        var path = Path.Combine(_dir, "ddt.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 11);
        gfx.DrawString("Documento di trasporto n. 412", font, XBrushes.Black, new XPoint(50, 60));
        var rows = new[]
        {
            ("Articolo", "Calibro", "Colli"),
            ("Mele Melinda", "75/80", "64"),
            ("Pere Abate", "60/65", "10"),
            ("Kiwi Hayward", "30/33", "8"),
        };
        var y = 100.0;
        foreach (var (a, b, c) in rows)
        {
            gfx.DrawString(a, font, XBrushes.Black, new XPoint(50, y));
            gfx.DrawString(b, font, XBrushes.Black, new XPoint(250, y));
            gfx.DrawString(c, font, XBrushes.Black, new XPoint(400, y));
            y += 20;
        }
        document.Save(path);
        return path;
    }

    private static IDictionary<string, object?> Run(params string[] args) =>
        (IDictionary<string, object?>)TableCommand.Run(CommandLine.Parse(args));

    private static IDictionary<string, object?> FirstTable(IDictionary<string, object?> result) =>
        ((IEnumerable<object>)result["tabelle"]!).Cast<IDictionary<string, object?>>().First();

    [Fact]
    public void ReadsTheRowsOfADeliveryNote()
    {
        var result = Run("tabella", DeliveryNote());

        Assert.True((bool)result["ok"]!);
        Assert.Equal(1, result["tabelleTrovate"]);
        var table = FirstTable(result);
        Assert.Equal(4, table["righe"]);
        Assert.Equal(3, table["colonne"]);
        // Il numero del documento sta sopra la tabella: non è una riga di dati, ed è giusto
        // che sia dichiarato come rimasto fuori.
        Assert.Equal(1, table["righeFuoriTabella"]);
    }

    [Fact]
    public void SavesToCsv_WithTheSeparatorExcelInItalianExpects()
    {
        var target = Path.Combine(_dir, "righe.csv");

        var result = Run("tabella", DeliveryNote(), target);

        Assert.Equal("CSV", result["formato"]);
        var text = File.ReadAllText(target);
        Assert.Contains("Articolo;Calibro;Colli", text);
        Assert.Contains("Mele Melinda;75/80;64", text);
    }

    [Fact]
    public void SavesToExcel_WhenTheNameSaysSo()
    {
        var target = Path.Combine(_dir, "righe.xlsx");

        var result = Run("tabella", DeliveryNote(), target);

        Assert.Equal("XLSX", result["formato"]);
        using var workbook = new XLWorkbook(target);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("Articolo", sheet.Cell(1, 1).GetString());
        Assert.Equal("Kiwi Hayward", sheet.Cell(4, 1).GetString());
    }

    [Fact]
    public void DoesNotOverwriteByItself()
    {
        var target = Path.Combine(_dir, "occupato.csv");
        File.WriteAllText(target, "roba da non perdere");

        var error = Assert.Throws<UsageException>(() => Run("tabella", DeliveryNote(), target));

        Assert.Contains("--sovrascrivi", error.Message);
        Assert.Equal("roba da non perdere", File.ReadAllText(target));
    }

    /// <summary>Se non c'è niente da salvare va detto, invece di lasciare un file vuoto che
    /// sembra un lavoro riuscito.</summary>
    [Fact]
    public void SaysSo_WhenThereIsNoTableToSave()
    {
        var path = Path.Combine(_dir, "prosa.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("Questa pagina contiene soltanto una frase.",
                new XFont("Arial", 11), XBrushes.Black, new XPoint(50, 100));
            document.Save(path);
        }
        var target = Path.Combine(_dir, "niente.csv");

        var error = Assert.Throws<InvalidOperationException>(() => Run("tabella", path, target));

        Assert.Contains("nessuna tabella", error.Message);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void RejectsASeparatorThatIsNotOneCharacter()
    {
        var error = Assert.Throws<UsageException>(() =>
            Run("tabella", DeliveryNote(), Path.Combine(_dir, "x.csv"), "--separatore", "abc"));

        Assert.Contains("un carattere solo", error.Message);
    }
}
