using System.Text;
using TrameEditor.Core.Documents;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Markdown;

/// <summary>
/// Salvare un documento di testo in PDF e in PDF/A. Il punto delicato è che un
/// <c>.txt</c> non è Markdown: interpretarlo come tale cambierebbe il documento
/// senza che l'utente l'abbia chiesto.
/// </summary>
public class PlainTextPdfExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-txtpdf-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string ExtractText(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var text = new StringBuilder();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
                text.AppendLine(line.Text);
        }
        return text.ToString();
    }

    [Fact]
    public void ExportPlainText_NonInterpretaLaSintassiMarkdown()
    {
        var path = Path.Combine(_dir, "appunti.pdf");
        const string testo = "# Questo non e' un titolo\n" +
            "Costo: 3 * 4 euro\n" +
            "_trattino basso_ e **due asterischi**\n";

        MarkdownPdfExporter.ExportPlainText(testo, "appunti", path);

        var estratto = ExtractText(path);
        Assert.Contains("# Questo non e' un titolo", estratto);
        Assert.Contains("3 * 4 euro", estratto);
        Assert.Contains("**due asterischi**", estratto);
    }

    [Fact]
    public void Export_Markdown_InterpretaLaSintassi()
    {
        var path = Path.Combine(_dir, "documento.pdf");

        MarkdownPdfExporter.Export("# Titolo vero\n\ntesto **in grassetto**", "documento", path);

        var estratto = ExtractText(path);
        Assert.Contains("Titolo vero", estratto);
        Assert.DoesNotContain("#", estratto);
        Assert.DoesNotContain("**", estratto);
    }

    [Fact]
    public void ExportPlainText_RigheVuote_ConservaLaSpaziatura()
    {
        var path = Path.Combine(_dir, "spaziato.pdf");

        MarkdownPdfExporter.ExportPlainText("prima\n\n\nquarta", "spaziato", path);

        var content = DocumentTextReader.Read(path);
        var estratto = string.Join("\n", content.Sections.Select(s => s.Text));
        Assert.Contains("prima", estratto);
        Assert.Contains("quarta", estratto);
    }

    /// <summary>
    /// La catena che l'utente percorre davvero: scrive un testo, lo salva in
    /// PDF/A per archiviarlo. Deve arrivare in fondo senza intoppi e col testo
    /// intatto.
    /// </summary>
    [Fact]
    public void DalTestoAlPdfA_LaCatenaCompletaFunziona()
    {
        var pdf = Path.Combine(_dir, "verbale.pdf");
        var pdfa = Path.Combine(_dir, "verbale-archivio.pdf");
        const string testo = "VERBALE DI ASSEMBLEA\n\n" +
            "Approvato il bilancio con 12 voti favorevoli.\n" +
            "Prossima seduta: 30 settembre.\n";

        MarkdownPdfExporter.ExportPlainText(testo, "verbale", pdf);

        var report = PdfAAnalyzer.Analyze(pdf);
        Assert.True(report.CanConvertFaithfully, string.Join("; ", report.Blocking));

        var result = PdfAConverter.ConvertFaithfully(pdf, pdfa, "verbale");

        Assert.Equal(PdfAConversionMethod.Fedele, result.Method);
        Assert.Empty(result.Verification.NonEmbeddedFonts);
        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));
        Assert.Contains("VERBALE DI ASSEMBLEA", ExtractText(pdfa));
        Assert.Contains("12 voti favorevoli", ExtractText(pdfa));
    }

    [Fact]
    public void DalMarkdownAlPdfA_LaCatenaCompletaFunziona()
    {
        var pdf = Path.Combine(_dir, "relazione.pdf");
        var pdfa = Path.Combine(_dir, "relazione-archivio.pdf");

        MarkdownPdfExporter.Export(
            "# Relazione annuale\n\nRisultato d'esercizio: **4.500 euro**.\n\n" +
            "- primo punto\n- secondo punto\n", "relazione", pdf);

        var result = PdfAConverter.ConvertFaithfully(pdf, pdfa, "relazione");

        Assert.Equal(PdfALevel.A2u, result.Level);
        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));
        Assert.Contains("Relazione annuale", ExtractText(pdfa));
    }
}
