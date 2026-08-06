using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfAnnotationServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-annot-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>PNG 1×1 valido, per il timbro nei test.</summary>
    private string CreateTinyPng()
    {
        var path = PathFor("stamp.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        return path;
    }

    private string CreateBlankPdf(int pages = 1)
    {
        var path = PathFor($"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (var i = 0; i < pages; i++)
            document.AddPage();
        document.Save(path);
        return path;
    }

    [Fact]
    public void HighlightArea_AddsHighlightAnnotation()
    {
        var source = CreateBlankPdf();
        var target = PathFor("highlight.pdf");

        PdfAnnotationService.HighlightArea(source, target, 1, 50, 700, 200, 14);

        using var document = new PdfDocument(new PdfReader(target));
        var annotations = document.GetPage(1).GetAnnotations();
        var highlight = Assert.Single(annotations);
        Assert.Equal(PdfName.Highlight, highlight.GetSubtype());
    }

    [Fact]
    public void AddNote_AddsTextAnnotationWithContents()
    {
        var source = CreateBlankPdf();
        var target = PathFor("note.pdf");

        PdfAnnotationService.AddNote(source, target, 1, 100, 650, "Promemoria di prova");

        using var document = new PdfDocument(new PdfReader(target));
        var note = Assert.Single(document.GetPage(1).GetAnnotations());
        Assert.Equal(PdfName.Text, note.GetSubtype());
        Assert.Equal("Promemoria di prova", note.GetContents().GetValue());
    }

    [Fact]
    public void StampImage_DrawsImageOnPage()
    {
        var source = CreateBlankPdf();
        var target = PathFor("stamp-out.pdf");

        PdfAnnotationService.StampImage(source, target, 1, 300, 400, CreateTinyPng(), 120);

        using var document = new PdfDocument(new PdfReader(target));
        var resources = document.GetPage(1).GetResources();
        var xobjects = resources.GetResource(PdfName.XObject);
        Assert.NotNull(xobjects);
        Assert.NotEmpty(xobjects.KeySet());
    }

    [Fact]
    public void Annotations_AccumulateAcrossOperations()
    {
        var source = CreateBlankPdf();
        var step1 = PathFor("step1.pdf");
        var step2 = PathFor("step2.pdf");

        PdfAnnotationService.HighlightArea(source, step1, 1, 50, 700, 100, 12);
        PdfAnnotationService.AddNote(step1, step2, 1, 200, 600, "seconda");

        using var document = new PdfDocument(new PdfReader(step2));
        Assert.Equal(2, document.GetPage(1).GetAnnotations().Count);
    }
}
