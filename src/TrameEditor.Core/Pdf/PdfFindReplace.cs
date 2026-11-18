namespace TrameEditor.Core.Pdf;

/// <summary>Un'occorrenza trovata: la riga che la contiene e come diventerebbe.</summary>
public sealed record FindMatch(
    PdfTextLine Line,
    int Index,
    string Before,
    string After)
{
    /// <summary>Quante volte il testo cercato compare in questa riga.</summary>
    public int Occurrences { get; init; } = 1;
}

/// <summary>Esito di una sostituzione su tutto il documento.</summary>
public sealed record FindReplaceResult(
    int LinesChanged,
    int OccurrencesReplaced,
    IReadOnlyList<PdfTextLine> SkippedLines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Vero se tutte le righe che contenevano il testo sono state riscritte.</summary>
    public bool Complete => SkippedLines.Count == 0;
}

/// <summary>
/// Cerca e sostituisce su tutte le pagine. Non è un ciclo sulla modifica di una riga: le
/// righe si riscrivono <b>tutte insieme</b> con una sola passata sul documento, come fa
/// l'anonimizzazione, perché riaprire e riscrivere il file una volta per occorrenza
/// moltiplicherebbe il lavoro e, su documenti grandi, anche i modi di sbagliare.
/// <para>
/// Prima si cerca e si mostra, poi si applica: una sostituzione su tutto il documento è la
/// cosa più facile da sbagliare che TrameEditor sappia fare, e chi la lancia deve poter
/// vedere cosa cambierà prima che cambi.
/// </para>
/// </summary>
public static class PdfFindReplace
{
    /// <summary>Le occorrenze, in ordine di pagina e di riga. Non tocca il file.</summary>
    public static IReadOnlyList<FindMatch> Find(string path, string search, string replacement,
        bool caseSensitive = false, bool wholeWord = false)
    {
        if (string.IsNullOrEmpty(search))
            throw new ArgumentException("Il testo da cercare non può essere vuoto.", nameof(search));

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        using var inspector = new PdfTextInspector(path);
        var matches = new List<FindMatch>();

        for (var page = 1; page <= inspector.PageCount; page++)
        {
            var index = 0;
            foreach (var line in inspector.GetLines(page))
            {
                var current = index++;
                var occurrences = Count(line.Text, search, comparison, wholeWord);
                if (occurrences == 0)
                    continue;

                matches.Add(new FindMatch(line, current, line.Text,
                    Apply(line.Text, search, replacement, comparison, wholeWord))
                {
                    Occurrences = occurrences,
                });
            }
        }
        return matches;
    }

    /// <summary>
    /// Applica le occorrenze scelte scrivendo un file nuovo. Chi chiama decide quali: da qui
    /// non si rilegge il documento, così quello che si applica è esattamente quello che è
    /// stato mostrato.
    /// </summary>
    public static FindReplaceResult Replace(string sourcePath, string targetPath,
        IReadOnlyList<FindMatch> matches)
    {
        if (matches.Count == 0)
            throw new InvalidOperationException(
                "Non è stata scelta nessuna occorrenza: non c'è niente da sostituire.");

        var warnings = new List<string>();

        // Una riga che compare due volte nell'elenco verrebbe riscritta due volte, e la
        // seconda sovrascriverebbe la prima: si tiene la prima e lo si dice.
        var byLine = new List<(PdfTextLine Line, string NewText)>();
        var seen = new HashSet<PdfTextLine>();
        foreach (var match in matches)
        {
            if (!seen.Add(match.Line))
            {
                warnings.Add($"La riga «{Shorten(match.Before)}» era in elenco più di una volta: " +
                             "è stata riscritta una sola volta, con tutte le sue occorrenze.");
                continue;
            }
            byLine.Add((match.Line, match.After));
        }

        var result = PdfTextReplacer.ReplaceMany(sourcePath, targetPath, byLine);

        if (result.SkippedLines.Count > 0)
            warnings.Add($"{result.SkippedLines.Count} righe non sono state riscritte: il testo " +
                         "che si vede lì non risulta disegnato dal contenuto della pagina. " +
                         "Nel file prodotto quelle righe sono rimaste come prima.");

        var replacedOccurrences = matches
            .Where(m => !result.SkippedLines.Contains(m.Line))
            .Sum(m => m.Occurrences);

        return new FindReplaceResult(result.LinesReplaced, replacedOccurrences,
            result.SkippedLines, warnings);
    }

    private static int Count(string text, string search, StringComparison comparison, bool wholeWord)
    {
        var count = 0;
        var from = 0;
        while (from <= text.Length - search.Length)
        {
            var at = text.IndexOf(search, from, comparison);
            if (at < 0)
                break;
            if (!wholeWord || IsWholeWord(text, at, search.Length))
                count++;
            from = at + search.Length;
        }
        return count;
    }

    private static string Apply(string text, string search, string replacement,
        StringComparison comparison, bool wholeWord)
    {
        var builder = new System.Text.StringBuilder();
        var from = 0;
        while (from <= text.Length - search.Length)
        {
            var at = text.IndexOf(search, from, comparison);
            if (at < 0)
                break;

            builder.Append(text, from, at - from);
            if (!wholeWord || IsWholeWord(text, at, search.Length))
                builder.Append(replacement);
            else
                builder.Append(text, at, search.Length);
            from = at + search.Length;
        }
        builder.Append(text, from, text.Length - from);
        return builder.ToString();
    }

    /// <summary>Parola intera: ai lati non deve esserci né una lettera né una cifra. Serve a
    /// non trasformare «via» dentro «ovvia».</summary>
    private static bool IsWholeWord(string text, int at, int length)
    {
        var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
        var end = at + length;
        var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return before && after;
    }

    private static string Shorten(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";
}
