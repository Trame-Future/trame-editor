using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>Una corrispondenza trovata: quale file, quale pagina, e la riga.</summary>
public sealed record FolderSearchHit(string FilePath, int PageNumber, string Snippet)
{
    public string FileName => Path.GetFileName(FilePath);

    public string Display => $"{FileName} — pag. {PageNumber}: {Snippet}";
}

public sealed record FolderSearchReport(
    IReadOnlyList<FolderSearchHit> Hits,
    int FilesSearched,
    IReadOnlyList<string> FilesWithoutText,
    IReadOnlyList<string> FilesNotReadable)
{
    public int FilesWithHits => Hits.Select(h => h.FilePath).Distinct().Count();
}

/// <summary>
/// Cerca una parola dentro tutti i PDF di una cartella: "in quale delle 400
/// fatture c'è questo codice fiscale?".
/// <para>
/// I PDF senza testo (le scansioni non passate dall'OCR) vengono <b>elencati a
/// parte</b>: un file in cui non abbiamo potuto cercare non è un file in cui la
/// parola non c'è, e confondere le due cose farebbe concludere il falso.
/// </para>
/// </summary>
public static class FolderSearchService
{
    private const int SnippetRadius = 60;

    public static FolderSearchReport Search(string folder, string query,
        bool includeSubfolders = false, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Indica che cosa cercare.", nameof(query));

        var files = Directory.EnumerateFiles(folder, "*.pdf",
                includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hits = new List<FolderSearchHit>();
        var withoutText = new List<string>();
        var notReadable = new List<string>();
        var searched = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{searched + 1} di {files.Count}: {Path.GetFileName(file)}");
            searched++;

            try
            {
                var found = SearchFile(file, query, out var hadAnyText);
                hits.AddRange(found);
                if (!hadAnyText)
                    withoutText.Add(file);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                notReadable.Add(file);
            }
        }

        return new FolderSearchReport(hits, searched, withoutText, notReadable);
    }

    private static List<FolderSearchHit> SearchFile(string path, string query, out bool hadAnyText)
    {
        var hits = new List<FolderSearchHit>();
        hadAnyText = false;

        using var inspector = new PdfTextInspector(path);
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
            {
                if (line.Text.Trim().Length > 0)
                    hadAnyText = true;
                var index = line.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    hits.Add(new FolderSearchHit(path, page, Snippet(line.Text, index, query.Length)));
            }
        }
        return hits;
    }

    private static string Snippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(text.Length, index + length + SnippetRadius);
        var snippet = text[start..end].Trim();
        if (start > 0)
            snippet = "…" + snippet;
        if (end < text.Length)
            snippet += "…";
        return snippet;
    }
}
