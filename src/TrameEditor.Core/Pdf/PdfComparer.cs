namespace TrameEditor.Core.Pdf;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed,
}

/// <summary>Una riga del confronto: le pagine si riferiscono al documento
/// di sinistra (Removed/Unchanged) o di destra (Added/Unchanged).</summary>
public sealed record PdfDiffEntry(DiffKind Kind, string Text, int? LeftPage, int? RightPage);

public sealed record PdfCompareResult(
    IReadOnlyList<PdfDiffEntry> Entries, int AddedCount, int RemovedCount)
{
    public bool AreIdentical => AddedCount == 0 && RemovedCount == 0;
}

/// <summary>
/// Confronto testuale di due PDF, riga per riga (LCS): mostra cosa è stato
/// aggiunto, rimosso o è rimasto identico tra due versioni di un documento.
/// Confronta il testo, non la grafica.
/// </summary>
public static class PdfComparer
{
    private const long MaxLcsCells = 25_000_000;

    public static PdfCompareResult Compare(string leftPath, string rightPath)
    {
        var left = ExtractLines(leftPath);
        var right = ExtractLines(rightPath);

        var entries = new List<PdfDiffEntry>();

        // prefisso comune
        var start = 0;
        while (start < left.Count && start < right.Count && left[start].Text == right[start].Text)
        {
            entries.Add(new PdfDiffEntry(DiffKind.Unchanged, left[start].Text,
                left[start].Page, right[start].Page));
            start++;
        }

        // suffisso comune (accodato alla fine)
        var endLeft = left.Count;
        var endRight = right.Count;
        while (endLeft > start && endRight > start &&
               left[endLeft - 1].Text == right[endRight - 1].Text)
        {
            endLeft--;
            endRight--;
        }

        AppendMiddleDiff(entries, left, start, endLeft, right, start, endRight);

        for (var i = 0; i < left.Count - endLeft; i++)
        {
            var l = left[endLeft + i];
            var r = right[endRight + i];
            entries.Add(new PdfDiffEntry(DiffKind.Unchanged, l.Text, l.Page, r.Page));
        }

        return new PdfCompareResult(entries,
            entries.Count(e => e.Kind == DiffKind.Added),
            entries.Count(e => e.Kind == DiffKind.Removed));
    }

    private static void AppendMiddleDiff(List<PdfDiffEntry> entries,
        List<(string Text, int Page)> left, int leftStart, int leftEnd,
        List<(string Text, int Page)> right, int rightStart, int rightEnd)
    {
        var n = leftEnd - leftStart;
        var m = rightEnd - rightStart;
        if (n == 0 && m == 0)
            return;

        if ((long)n * m > MaxLcsCells)
        {
            // documento enorme: degradazione onesta a rimosso-tutto/aggiunto-tutto
            for (var i = leftStart; i < leftEnd; i++)
                entries.Add(new PdfDiffEntry(DiffKind.Removed, left[i].Text, left[i].Page, null));
            for (var j = rightStart; j < rightEnd; j++)
                entries.Add(new PdfDiffEntry(DiffKind.Added, right[j].Text, null, right[j].Page));
            return;
        }

        // LCS classico con backtracking
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = left[leftStart + i].Text == right[rightStart + j].Text
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var a = 0;
        var b = 0;
        while (a < n && b < m)
        {
            if (left[leftStart + a].Text == right[rightStart + b].Text)
            {
                entries.Add(new PdfDiffEntry(DiffKind.Unchanged, left[leftStart + a].Text,
                    left[leftStart + a].Page, right[rightStart + b].Page));
                a++;
                b++;
            }
            else if (dp[a + 1, b] >= dp[a, b + 1])
            {
                entries.Add(new PdfDiffEntry(DiffKind.Removed, left[leftStart + a].Text,
                    left[leftStart + a].Page, null));
                a++;
            }
            else
            {
                entries.Add(new PdfDiffEntry(DiffKind.Added, right[rightStart + b].Text,
                    null, right[rightStart + b].Page));
                b++;
            }
        }
        for (; a < n; a++)
            entries.Add(new PdfDiffEntry(DiffKind.Removed, left[leftStart + a].Text,
                left[leftStart + a].Page, null));
        for (; b < m; b++)
            entries.Add(new PdfDiffEntry(DiffKind.Added, right[rightStart + b].Text,
                null, right[rightStart + b].Page));
    }

    private static List<(string Text, int Page)> ExtractLines(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var lines = new List<(string, int)>();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
            {
                var text = line.Text.Trim();
                if (text.Length > 0)
                    lines.Add((text, page));
            }
        }
        return lines;
    }
}
