using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

public sealed record RedactionResult(
    int ItemsRedacted,
    int LinesChanged,
    IReadOnlyList<PdfTextLine> SkippedLines,
    bool MetadataStripped);

/// <summary>
/// Anonimizzazione: i dati sensibili selezionati vengono <b>rimossi davvero</b>
/// dal content stream (la riga è riscritta con i dati mascherati da 'X') —
/// non coperti con un rettangolo che lascia il testo copiabile sotto.
/// Opzionalmente ripulisce i metadati del documento (autore, titolo, XMP).
/// </summary>
public static class PdfRedactionService
{
    public static IReadOnlyList<SensitiveMatch> Scan(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var matches = new List<SensitiveMatch>();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
                matches.AddRange(SensitiveDataScanner.ScanLine(line));
        }
        return matches;
    }

    public static RedactionResult Apply(string sourcePath, string targetPath,
        IReadOnlyList<SensitiveMatch> selected, bool stripMetadata)
    {
        var edits = selected
            .GroupBy(m => m.Line)
            .Select(group => (Line: group.Key,
                NewText: SensitiveDataScanner.MaskLine(group.Key.Text, group)))
            .ToList();

        PdfReplaceManyResult replaceResult;
        if (edits.Count > 0)
        {
            replaceResult = PdfTextReplacer.ReplaceMany(sourcePath, targetPath, edits);
        }
        else
        {
            File.Copy(sourcePath, Path.GetFullPath(targetPath), overwrite: true);
            replaceResult = new PdfReplaceManyResult(0, []);
        }

        if (stripMetadata)
            StripMetadata(targetPath, targetPath);

        var redactedItems = selected.Count(m => !replaceResult.SkippedLines.Contains(m.Line));
        return new RedactionResult(redactedItems, replaceResult.LinesReplaced,
            replaceResult.SkippedLines, stripMetadata);
    }

    /// <summary>Rimuove i metadati identificativi: Info (autore, titolo, oggetto,
    /// parole chiave, applicazione) e il flusso XMP del catalogo.</summary>
    public static void StripMetadata(string sourcePath, string targetPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                var info = document.GetDocumentInfo();
                info.SetAuthor(string.Empty);
                info.SetTitle(string.Empty);
                info.SetSubject(string.Empty);
                info.SetKeywords(string.Empty);
                info.SetCreator(string.Empty);
                document.GetCatalog().GetPdfObject().Remove(PdfName.Metadata);
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
