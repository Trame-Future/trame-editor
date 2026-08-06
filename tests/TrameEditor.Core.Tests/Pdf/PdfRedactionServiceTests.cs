using iText.Kernel.Pdf;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfRedactionServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-redact-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateDocumentWithSensitiveData()
    {
        var path = Path.Combine(_dir, "domanda.pdf");
        MarkdownPdfExporter.Export(
            "# Domanda di iscrizione\n\n" +
            "Il sottoscritto Mario Rossi, codice fiscale RSSMRA80A01H501U,\n\n" +
            "IBAN IT60X0542811101000000123456,\n\n" +
            "email mario.rossi@example.com, chiede l'iscrizione al servizio.\n\n" +
            "Distinti saluti.",
            "domanda", path);
        return path;
    }

    [Fact]
    public void Scan_FindsAllSensitiveData()
    {
        var path = CreateDocumentWithSensitiveData();

        var matches = PdfRedactionService.Scan(path);

        Assert.Contains(matches, m => m.Kind == SensitiveKind.CodiceFiscale);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Iban);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Email);
    }

    [Fact]
    public void Apply_RemovesDataForReal_AndKeepsTheRest()
    {
        var source = CreateDocumentWithSensitiveData();
        var target = Path.Combine(_dir, "anonimo.pdf");
        var matches = PdfRedactionService.Scan(source);

        var result = PdfRedactionService.Apply(source, target, matches, stripMetadata: true);

        Assert.Empty(result.SkippedLines);
        Assert.True(result.ItemsRedacted >= 3);

        // Verifica sul testo REALMENTE estraibile dal file prodotto
        using var inspector = new PdfTextInspector(target);
        var allText = string.Join("\n", inspector.GetLines(1).Select(l => l.Text));
        Assert.DoesNotContain("RSSMRA80A01H501U", allText);
        Assert.DoesNotContain("IT60X0542811101000000123456", allText.Replace(" ", ""));
        Assert.DoesNotContain("mario.rossi@example.com", allText);
        Assert.Contains("XXX", allText);                    // mascheratura presente
        Assert.Contains("Domanda di iscrizione", allText);  // il resto è intatto
        Assert.Contains("Distinti saluti", allText);
    }

    [Fact]
    public void StripMetadata_ClearsAuthorTitleAndXmp()
    {
        var path = Path.Combine(_dir, "conMetadati.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            document.AddNewPage();
            var info = document.GetDocumentInfo();
            info.SetAuthor("Mario Rossi");
            info.SetTitle("Documento personale di Mario");
        }
        var target = Path.Combine(_dir, "senzaMetadati.pdf");

        PdfRedactionService.StripMetadata(path, target);

        using var check = new PdfDocument(new PdfReader(target));
        var checkedInfo = check.GetDocumentInfo();
        Assert.True(string.IsNullOrEmpty(checkedInfo.GetAuthor()));
        Assert.True(string.IsNullOrEmpty(checkedInfo.GetTitle()));
        // se un flusso XMP esiste ancora, non deve contenere i dati personali
        var xmpStream = check.GetCatalog().GetPdfObject().GetAsStream(PdfName.Metadata);
        if (xmpStream is not null)
        {
            var xmpText = System.Text.Encoding.UTF8.GetString(xmpStream.GetBytes());
            Assert.DoesNotContain("Mario", xmpText);
        }
    }

    [Fact]
    public void Apply_WithNoSelection_JustCopiesOrStrips()
    {
        var source = CreateDocumentWithSensitiveData();
        var target = Path.Combine(_dir, "copia.pdf");

        var result = PdfRedactionService.Apply(source, target, [], stripMetadata: false);

        Assert.Equal(0, result.ItemsRedacted);
        Assert.True(File.Exists(target));
    }
}
