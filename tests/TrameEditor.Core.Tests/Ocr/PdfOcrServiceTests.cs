using System.Drawing;
using System.Drawing.Imaging;
using iText.IO.Image;
using iText.Kernel.Pdf;
using TrameEditor.Core.Ocr;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Ocr;

public class PdfOcrServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-ocr-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Trova la cartella tessdata del repo risalendo da bin/.</summary>
    private static string TessdataPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tessdata");
            if (File.Exists(Path.Combine(candidate, "ita.traineddata")))
                return candidate;
            current = current.Parent;
        }
        throw new InvalidOperationException("cartella tessdata non trovata");
    }

    /// <summary>"Scansione" sintetica: immagine con testo, incorporata in un PDF senza layer testo.</summary>
    private (string PdfPath, byte[] PagePng, double Scale) CreateScannedPdf()
    {
        const int width = 1240, height = 1754; // A4 a ~150 dpi
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Arial", 40);
            g.DrawString("DOCUMENTO PROVA OCR", font, Brushes.Black, 100, 300);
            g.DrawString("fattura numero 12345", font, Brushes.Black, 100, 450);
        }
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var png = ms.ToArray();

        var pdfPath = Path.Combine(_dir, "scanned.pdf");
        using (var document = new PdfDocument(new PdfWriter(pdfPath)))
        {
            var page = document.AddNewPage(new iText.Kernel.Geom.PageSize(595, 842));
            var image = ImageDataFactory.Create(png);
            new iText.Kernel.Pdf.Canvas.PdfCanvas(page)
                .AddImageFittedIntoRectangle(image, new iText.Kernel.Geom.Rectangle(0, 0, 595, 842), false);
        }
        return (pdfPath, png, width / 595.0);
    }

    [Fact]
    public void MakeSearchable_AddsInvisibleTextLayer_FoundByInspector()
    {
        var (pdfPath, png, scale) = CreateScannedPdf();
        var target = Path.Combine(_dir, "searchable.pdf");

        var result = PdfOcrService.MakeSearchable(pdfPath, target, TessdataPath(), _ => png, scale);

        Assert.Equal(1, result.PagesProcessed);
        Assert.True(result.WordsFound >= 5, $"poche parole riconosciute: {result.WordsFound}");
        using var inspector = new PdfTextInspector(target);
        var texts = inspector.GetLines(1).Select(l => l.Text).ToList();
        Assert.Contains(texts, t => t.Contains("PROVA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(texts, t => t.Contains("12345"));
    }

    [Fact]
    public void MakeSearchable_PdfWithTextLayer_IsCopiedUntouched()
    {
        var source = Path.Combine(_dir, "conTesto.pdf");
        TrameEditor.Core.Markdown.MarkdownPdfExporter.Export("# Già testuale", "t", source);
        var target = Path.Combine(_dir, "copiato.pdf");

        var result = PdfOcrService.MakeSearchable(source, target, TessdataPath(),
            _ => throw new InvalidOperationException("non deve renderizzare"), 1.0);

        Assert.Equal(0, result.PagesProcessed);
        Assert.True(File.Exists(target));
    }
}
