using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Tesseract;
using Path = System.IO.Path;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace TrameEditor.Core.Ocr;

public sealed record PdfOcrResult(int PagesProcessed, int WordsFound);

/// <summary>
/// OCR offline (Tesseract) dei PDF scansionati: alle pagine senza layer di testo
/// viene aggiunto un layer <b>invisibile</b> con le parole riconosciute nelle
/// loro posizioni, rendendo il documento ricercabile (e selezionabile) senza
/// alterarne l'aspetto.
/// </summary>
public static class PdfOcrService
{
    /// <param name="renderPagePng">Rende la pagina (1-based) come PNG per l'OCR.</param>
    /// <param name="renderScale">Pixel dell'immagine per punto PDF (per riportare i box in coordinate pagina).</param>
    public static PdfOcrResult MakeSearchable(string sourcePath, string targetPath,
        string tessdataPath, Func<int, byte[]> renderPagePng, double renderScale,
        string languages = "ita+eng")
    {
        var pagesWithoutText = new List<int>();
        using (var inspection = PigDocument.Open(File.ReadAllBytes(sourcePath)))
        {
            for (var page = 1; page <= inspection.NumberOfPages; page++)
            {
                if (!inspection.GetPage(page).GetWords().Any())
                    pagesWithoutText.Add(page);
            }
        }

        if (pagesWithoutText.Count == 0)
        {
            File.Copy(sourcePath, Path.GetFullPath(targetPath), overwrite: true);
            return new PdfOcrResult(0, 0);
        }

        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        var wordsFound = 0;
        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            using (var engine = new TesseractEngine(tessdataPath, languages, EngineMode.Default))
            {
                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                foreach (var pageNumber in pagesWithoutText)
                {
                    using var pix = Pix.LoadFromMemory(renderPagePng(pageNumber));
                    using var ocrPage = engine.Process(pix);

                    var page = document.GetPage(pageNumber);
                    var pageHeight = page.GetPageSize().GetHeight();
                    var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), document);
                    canvas.BeginText();
                    canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);

                    using var iterator = ocrPage.GetIterator();
                    iterator.Begin();
                    do
                    {
                        if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                            continue;
                        var word = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                        if (string.IsNullOrEmpty(word))
                            continue;

                        var x = box.X1 / renderScale;
                        var y = pageHeight - box.Y2 / renderScale;
                        var size = Math.Max(4.0, (box.Y2 - box.Y1) / renderScale);
                        try
                        {
                            canvas.SetFontAndSize(font, (float)size);
                            canvas.SetTextMatrix((float)x, (float)y);
                            canvas.ShowText(word);
                            wordsFound++;
                        }
                        catch
                        {
                            // parola con glifi non rappresentabili nel font standard: saltata
                        }
                    } while (iterator.Next(PageIteratorLevel.Word));

                    canvas.EndText();
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

        return new PdfOcrResult(pagesWithoutText.Count, wordsFound);
    }
}
