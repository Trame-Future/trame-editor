namespace TrameEditor.Core.Documents;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed,
}

/// <summary>Una riga del confronto. I riferimenti sono la pagina (PDF) o la riga
/// (testo) del documento di sinistra (Removed/Unchanged) o di destra (Added).</summary>
public sealed record DiffEntry(DiffKind Kind, string Text, int? LeftRef, int? RightRef);

public sealed record CompareResult(
    IReadOnlyList<DiffEntry> Entries,
    int AddedCount,
    int RemovedCount,
    DocumentUnit LeftUnit,
    DocumentUnit RightUnit)
{
    public bool AreIdentical => AddedCount == 0 && RemovedCount == 0;

    /// <summary>Etichetta da mostrare accanto ai riferimenti. Se i due documenti
    /// sono di tipo diverso (un PDF contro un .txt) si usa quella di sinistra e si
    /// dichiara la differenza.</summary>
    public string ReferenceLabel => LeftUnit.ShortLabel();

    public bool MixedTypes => LeftUnit != RightUnit;
}

/// <summary>
/// Confronto testuale di due documenti, riga per riga (LCS): cosa è stato
/// aggiunto, rimosso o è rimasto identico fra due versioni. Funziona su PDF,
/// TXT e Markdown, anche misti — perché il confronto guarda il testo, non il
/// formato (e non guarda la grafica).
/// </summary>
public static class DocumentComparer
{
    private const long MaxLcsCells = 25_000_000;

    public static CompareResult Compare(string leftPath, string rightPath)
    {
        var (leftUnit, left) = DocumentTextReader.ReadLines(leftPath);
        var (rightUnit, right) = DocumentTextReader.ReadLines(rightPath);

        var entries = new List<DiffEntry>();

        // prefisso comune
        var start = 0;
        while (start < left.Count && start < right.Count && left[start].Text == right[start].Text)
        {
            entries.Add(new DiffEntry(DiffKind.Unchanged, left[start].Text,
                left[start].Reference, right[start].Reference));
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
            entries.Add(new DiffEntry(DiffKind.Unchanged, l.Text, l.Reference, r.Reference));
        }

        return new CompareResult(entries,
            entries.Count(e => e.Kind == DiffKind.Added),
            entries.Count(e => e.Kind == DiffKind.Removed),
            leftUnit, rightUnit);
    }

    private static void AppendMiddleDiff(List<DiffEntry> entries,
        List<(string Text, int Reference)> left, int leftStart, int leftEnd,
        List<(string Text, int Reference)> right, int rightStart, int rightEnd)
    {
        var n = leftEnd - leftStart;
        var m = rightEnd - rightStart;
        if (n == 0 && m == 0)
            return;

        if ((long)n * m > MaxLcsCells)
        {
            // documento enorme: degradazione onesta a rimosso-tutto/aggiunto-tutto
            for (var i = leftStart; i < leftEnd; i++)
                entries.Add(new DiffEntry(DiffKind.Removed, left[i].Text, left[i].Reference, null));
            for (var j = rightStart; j < rightEnd; j++)
                entries.Add(new DiffEntry(DiffKind.Added, right[j].Text, null, right[j].Reference));
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
                entries.Add(new DiffEntry(DiffKind.Unchanged, left[leftStart + a].Text,
                    left[leftStart + a].Reference, right[rightStart + b].Reference));
                a++;
                b++;
            }
            else if (dp[a + 1, b] >= dp[a, b + 1])
            {
                entries.Add(new DiffEntry(DiffKind.Removed, left[leftStart + a].Text,
                    left[leftStart + a].Reference, null));
                a++;
            }
            else
            {
                entries.Add(new DiffEntry(DiffKind.Added, right[rightStart + b].Text,
                    null, right[rightStart + b].Reference));
                b++;
            }
        }
        for (; a < n; a++)
            entries.Add(new DiffEntry(DiffKind.Removed, left[leftStart + a].Text,
                left[leftStart + a].Reference, null));
        for (; b < m; b++)
            entries.Add(new DiffEntry(DiffKind.Added, right[rightStart + b].Text,
                null, right[rightStart + b].Reference));
    }
}
