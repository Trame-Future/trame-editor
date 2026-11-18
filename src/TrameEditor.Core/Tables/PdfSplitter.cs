using TrameEditor.Core.Pdf;
using UglyToad.PdfPig;

namespace TrameEditor.Core.Tables;

/// <summary>Un documento ritagliato dalla stampa cumulativa.</summary>
public sealed record SplitPart(string Value, int FirstPage, int LastPage, string OutputPath)
{
    public int PageCount => LastPage - FirstPage + 1;
}

/// <summary>Esito della divisione, con quello che non ha funzionato.</summary>
public sealed record SplitResult(
    IReadOnlyList<SplitPart> Parts,
    int PagesWithoutValue,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Divide una stampa cumulativa — duecento fatture in un PDF solo — ogni volta che cambia
/// il valore letto in una certa area della pagina: il numero del documento, il codice del
/// cliente. Il nome del file lo dà quel valore.
/// <para>
/// È il verso opposto dell'unione: i gestionali stampano tutto insieme perché è comodo per
/// loro, e chi riceve il file deve poi ritrovare il singolo documento.
/// </para>
/// </summary>
public static class PdfSplitter
{
    /// <summary>
    /// <paramref name="field"/> indica dove leggere il valore; il numero di pagina che
    /// contiene è ignorato, perché l'area va cercata su <b>ogni</b> pagina.
    /// </summary>
    public static SplitResult Split(string sourcePath, TemplateField field, string outputFolder,
        string? namePrefix = null, bool overwrite = false)
    {
        Directory.CreateDirectory(outputFolder);
        var values = ReadValuePerPage(sourcePath, field, out var pagesWithoutValue);
        if (values.Count == 0)
            return new SplitResult([], 0, ["Il documento non ha pagine."]);

        var warnings = new List<string>();
        var parts = new List<SplitPart>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (value, first, last) in GroupConsecutive(values))
        {
            var label = string.IsNullOrWhiteSpace(value) ? "senza-valore" : value;
            var name = SafeName(string.IsNullOrEmpty(namePrefix) ? label : $"{namePrefix}{label}");

            // Lo stesso valore che ricompare più avanti, staccato: sono due documenti diversi
            // o uno interrotto? Non si può sapere, quindi si numerano e lo si dice.
            if (seen.TryGetValue(name, out var times))
            {
                seen[name] = times + 1;
                warnings.Add($"Il valore «{label}» ricompare più avanti nel documento, non di " +
                             $"seguito: i blocchi sono stati numerati ({name}, {name}-{times + 1}).");
                name = $"{name}-{times + 1}";
            }
            else
            {
                seen[name] = 1;
            }

            var target = Path.Combine(outputFolder, name + ".pdf");
            if (File.Exists(target) && !overwrite)
                throw new InvalidOperationException(
                    $"Esiste già un file con questo nome: {target}. " +
                    "Scegli un'altra cartella, oppure chiedi di sovrascrivere.");

            var edits = Enumerable.Range(first, last - first + 1)
                .Select(page => new PdfPageEdit(page - 1, 0))
                .ToList();
            PdfPageOperations.Build(sourcePath, edits, target);
            parts.Add(new SplitPart(label, first, last, Path.GetFullPath(target)));
        }

        if (pagesWithoutValue > 0)
            warnings.Add($"{pagesWithoutValue} pagine non avevano nessun testo nell'area indicata: " +
                         "sono rimaste attaccate al documento che le precede (o raccolte in " +
                         "«senza-valore» se stanno all'inizio).");

        // Ogni pagina un documento, su un file lungo: quasi sempre vuol dire che nell'area
        // scelta non c'è un identificativo ma testo che cambia da sé. Il risultato è coerente
        // con quello che è stato chiesto, e proprio per questo va detto: nessuno divide una
        // stampa cumulativa per ritrovarsi una pagina per file.
        if (parts.Count == values.Count && values.Count > 3)
            warnings.Add($"Ogni pagina è diventata un documento ({parts.Count} su {values.Count}): " +
                         "nell'area indicata il testo cambia a ogni pagina. Probabilmente non " +
                         "contiene il numero del documento ma testo normale — conviene " +
                         "spostarla su un dato che si ripete.");
        return new SplitResult(parts, pagesWithoutValue, warnings);
    }

    /// <summary>Il valore letto in quell'area, pagina per pagina.</summary>
    private static List<string> ReadValuePerPage(string sourcePath, TemplateField field, out int empty)
    {
        using var document = PdfDocument.Open(sourcePath);
        var values = new List<string>();
        empty = 0;

        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            var words = document.GetPage(pageNumber).GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Where(w => field.Contains(w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom))
                .OrderByDescending(w => Math.Round(w.BoundingBox.Bottom, 1))
                .ThenBy(w => w.BoundingBox.Left)
                .Select(w => w.Text);

            var value = string.Join(" ", words).Trim();
            if (value.Length == 0)
            {
                empty++;
                // Una pagina muta continua il documento di prima: le fatture di più pagine
                // ripetono il numero solo sulla prima.
                value = values.Count > 0 ? values[^1] : string.Empty;
            }
            values.Add(value);
        }
        return values;
    }

    /// <summary>Raggruppa le pagine consecutive che portano lo stesso valore.</summary>
    private static List<(string Value, int First, int Last)> GroupConsecutive(List<string> values)
    {
        var groups = new List<(string Value, int First, int Last)>();
        for (var i = 0; i < values.Count; i++)
        {
            if (groups.Count > 0 && string.Equals(groups[^1].Value, values[i], StringComparison.Ordinal))
            {
                groups[^1] = (groups[^1].Value, groups[^1].First, i + 1);
                continue;
            }
            groups.Add((values[i], i + 1, i + 1));
        }
        return groups;
    }

    /// <summary>Il valore arriva dal documento, cioè da fuori: va ripulito prima di farne un
    /// nome di file, o un codice con una barra finirebbe per scrivere in un'altra cartella.</summary>
    public static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        cleaned = cleaned.Trim('.', ' ');
        if (cleaned.Length > 100)
            cleaned = cleaned[..100];
        return cleaned.Length == 0 ? "documento" : cleaned;
    }
}
