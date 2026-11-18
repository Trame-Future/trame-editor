using ClosedXML.Excel;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Tables;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Tables;

/// <summary>
/// Compilazione in serie: un modulo più una tabella di dati diventano N documenti. Quello
/// che non si abbina — colonne avanzate, campi rimasti vuoti — va dichiarato, perché un
/// modulo compilato a metà senza dirlo è peggio di uno non compilato.
/// </summary>
public class FormBatchFillerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-moduli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>Un modulo con tre campi: nome, citta, importo.</summary>
    private string FormPdf()
    {
        var path = PathFor("modulo.pdf");
        using var document = new PdfDocument(new PdfWriter(path));
        document.AddNewPage();
        var form = PdfAcroForm.GetAcroForm(document, createIfNotExist: true);
        var y = 700;
        foreach (var name in new[] { "nome", "citta", "importo" })
        {
            form.AddField(new TextFormFieldBuilder(document, name)
                .SetWidgetRectangle(new Rectangle(50, y, 200, 20)).CreateText());
            y -= 40;
        }
        return path;
    }

    private string Csv(string content, string name = "dati.csv")
    {
        var path = PathFor(name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Output() => PathFor("prodotti");

    [Fact]
    public void FillsOneDocumentPerRow()
    {
        var data = Csv("nome;citta;importo\nRossi;Caserta;100\nBianchi;Napoli;200\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Equal(2, result.Documents.Count);
        Assert.All(result.Documents, d => Assert.True(File.Exists(d.OutputPath)));
        var first = PdfFormService.GetFields(result.Documents[0].OutputPath);
        Assert.Equal("Rossi", first.Single(f => f.Name == "nome").Value);
        Assert.Equal("Caserta", first.Single(f => f.Name == "citta").Value);
    }

    /// <summary>Il nome del file viene dalla prima colonna, o da quella indicata.</summary>
    [Fact]
    public void TheFileNameComesFromTheChosenColumn()
    {
        var data = Csv("nome;citta;importo\nRossi;Caserta;100\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output(), nameColumn: "citta");

        Assert.Equal("Caserta.pdf", Path.GetFileName(result.Documents[0].OutputPath));
    }

    [Fact]
    public void MatchesColumnsToFields_IgnoringCaseAndSpaces()
    {
        var data = Csv("Nome; CITTA ;Importo\nRossi;Caserta;100\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Empty(result.UnmatchedColumns);
        var fields = PdfFormService.GetFields(result.Documents[0].OutputPath);
        Assert.Equal("Caserta", fields.Single(f => f.Name == "citta").Value);
    }

    /// <summary>Le colonne che non corrispondono a nessun campo non si indovinano: si dicono.</summary>
    [Fact]
    public void ColumnsWithoutAField_AreReported()
    {
        var data = Csv("nome;citta;importo;telefono\nRossi;Caserta;100;0823\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Contains("telefono", result.UnmatchedColumns);
        Assert.Contains(result.Warnings, w => w.Contains("telefono"));
    }

    [Fact]
    public void FieldsWithoutAColumn_AreReported()
    {
        var data = Csv("nome;citta\nRossi;Caserta\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Contains("importo", result.UnfilledFields);
        Assert.Contains(result.Warnings, w => w.Contains("importo"));
    }

    [Fact]
    public void AnEmptyCell_IsReportedPerDocument()
    {
        var data = Csv("nome;citta;importo\nRossi;;100\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Contains("citta", result.Documents[0].EmptyFields);
    }

    [Fact]
    public void RepeatedNames_AreNumbered_AndReported()
    {
        var data = Csv("nome;citta;importo\nRossi;Caserta;100\nRossi;Napoli;200\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Equal("Rossi.pdf", Path.GetFileName(result.Documents[0].OutputPath));
        Assert.Equal("Rossi-2.pdf", Path.GetFileName(result.Documents[1].OutputPath));
        Assert.Contains(result.Warnings, w => w.Contains("più di una volta"));
    }

    [Fact]
    public void EmptyRowsAtTheEnd_AreSkipped()
    {
        var data = Csv("nome;citta;importo\nRossi;Caserta;100\n;;\n;;\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Single(result.Documents);
    }

    [Fact]
    public void ReadsDataFromExcelToo()
    {
        var path = PathFor("dati.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Dati");
            sheet.Cell(1, 1).SetValue("nome");
            sheet.Cell(1, 2).SetValue("citta");
            sheet.Cell(2, 1).SetValue("Verdi");
            sheet.Cell(2, 2).SetValue("Roma");
            workbook.SaveAs(path);
        }

        var result = FormBatchFiller.Fill(FormPdf(), path, Output());

        Assert.Single(result.Documents);
        Assert.Equal("Verdi", PdfFormService.GetFields(result.Documents[0].OutputPath)
            .Single(f => f.Name == "nome").Value);
    }

    /// <summary>Una cella può contenere il separatore, se è fra virgolette.</summary>
    [Fact]
    public void CsvQuotesAreRespected()
    {
        var data = Csv("nome;citta;importo\n\"Rossi; Mario\";Caserta;100\n");

        var result = FormBatchFiller.Fill(FormPdf(), data, Output());

        Assert.Equal("Rossi; Mario", PdfFormService.GetFields(result.Documents[0].OutputPath)
            .Single(f => f.Name == "nome").Value);
    }

    [Fact]
    public void APdfWithoutFields_IsRefusedWithAReason()
    {
        var notAForm = PathFor("non-modulo.pdf");
        using (var document = new PdfDocument(new PdfWriter(notAForm)))
            document.AddNewPage();
        var data = Csv("nome\nRossi\n");

        var error = Assert.Throws<InvalidOperationException>(
            () => FormBatchFiller.Fill(notAForm, data, Output()));

        Assert.Contains("non è un modulo", error.Message);
    }

    [Fact]
    public void NoColumnMatching_SaysWhichNamesWereSeen()
    {
        var data = Csv("alfa;beta\n1;2\n");

        var error = Assert.Throws<InvalidOperationException>(
            () => FormBatchFiller.Fill(FormPdf(), data, Output()));

        Assert.Contains("alfa", error.Message);
        Assert.Contains("nome", error.Message); // i campi del modulo, per capire cosa aspettarsi
    }

    [Fact]
    public void DoesNotOverwriteByItself()
    {
        var data = Csv("nome;citta;importo\nRossi;Caserta;100\n");
        var folder = Output();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Rossi.pdf"), "roba da non perdere");

        var error = Assert.Throws<InvalidOperationException>(
            () => FormBatchFiller.Fill(FormPdf(), data, folder));

        Assert.Contains("Esiste già", error.Message);
        Assert.Equal("roba da non perdere", File.ReadAllText(Path.Combine(folder, "Rossi.pdf")));
    }
}
