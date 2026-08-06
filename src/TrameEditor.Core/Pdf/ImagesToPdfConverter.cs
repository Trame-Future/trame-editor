using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Converte una sequenza di immagini in un PDF: una pagina per immagine,
/// dimensionata sull'immagine stessa (96 dpi → punti PDF).
/// </summary>
public static class ImagesToPdfConverter
{
    private const double PixelsToPoints = 72.0 / 96.0;

    public static void Convert(IReadOnlyList<string> imagePaths, string targetPath)
    {
        if (imagePaths.Count == 0)
            throw new ArgumentException("Nessuna immagine da convertire.", nameof(imagePaths));

        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = new PdfDocument(new PdfWriter(tempPath)))
            {
                foreach (var imagePath in imagePaths)
                {
                    var image = ImageDataFactory.Create(imagePath);
                    var width = (float)(image.GetWidth() * PixelsToPoints);
                    var height = (float)(image.GetHeight() * PixelsToPoints);
                    var page = document.AddNewPage(new PageSize(width, height));
                    new PdfCanvas(page).AddImageFittedIntoRectangle(
                        image, new Rectangle(0, 0, width, height), asInline: false);
                }
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
