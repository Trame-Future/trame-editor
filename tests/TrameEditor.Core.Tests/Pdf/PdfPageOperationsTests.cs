using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfPageOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-pdf-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>Crea un PDF con pagine di larghezza distinta (100+i pt) per riconoscerle dopo.</summary>
    private string CreatePdf(string name, int pageCount)
    {
        var path = PathFor(name);
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(100 + i);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(200);
        }
        document.Save(path);
        return path;
    }

    private static double[] PageWidths(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(p => p.Width.Point).ToArray();
    }

    [Fact]
    public void GetPageCount_ReturnsCount()
    {
        var source = CreatePdf("count.pdf", 4);
        Assert.Equal(4, PdfPageOperations.GetPageCount(source));
    }

    [Fact]
    public void Build_ReordersAndDeletesPages()
    {
        var source = CreatePdf("reorder.pdf", 4); // larghezze 100,101,102,103
        var target = PathFor("reordered.pdf");

        PdfPageOperations.Build(source,
            [new PdfPageEdit(2, 0), new PdfPageEdit(0, 0), new PdfPageEdit(3, 0)], target);

        Assert.Equal([102, 100, 103], PageWidths(target));
    }

    [Fact]
    public void Build_AppliesRotation()
    {
        var source = CreatePdf("rotate.pdf", 2);
        var target = PathFor("rotated.pdf");

        PdfPageOperations.Build(source,
            [new PdfPageEdit(0, 90), new PdfPageEdit(1, -90)], target);

        using var document = PdfReader.Open(target, PdfDocumentOpenMode.Import);
        Assert.Equal(90, document.Pages[0].Rotate);
        Assert.Equal(270, document.Pages[1].Rotate);
    }

    [Fact]
    public void Build_CanOverwriteSourceFile()
    {
        var source = CreatePdf("inplace.pdf", 3);

        PdfPageOperations.Build(source, [new PdfPageEdit(2, 0), new PdfPageEdit(1, 0)], source);

        Assert.Equal([102, 101], PageWidths(source));
    }

    [Fact]
    public void Build_WithNoPages_Throws()
    {
        var source = CreatePdf("empty.pdf", 1);
        Assert.Throws<ArgumentException>(() =>
            PdfPageOperations.Build(source, [], PathFor("out.pdf")));
    }

    [Fact]
    public void Merge_ConcatenatesInOrder()
    {
        var a = CreatePdf("a.pdf", 2);   // 100,101
        var b = CreatePdf("b.pdf", 1);   // 100
        var target = PathFor("merged.pdf");

        PdfPageOperations.Merge([a, b], target);

        Assert.Equal([100, 101, 100], PageWidths(target));
    }

    [Fact]
    public void Merge_WithSingleSource_Throws()
    {
        var a = CreatePdf("single.pdf", 1);
        Assert.Throws<ArgumentException>(() => PdfPageOperations.Merge([a], PathFor("out.pdf")));
    }
}
