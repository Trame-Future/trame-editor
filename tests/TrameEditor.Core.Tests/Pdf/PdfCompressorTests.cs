using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using iText.IO.Image;
using iText.Kernel.Pdf;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;
using Rectangle = System.Drawing.Rectangle;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfCompressorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-compress-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>JPEG 2200×2200 di rumore (comprime male: file grande).</summary>
    private static byte[] CreateNoiseJpeg(int size = 2200)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var bytes = new byte[Math.Abs(data.Stride) * size];
        new Random(42).NextBytes(bytes);
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        bitmap.UnlockBits(data);

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private string CreatePdfWithBigJpeg()
    {
        var path = Path.Combine(_dir, "big.pdf");
        using var document = new PdfDocument(new PdfWriter(path));
        using var layout = new iText.Layout.Document(document);
        layout.Add(new iText.Layout.Element.Image(ImageDataFactory.Create(CreateNoiseJpeg()))
            .SetAutoScale(true));
        return path;
    }

    [Fact]
    public void Compress_ShrinksImageHeavyPdf_AndKeepsItReadable()
    {
        var source = CreatePdfWithBigJpeg();
        var target = Path.Combine(_dir, "small.pdf");

        var result = PdfCompressor.Compress(source, target);

        Assert.Equal(1, result.ImagesRecompressed);
        Assert.True(result.AfterBytes < result.BeforeBytes * 0.7,
            $"compressione insufficiente: {result.BeforeBytes} → {result.AfterBytes}");
        using var inspector = new PdfTextInspector(target);
        Assert.Equal(1, inspector.PageCount);
    }

    [Fact]
    public void Compress_PdfWithoutImages_StillProducesValidOutput()
    {
        var source = Path.Combine(_dir, "plain.pdf");
        using (var document = new PdfDocument(new PdfWriter(source)))
            document.AddNewPage();
        var target = Path.Combine(_dir, "plain-out.pdf");

        var result = PdfCompressor.Compress(source, target);

        Assert.Equal(0, result.ImagesRecompressed);
        using var inspector = new PdfTextInspector(target);
        Assert.Equal(1, inspector.PageCount);
    }
}
