using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Annotazioni PDF di base: evidenziazione, nota a comparsa, timbro immagine.
/// Coordinate in spazio utente PDF (origine in basso a sinistra, punti).
/// Ogni operazione scrive un nuovo file in modo atomico.
/// </summary>
public static class PdfAnnotationService
{
    public static void HighlightArea(string sourcePath, string targetPath,
        int pageNumber, double left, double bottom, double width, double height)
    {
        WithDocument(sourcePath, targetPath, document =>
        {
            var page = document.GetPage(pageNumber);
            var l = (float)left;
            var b = (float)bottom;
            var r = (float)(left + width);
            var t = (float)(bottom + height);
            // QuadPoints: alto-sx, alto-dx, basso-sx, basso-dx
            var annotation = PdfTextMarkupAnnotation.CreateHighLight(
                new Rectangle(l, b, r - l, t - b),
                [l, t, r, t, l, b, r, b]);
            annotation.SetColor(ColorConstants.YELLOW);
            annotation.SetTitle(new PdfString("TrameEditor"));
            page.AddAnnotation(annotation);
        });
    }

    public static void AddNote(string sourcePath, string targetPath,
        int pageNumber, double x, double y, string text)
    {
        WithDocument(sourcePath, targetPath, document =>
        {
            var page = document.GetPage(pageNumber);
            var annotation = new PdfTextAnnotation(new Rectangle((float)x, (float)y, 22, 22));
            annotation.SetContents(text);
            annotation.SetTitle(new PdfString("TrameEditor"));
            annotation.SetIconName(new PdfName("Comment"));
            annotation.SetColor(new DeviceRgb(1f, 0.85f, 0.2f));
            page.AddAnnotation(annotation);
        });
    }

    /// <summary>Disegna un'immagine (timbro/firma) centrata nel punto indicato.</summary>
    public static void StampImage(string sourcePath, string targetPath,
        int pageNumber, double centerX, double centerY, string imagePath, double widthPt)
    {
        WithDocument(sourcePath, targetPath, document =>
        {
            var page = document.GetPage(pageNumber);
            var image = ImageDataFactory.Create(imagePath);
            var heightPt = widthPt * image.GetHeight() / image.GetWidth();
            var rect = new Rectangle(
                (float)(centerX - widthPt / 2),
                (float)(centerY - heightPt / 2),
                (float)widthPt,
                (float)heightPt);
            new PdfCanvas(page).AddImageFittedIntoRectangle(image, rect, asInline: false);
        });
    }

    private static void WithDocument(string sourcePath, string targetPath, Action<PdfDocument> action)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                action(document);
            }

            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
