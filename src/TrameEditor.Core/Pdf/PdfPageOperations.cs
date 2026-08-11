using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TrameEditor.Core.Pdf;

/// <summary>Una pagina del PDF di destinazione: indice nel file di origine + rotazione aggiuntiva.</summary>
public sealed record PdfPageEdit(int OriginalIndex, int RotationDelta);

public static class PdfPageOperations
{
    public static int GetPageCount(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>
    /// Costruisce un nuovo PDF prendendo dal file di origine le pagine indicate,
    /// nell'ordine indicato, applicando le rotazioni. Copre riordino, eliminazione,
    /// rotazione ed estrazione. La scrittura è atomica e la destinazione può
    /// coincidere con l'origine.
    /// </summary>
    public static void Build(string sourcePath, IReadOnlyList<PdfPageEdit> pages, string targetPath)
    {
        if (pages.Count == 0)
            throw new ArgumentException("Un PDF deve contenere almeno una pagina.", nameof(pages));

        using var target = new PdfDocument();
        // AddPage clona la pagina dentro il target: l'origine va chiusa prima della
        // scrittura, così la destinazione può coincidere con il file di origine.
        using (var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
        {
            foreach (var edit in pages)
            {
                var page = target.AddPage(source.Pages[edit.OriginalIndex]);
                page.Rotate = NormalizeRotation(page.Rotate + edit.RotationDelta);
            }
        }
        CopyIdentity(sourcePath, target);
        SaveAtomic(target, targetPath);
    }

    public static void Merge(IReadOnlyList<string> sourcePaths, string targetPath)
    {
        if (sourcePaths.Count < 2)
            throw new ArgumentException("Per unire servono almeno due PDF.", nameof(sourcePaths));

        using var target = new PdfDocument();
        foreach (var sourcePath in sourcePaths)
        {
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            for (var i = 0; i < source.PageCount; i++)
                target.AddPage(source.Pages[i]);
        }
        SaveAtomic(target, targetPath);
    }

    private static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>
    /// Rimontare le pagine non cambia <i>quale</i> documento è: titolo, autore e
    /// lingua devono sopravvivere. Senza questo si perdevano — e con la lingua se
    /// ne va l'accessibilità, perché una sintesi vocale non sa più come leggerlo.
    /// </summary>
    private static void CopyIdentity(string sourcePath, PdfDocument target)
    {
        try
        {
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.InformationOnly);

            if (!string.IsNullOrEmpty(source.Info.Title))
                target.Info.Title = source.Info.Title;
            if (!string.IsNullOrEmpty(source.Info.Author))
                target.Info.Author = source.Info.Author;
            if (!string.IsNullOrEmpty(source.Info.Subject))
                target.Info.Subject = source.Info.Subject;
            if (!string.IsNullOrEmpty(source.Info.Keywords))
                target.Info.Keywords = source.Info.Keywords;

            var language = source.Internals.Catalog.Elements.GetString("/Lang");
            if (!string.IsNullOrEmpty(language))
                target.Internals.Catalog.Elements.SetString("/Lang", language);

            var preferences = source.Internals.Catalog.Elements.GetDictionary("/ViewerPreferences");
            if (preferences?.Elements.GetBoolean("/DisplayDocTitle") == true)
            {
                var copy = new PdfDictionary(target);
                copy.Elements.SetBoolean("/DisplayDocTitle", true);
                target.Internals.Catalog.Elements.SetObject("/ViewerPreferences", copy);
            }
        }
        catch (Exception)
        {
            // i metadati sono un di più: non devono impedire di salvare le pagine
        }
    }

    private static void SaveAtomic(PdfDocument document, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Percorso senza cartella: {path}", nameof(path));

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            document.Save(tempPath);
            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
