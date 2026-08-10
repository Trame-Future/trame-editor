using System.Net;
using iText.Html2pdf;

namespace TrameEditor.Core.Markdown;

/// <summary>
/// Export di un documento di testo in PDF: il Markdown è reso in HTML (stessa
/// pipeline dell'anteprima) e convertito in PDF con iText pdfHTML. Tutto offline.
/// </summary>
public static class MarkdownPdfExporter
{
    public static void Export(string markdown, string title, string targetPath) =>
        WriteHtmlAsPdf(MarkdownRenderService.RenderDocument(markdown, title), targetPath);

    /// <summary>
    /// Export di un file di testo semplice. Il contenuto <b>non</b> viene
    /// interpretato come Markdown: in un .txt un asterisco è un asterisco e una
    /// riga che comincia con # non è un titolo. Il testo va sulla pagina come lo
    /// vedi nell'editor, a spaziatura fissa e con le righe dove le hai messe.
    /// </summary>
    public static void ExportPlainText(string text, string title, string targetPath)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var body = WebUtility.HtmlEncode(text ?? string.Empty);
        var html = $$"""
            <!DOCTYPE html>
            <html lang="it">
            <head>
            <meta charset="utf-8">
            <title>{{safeTitle}}</title>
            <style>
            body { margin: 0; padding: 0; }
            pre { font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
                  font-size: 10.5pt; line-height: 1.45; white-space: pre-wrap;
                  word-wrap: break-word; margin: 0; }
            </style>
            </head>
            <body>
            <pre>{{body}}</pre>
            </body>
            </html>
            """;
        WriteHtmlAsPdf(html, targetPath);
    }

    private static void WriteHtmlAsPdf(string html, string targetPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
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
