using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Documents;

/// <summary>Un dato personale trovato in un file di testo.</summary>
public sealed record TextSensitiveMatch(int LineNumber, SensitiveKind Kind, string Value);

/// <summary>Esito dell'anonimizzazione di un file di testo.</summary>
public sealed record TextRedactionResult(
    int LinesChanged,
    int ItemsRedacted,
    IReadOnlyList<TextSensitiveMatch> Found);

/// <summary>
/// Anonimizzazione dei file di testo e Markdown. Lo scanner dei dati personali — codici
/// fiscali, IBAN, email, telefoni, targhe — era già in Core e lo usava solo il PDF: qui
/// non serve riscrivere niente dentro un formato, basta sostituire nel testo.
/// <para>
/// La codifica e i fine riga del file si conservano: un .txt che arriva da un gestionale
/// può essere in ANSI con fine riga di Windows, e restituirlo in UTF-8 con fine riga Unix
/// significa che il programma che lo rilegge non lo riconosce più.
/// </para>
/// </summary>
public static class TextRedactor
{
    /// <summary>Cerca senza toccare niente: serve a mostrare prima cosa verrebbe cancellato.</summary>
    public static IReadOnlyList<TextSensitiveMatch> Scan(string text)
    {
        var found = new List<TextSensitiveMatch>();
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
                continue;

            // Si riusa lo scanner del PDF costruendo una riga finta: la geometria non conta,
            // conta il testo. Duplicare i criteri di riconoscimento vorrebbe dire ritrovarsi
            // due elenchi di schemi che col tempo divergono.
            var fake = new PdfTextLine(1, line, 0, 0, 1, 1, 0, 0, "", 10, 0, 0, 0, true, null);
            foreach (var match in SensitiveDataScanner.ScanLine(fake))
                found.Add(new TextSensitiveMatch(i + 1, match.Kind, match.Value));
        }
        return found;
    }

    /// <summary>Restituisce il testo con i dati scelti sostituiti da X.</summary>
    public static (string Text, TextRedactionResult Result) Redact(string text,
        IReadOnlyCollection<SensitiveKind>? kinds = null)
    {
        kinds ??= [.. Enum.GetValues<SensitiveKind>()];
        var found = Scan(text).Where(m => kinds.Contains(m.Kind)).ToList();
        if (found.Count == 0)
            return (text, new TextRedactionResult(0, 0, found));

        var lines = text.Split('\n');
        var changed = 0;

        foreach (var group in found.GroupBy(m => m.LineNumber))
        {
            var index = group.Key - 1;
            if (index < 0 || index >= lines.Length)
                continue;

            // Il fine riga va conservato: si lavora sul contenuto e si rimette com'era.
            var hadReturn = lines[index].EndsWith('\r');
            var content = hadReturn ? lines[index][..^1] : lines[index];
            var before = content;

            foreach (var match in group.OrderByDescending(m => m.Value.Length))
                content = content.Replace(match.Value, new string('X', match.Value.Length));

            if (content != before)
            {
                lines[index] = hadReturn ? content + '\r' : content;
                changed++;
            }
        }

        return (string.Join('\n', lines), new TextRedactionResult(changed, found.Count, found));
    }

    /// <summary>
    /// Anonimizza un file scrivendone un altro. Codifica e fine riga si conservano passando
    /// dal servizio che l'editor già usa per aprire e salvare: un .txt di un gestionale può
    /// essere in ANSI con fine riga di Windows, e restituirlo in UTF-8 con fine riga Unix
    /// vuol dire che il programma che lo rilegge non lo riconosce più.
    /// </summary>
    public static TextRedactionResult RedactFile(string sourcePath, string targetPath,
        IReadOnlyCollection<SensitiveKind>? kinds = null)
    {
        var loaded = TextFiles.TextFileService.Load(sourcePath);
        var (redacted, result) = Redact(loaded.Content, kinds);

        var full = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        TextFiles.TextFileService.Save(full, redacted, loaded.Format);
        return result;
    }
}
