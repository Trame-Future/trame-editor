using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfComparerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-compare-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreatePdf(string name, string markdown)
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.Export(markdown, name, path);
        return path;
    }

    [Fact]
    public void Compare_FindsAddedAndRemovedLines()
    {
        var v1 = CreatePdf("v1.pdf",
            "# Contratto\n\nDurata: 12 mesi\n\nCanone: 100 euro al mese\n\nFirma del cliente");
        var v2 = CreatePdf("v2.pdf",
            "# Contratto\n\nDurata: 12 mesi\n\nCanone: 150 euro al mese\n\nPenale per recesso anticipato\n\nFirma del cliente");

        var result = PdfComparer.Compare(v1, v2);

        Assert.False(result.AreIdentical);
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Text.Contains("100 euro"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("150 euro"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("Penale"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Unchanged && e.Text.Contains("Contratto"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Unchanged && e.Text.Contains("Firma"));
        Assert.Equal(2, result.AddedCount);
        Assert.Equal(1, result.RemovedCount);
    }

    [Fact]
    public void Compare_IdenticalDocuments()
    {
        var a = CreatePdf("a.pdf", "# Uguale\n\nriga uno\n\nriga due");
        var b = CreatePdf("b.pdf", "# Uguale\n\nriga uno\n\nriga due");

        var result = PdfComparer.Compare(a, b);

        Assert.True(result.AreIdentical);
        Assert.All(result.Entries, e => Assert.Equal(DiffKind.Unchanged, e.Kind));
    }

    [Fact]
    public void Compare_ReportsPageNumbers()
    {
        var v1 = CreatePdf("p1.pdf", "prima pagina");
        var v2 = CreatePdf("p2.pdf", "prima pagina\n\nriga nuova");

        var added = PdfComparer.Compare(v1, v2).Entries.Single(e => e.Kind == DiffKind.Added);
        Assert.Null(added.LeftPage);
        Assert.Equal(1, added.RightPage);
    }
}
