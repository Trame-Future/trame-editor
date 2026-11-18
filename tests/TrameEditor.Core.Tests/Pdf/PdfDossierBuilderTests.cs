using iText.Kernel.Pdf;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Fascicolo: unire più PDF con segnalibri e una pagina di indice. È la differenza fra un
/// malloppo e un documento consultabile.
/// </summary>
public class PdfDossierBuilderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-fascicolo-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Un PDF con il numero di pagine chiesto.</summary>
    private string Document(string name, int pages)
    {
        var path = Path.Combine(_dir, name + ".pdf");
        using var document = new PdfDocument(new PdfWriter(path));
        for (var i = 0; i < pages; i++)
            document.AddNewPage();
        return path;
    }

    private string Target() => Path.Combine(_dir, "fascicolo.pdf");

    [Fact]
    public void JoinsTheDocumentsAndCountsThePages()
    {
        var sources = new[] { Document("Relazione", 2), Document("Allegato A", 3) };

        var result = PdfDossierBuilder.Build(sources, Target());

        // 2 + 3 pagine più quella dell'indice.
        Assert.Equal(6, result.TotalPages);
        using var dossier = new PdfDocument(new PdfReader(Target()));
        Assert.Equal(6, dossier.GetNumberOfPages());
    }

    /// <summary>
    /// L'indice occupa la prima pagina, quindi tutto il resto scala di uno. Senza questa
    /// correzione i rimandi sarebbero sbagliati di una pagina: il modo più silenzioso di
    /// rendere inutile un indice.
    /// </summary>
    [Fact]
    public void ThePageNumbersAccountForTheIndexPage()
    {
        var sources = new[] { Document("Primo", 2), Document("Secondo", 1) };

        var result = PdfDossierBuilder.Build(sources, Target());

        Assert.Equal(2, result.Entries[0].FirstPage); // dopo l'indice
        Assert.Equal(4, result.Entries[1].FirstPage); // dopo l'indice e le due del primo
    }

    [Fact]
    public void WithoutTheIndexPage_TheFirstDocumentStartsAtPageOne()
    {
        var sources = new[] { Document("Primo", 2), Document("Secondo", 1) };

        var result = PdfDossierBuilder.Build(sources, Target(), withIndexPage: false);

        Assert.False(result.HasIndexPage);
        Assert.Equal(1, result.Entries[0].FirstPage);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void EachDocumentGetsItsBookmark()
    {
        var sources = new[] { Document("Relazione tecnica", 1), Document("Computo", 1) };

        PdfDossierBuilder.Build(sources, Target());

        using var dossier = new PdfDocument(new PdfReader(Target()));
        var outlines = dossier.GetOutlines(false).GetAllChildren();
        Assert.Equal(2, outlines.Count);
        Assert.Contains(outlines, o => o.GetTitle() == "Relazione tecnica");
    }

    [Fact]
    public void TheIndexPageIsClickable()
    {
        var sources = new[] { Document("Primo", 1), Document("Secondo", 1) };

        PdfDossierBuilder.Build(sources, Target());

        using var dossier = new PdfDocument(new PdfReader(Target()));
        var links = dossier.GetPage(1).GetAnnotations()
            .OfType<iText.Kernel.Pdf.Annot.PdfLinkAnnotation>().ToList();
        Assert.Equal(2, links.Count);
    }

    /// <summary>Un file illeggibile non deve far saltare tutto il fascicolo: si esclude e
    /// lo si dice.</summary>
    [Fact]
    public void ABrokenFileIsExcluded_AndReported()
    {
        var broken = Path.Combine(_dir, "rotto.pdf");
        File.WriteAllText(broken, "questo non è un PDF");
        var sources = new[] { Document("Buono", 1), broken, Document("Altro", 1) };

        var result = PdfDossierBuilder.Build(sources, Target());

        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Warnings, w => w.Contains("rotto.pdf"));
    }

    [Fact]
    public void LessThanTwoDocuments_IsRefused()
    {
        var single = new[] { Document("Solo", 1) };

        Assert.Throws<ArgumentException>(() => PdfDossierBuilder.Build(single, Target()));
    }

    [Fact]
    public void IfTooManyAreBroken_ItSaysThereIsNoDossier()
    {
        var broken = Path.Combine(_dir, "rotto.pdf");
        File.WriteAllText(broken, "non è un PDF");
        var sources = new[] { Document("Buono", 1), broken };

        var error = Assert.Throws<InvalidOperationException>(
            () => PdfDossierBuilder.Build(sources, Target()));

        Assert.Contains("meno di due documenti", error.Message);
    }

    /// <summary>Il titolo di ogni voce è il nome del file, senza estensione: è quello che
    /// chi ha preparato gli allegati ha già scelto come nome.</summary>
    [Fact]
    public void TheTitleComesFromTheFileName()
    {
        var sources = new[] { Document("Verbale del 12 marzo", 1), Document("Allegato B", 1) };

        var result = PdfDossierBuilder.Build(sources, Target());

        Assert.Equal("Verbale del 12 marzo", result.Entries[0].Title);
    }
}
