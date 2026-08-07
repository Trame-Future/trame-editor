using TrameEditor.Core.Ocr;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>La "ricetta": i passi da applicare a ogni PDF, nell'ordine sensato
/// OCR → Anonimizza → Comprimi → Proteggi.</summary>
public sealed record BatchRecipe(
    bool RunOcr,
    bool Redact,
    bool Compress,
    string? ProtectPassword);

public sealed record BatchFileResult(
    string SourcePath,
    string? OutputPath,
    bool Success,
    string Outcome);

/// <summary>
/// Elaborazione in serie di più PDF con la stessa ricetta. Ogni passo lavora
/// su copie temporanee; i file originali non vengono mai toccati.
/// </summary>
public static class BatchProcessor
{
    /// <param name="ocrRenderPage">Render della pagina (1-based) in PNG per l'OCR
    /// di QUESTO file; null se l'OCR non è richiesto o non disponibile.</param>
    public static BatchFileResult ProcessFile(string sourcePath, string outputDirectory,
        BatchRecipe recipe, string? tessdataPath = null,
        Func<int, byte[]>? ocrRenderPage = null, double ocrRenderScale = 0)
    {
        var tempFiles = new List<string>();
        try
        {
            if (PdfCryptoService.IsPasswordProtected(sourcePath))
                return new BatchFileResult(sourcePath, null, false,
                    "protetto da password: saltato (aprilo singolarmente)");

            var notes = new List<string>();
            var current = sourcePath;

            if (recipe.RunOcr)
            {
                if (ocrRenderPage is null || tessdataPath is null)
                {
                    notes.Add("OCR non disponibile: saltato");
                }
                else
                {
                    var step = NextTemp(tempFiles);
                    var ocr = PdfOcrService.MakeSearchable(current, step, tessdataPath,
                        ocrRenderPage, ocrRenderScale);
                    current = step;
                    notes.Add(ocr.PagesProcessed == 0
                        ? "OCR: già testuale"
                        : $"OCR: {ocr.PagesProcessed} pagine riconosciute");
                }
            }

            if (recipe.Redact)
            {
                var matches = PdfRedactionService.Scan(current);
                var step = NextTemp(tempFiles);
                var redaction = PdfRedactionService.Apply(current, step, matches, stripMetadata: true);
                current = step;
                notes.Add($"anonimizzati {redaction.ItemsRedacted} dati, metadati ripuliti");
                if (redaction.SkippedLines.Count > 0)
                    notes.Add($"ATTENZIONE: {redaction.SkippedLines.Count} righe non rimovibili");
            }

            if (recipe.Compress)
            {
                var step = NextTemp(tempFiles);
                var compression = PdfCompressor.Compress(current, step);
                current = step;
                notes.Add($"compresso {compression.BeforeBytes / 1024:N0}→{compression.AfterBytes / 1024:N0} KB");
            }

            if (!string.IsNullOrEmpty(recipe.ProtectPassword))
            {
                var step = NextTemp(tempFiles);
                PdfCryptoService.Encrypt(current, step, recipe.ProtectPassword);
                current = step;
                notes.Add("protetto con password");
            }

            if (current == sourcePath)
                return new BatchFileResult(sourcePath, null, false, "nessun passo applicato");

            var outputPath = UniqueOutputPath(sourcePath, outputDirectory);
            File.Copy(current, outputPath, overwrite: true);
            return new BatchFileResult(sourcePath, outputPath, true, string.Join("; ", notes));
        }
        catch (Exception ex)
        {
            return new BatchFileResult(sourcePath, null, false, $"errore: {ex.Message}");
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private static string NextTemp(List<string> tempFiles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrameEditor");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
        tempFiles.Add(path);
        return path;
    }

    private static string UniqueOutputPath(string sourcePath, string outputDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var candidate = Path.Combine(outputDirectory, $"{name}.pdf");
        if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase) || File.Exists(candidate))
            candidate = Path.Combine(outputDirectory, $"{name} - elaborato.pdf");
        var counter = 2;
        while (File.Exists(candidate))
            candidate = Path.Combine(outputDirectory, $"{name} - elaborato ({counter++}).pdf");
        return candidate;
    }
}
