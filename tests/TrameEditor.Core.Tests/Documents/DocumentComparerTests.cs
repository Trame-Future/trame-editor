using TrameEditor.Core.Documents;
using TrameEditor.Core.Markdown;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Documents;

public class DocumentComparerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-compare-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreatePdf(string name, string markdown)
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.Export(markdown, name, path);
        return path;
    }

    private string CreateText(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Compare_FindsAddedAndRemovedLines()
    {
        var v1 = CreatePdf("v1.pdf",
            "# Contratto\n\nDurata: 12 mesi\n\nCanone: 100 euro al mese\n\nFirma del cliente");
        var v2 = CreatePdf("v2.pdf",
            "# Contratto\n\nDurata: 12 mesi\n\nCanone: 150 euro al mese\n\nPenale per recesso anticipato\n\nFirma del cliente");

        var result = DocumentComparer.Compare(v1, v2);

        Assert.False(result.AreIdentical);
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Text.Contains("100 euro"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("150 euro"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("Penale"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Unchanged && e.Text.Contains("Contratto"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Unchanged && e.Text.Contains("Firma"));
        Assert.Equal(2, result.AddedCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal("pag.", result.ReferenceLabel);
    }

    [Fact]
    public void Compare_IdenticalDocuments()
    {
        var a = CreatePdf("a.pdf", "# Uguale\n\nriga uno\n\nriga due");
        var b = CreatePdf("b.pdf", "# Uguale\n\nriga uno\n\nriga due");

        var result = DocumentComparer.Compare(a, b);

        Assert.True(result.AreIdentical);
        Assert.All(result.Entries, e => Assert.Equal(DiffKind.Unchanged, e.Kind));
    }

    [Fact]
    public void Compare_ReportsPageNumbers()
    {
        var v1 = CreatePdf("p1.pdf", "prima pagina");
        var v2 = CreatePdf("p2.pdf", "prima pagina\n\nriga nuova");

        var added = DocumentComparer.Compare(v1, v2).Entries.Single(e => e.Kind == DiffKind.Added);
        Assert.Null(added.LeftRef);
        Assert.Equal(1, added.RightRef);
    }

    // ----- Testo e Markdown -----

    [Fact]
    public void Compare_FileDiTesto_UsaINumeriDiRiga()
    {
        var v1 = CreateText("appunti-v1.txt", "prima riga\nseconda riga\nterza riga\n");
        var v2 = CreateText("appunti-v2.txt", "prima riga\nseconda riga MODIFICATA\nterza riga\nquarta riga\n");

        var result = DocumentComparer.Compare(v1, v2);

        Assert.Equal("riga", result.ReferenceLabel);
        Assert.False(result.MixedTypes);
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Text == "seconda riga");
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("MODIFICATA"));

        var aggiunta = result.Entries.Single(e => e.Kind == DiffKind.Added && e.Text == "quarta riga");
        Assert.Equal(4, aggiunta.RightRef); // il numero di riga vero nel file
    }

    [Fact]
    public void Compare_Markdown_IgnoraLeRigheVuote()
    {
        var v1 = CreateText("doc-v1.md", "# Titolo\n\nUn paragrafo.\n");
        var v2 = CreateText("doc-v2.md", "# Titolo\n\n\n\nUn paragrafo.\n");

        var result = DocumentComparer.Compare(v1, v2);

        Assert.True(result.AreIdentical, "cambiare solo le righe vuote non è una differenza di contenuto");
    }

    /// <summary>Un PDF contro il suo sorgente Markdown: tipi diversi, confronto
    /// possibile perché guardiamo il testo. Va però dichiarato.</summary>
    [Fact]
    public void Compare_TipiMisti_ConfrontaEDichiara()
    {
        var pdf = CreatePdf("misto.pdf", "riga comune\n\nsolo nel pdf");
        var testo = CreateText("misto.txt", "riga comune\nsolo nel testo\n");

        var result = DocumentComparer.Compare(pdf, testo);

        Assert.True(result.MixedTypes);
        Assert.Equal("pag.", result.ReferenceLabel); // i riferimenti seguono il primo documento
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Unchanged && e.Text == "riga comune");
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Text.Contains("solo nel pdf"));
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Text.Contains("solo nel testo"));
    }
}
