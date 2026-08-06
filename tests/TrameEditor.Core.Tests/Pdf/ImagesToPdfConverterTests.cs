using System.Drawing;
using System.Drawing.Imaging;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class ImagesToPdfConverterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-img2pdf-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreatePng(int width, int height)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(Color.CornflowerBlue);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Convert_OnePagePerImage_SizedFromPixels()
    {
        var target = Path.Combine(_dir, "out.pdf");

        ImagesToPdfConverter.Convert([CreatePng(200, 100), CreatePng(400, 400)], target);

        using var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(target));
        Assert.Equal(2, document.GetNumberOfPages());
        var first = document.GetPage(1).GetPageSize();
        Assert.Equal(150, first.GetWidth(), 0.5);  // 200 px a 96 dpi = 150 pt
        Assert.Equal(75, first.GetHeight(), 0.5);
    }

    [Fact]
    public void Convert_WithNoImages_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ImagesToPdfConverter.Convert([], Path.Combine(_dir, "vuoto.pdf")));
    }
}
