using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Markdown;

public class MarkdownPdfExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-mdpdf-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Export_ProducesReadablePdfWithContent()
    {
        var target = Path.Combine(_dir, "export.pdf");

        MarkdownPdfExporter.Export(
            "# Relazione di prova\n\nParagrafo con **grassetto** e un elenco:\n\n- prima voce\n- seconda voce",
            "Relazione", target);

        using var inspector = new PdfTextInspector(target);
        Assert.True(inspector.PageCount >= 1);
        var texts = inspector.GetLines(1).Select(l => l.Text).ToList();
        Assert.Contains(texts, t => t.Contains("Relazione di prova"));
        Assert.Contains(texts, t => t.Contains("prima voce"));
    }

    [Fact]
    public void Export_OverwritesExistingFileAtomically()
    {
        var target = Path.Combine(_dir, "overwrite.pdf");
        MarkdownPdfExporter.Export("# Prima versione", "v1", target);

        MarkdownPdfExporter.Export("# Seconda versione", "v2", target);

        using var inspector = new PdfTextInspector(target);
        var texts = inspector.GetLines(1).Select(l => l.Text).ToList();
        Assert.Contains(texts, t => t.Contains("Seconda versione"));
        Assert.DoesNotContain(texts, t => t.Contains("Prima versione"));
    }
}
