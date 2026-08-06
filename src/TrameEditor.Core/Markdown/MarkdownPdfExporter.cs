using iText.Html2pdf;

namespace TrameEditor.Core.Markdown;

/// <summary>
/// Export Markdown → PDF: il Markdown è reso in HTML (stessa pipeline
/// dell'anteprima) e convertito in PDF con iText pdfHTML. Tutto offline.
/// </summary>
public static class MarkdownPdfExporter
{
    public static void Export(string markdown, string title, string targetPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var html = MarkdownRenderService.RenderDocument(markdown, title);
            using (var output = File.Create(tempPath))
            {
                HtmlConverter.ConvertToPdf(html, output);
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
