using System.Text;
using ClosedXML.Excel;
using TrameEditor.Core.Tables;

namespace TrameEditor.Core.Tests.Tables;

/// <summary>
/// Salvataggio della griglia. Le prove guardano soprattutto i dettagli che decidono se il
/// file si apre bene in Excel su un computer italiano: separatore, firma della codifica,
/// virgolette, e i valori che Excel sarebbe tentato di reinterpretare.
/// </summary>
public class TableExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-export-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Target(string name) => Path.Combine(_dir, name);

    private static PdfTable Table(params string[][] rows) =>
        new(1, rows.Select(r => (IReadOnlyList<string>)r).ToList(), [0, 100], 0);

    [Fact]
    public void Csv_UsesSemicolon_SoExcelInItalianSplitsTheColumns()
    {
        var path = Target("tabella.csv");

        TableExporter.Export(Table(["Articolo", "Prezzo"], ["Mele", "3,50"]), path);

        var text = File.ReadAllText(path);
        Assert.Contains("Articolo;Prezzo", text);
        // Con la virgola come separatore, "3,50" spaccherebbe la riga in tre celle.
        Assert.Contains("Mele;3,50", text);
    }

    /// <summary>Senza la firma in testa Excel legge il file come se fosse nella vecchia
    /// codifica di Windows, e ogni accento diventa un carattere strano.</summary>
    [Fact]
    public void Csv_StartsWithTheUtf8Signature()
    {
        var path = Target("accenti.csv");

        TableExporter.Export(Table(["Città", "Qualità"], ["Caserta", "però"]), path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("Città", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void Csv_QuotesWhatWouldBreakTheFormat()
    {
        var path = Target("virgolette.csv");

        TableExporter.Export(Table(
            ["Descrizione", "Nota"],
            ["Mele; Melinda", "Il \"vero\" prezzo"]), path);

        var lines = File.ReadAllLines(path);
        Assert.Contains("\"Mele; Melinda\"", lines[1]);
        Assert.Contains("\"Il \"\"vero\"\" prezzo\"", lines[1]);
    }

    [Fact]
    public void Csv_CanUseTheComma_WhenAsked()
    {
        var path = Target("virgola.csv");

        TableExporter.Export(Table(["A", "B"]), path, separator: ',');

        Assert.Contains("A,B", File.ReadAllText(path));
    }

    [Fact]
    public void Xlsx_IsAWorkbookThatOpens()
    {
        var path = Target("tabella.xlsx");

        TableExporter.Export(Table(["Articolo", "Quantita"], ["Mele Melinda", "64"]), path);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("Articolo", sheet.Cell(1, 1).GetString());
        Assert.Equal("Mele Melinda", sheet.Cell(2, 1).GetString());
        Assert.Equal("64", sheet.Cell(2, 2).GetString());
    }

    /// <summary>Un codice come 00123 deve restare 00123: se Excel lo prende per un numero,
    /// gli zeri davanti spariscono e il codice non è più quello.</summary>
    [Fact]
    public void Xlsx_KeepsValuesAsText()
    {
        var path = Target("codici.xlsx");

        TableExporter.Export(Table(["Codice", "Data"], ["00123", "01/02/2026"]), path);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("00123", sheet.Cell(2, 1).GetString());
        Assert.Equal("01/02/2026", sheet.Cell(2, 2).GetString());
    }

    [Fact]
    public void Xlsx_PutsEachPageOnItsOwnSheet()
    {
        var path = Target("due-pagine.xlsx");
        var first = new PdfTable(1, [["A"], ["B"]], [0], 0);
        var second = new PdfTable(7, [["C"]], [0], 0);

        TableExporter.Export([first, second], path);

        using var workbook = new XLWorkbook(path);
        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.Contains(workbook.Worksheets, s => s.Name == "Pagina 7");
    }

    [Fact]
    public void TheExtensionChoosesTheFormat()
    {
        Assert.Equal(TableFormat.Xlsx, TableExporter.FormatOf("c:/x/tabella.XLSX"));
        Assert.Equal(TableFormat.Csv, TableExporter.FormatOf("c:/x/tabella.csv"));
        Assert.Equal(TableFormat.Csv, TableExporter.FormatOf("c:/x/tabella"));
    }

    /// <summary>Se la scrittura va male a metà, chi guarda non deve trovare un file mezzo
    /// scritto al posto di quello di prima.</summary>
    [Fact]
    public void AnExistingFile_IsReplacedOnlyWhenTheNewOneIsComplete()
    {
        var path = Target("esistente.csv");
        File.WriteAllText(path, "vecchio contenuto");

        TableExporter.Export(Table(["nuovo", "contenuto"]), path);

        Assert.Contains("nuovo;contenuto", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));
    }

    [Fact]
    public void AnEmptyTable_StillProducesAFileThatOpens()
    {
        var path = Target("vuota.xlsx");

        TableExporter.Export(new PdfTable(1, [], [], 0), path);

        using var workbook = new XLWorkbook(path);
        Assert.Single(workbook.Worksheets);
    }
}
