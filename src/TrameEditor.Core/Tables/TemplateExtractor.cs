using UglyToad.PdfPig;

namespace TrameEditor.Core.Tables;

/// <summary>Quello che un modello ha letto da un documento.</summary>
public sealed record ExtractedDocument(
    string SourcePath,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> MissingFields)
{
    /// <summary>Vero se tutti i campi del modello hanno trovato qualcosa. Un campo vuoto non
    /// è un errore — può esserlo davvero — ma chi guarda deve saperlo.</summary>
    public bool Complete => MissingFields.Count == 0;
}

/// <summary>
/// Applica un modello a uno o più documenti: per ogni campo prende le parole che cadono
/// nell'area segnata e le rimette in fila.
/// <para>
/// Niente riconoscimento automatico del tipo di documento: il modello si sceglie, non si
/// indovina. Indovinare vorrebbe dire applicare a una fattura il modello di un'altra e
/// restituire numeri plausibili ma sbagliati, che è il modo peggiore di fallire.
/// </para>
/// </summary>
public static class TemplateExtractor
{
    public static ExtractedDocument Apply(ExtractionTemplate template, string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var fields = new Dictionary<string, string>();
        var missing = new List<string>();

        foreach (var pageNumber in template.Pages())
        {
            if (pageNumber < 1 || pageNumber > document.NumberOfPages)
                continue; // documento più corto dell'esemplare: i campi di quella pagina restano vuoti

            var words = document.GetPage(pageNumber).GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => (w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom))
                .ToList();

            foreach (var field in template.Fields.Where(f => f.PageNumber == pageNumber))
            {
                var inside = words
                    .Where(w => field.Contains(w.Left, w.Right, w.Bottom))
                    // Si rimette in ordine di lettura: prima dall'alto al basso, poi da
                    // sinistra a destra. Un campo può prendere due righe (un indirizzo).
                    .OrderByDescending(w => Math.Round(w.Bottom, 1))
                    .ThenBy(w => w.Left)
                    .Select(w => w.Text)
                    .ToList();
                fields[field.Name] = string.Join(" ", inside);
            }
        }

        foreach (var field in template.Fields)
        {
            if (!fields.ContainsKey(field.Name))
                fields[field.Name] = string.Empty;
            if (string.IsNullOrWhiteSpace(fields[field.Name]))
                missing.Add(field.Name);
        }

        return new ExtractedDocument(Path.GetFullPath(pdfPath), fields, missing);
    }

    /// <summary>
    /// Lo stesso modello su una cartella di documenti. L'ordine è quello alfabetico dei nomi,
    /// per avere sempre lo stesso risultato: l'ordine in cui il sistema restituisce i file
    /// non è garantito, e un rapporto che cambia ordine a ogni esecuzione è difficile da
    /// confrontare con quello di ieri.
    /// </summary>
    public static IReadOnlyList<ExtractedDocument> ApplyToFolder(
        ExtractionTemplate template, string folder, bool includeSubfolders = false)
    {
        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folder, "*.pdf", option).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var results = new List<ExtractedDocument>();
        foreach (var file in files)
        {
            try
            {
                results.Add(Apply(template, file));
            }
            catch (Exception ex)
            {
                // Un file rotto in mezzo a cento non deve fermare gli altri novantanove: si
                // registra come documento senza campi, con il motivo al posto del primo.
                var fields = template.Fields.ToDictionary(f => f.Name, _ => string.Empty);
                if (fields.Count > 0)
                    fields[template.Fields[0].Name] = $"(non leggibile: {ex.Message})";
                results.Add(new ExtractedDocument(Path.GetFullPath(file), fields,
                    [.. template.Fields.Select(f => f.Name)]));
            }
        }
        return results;
    }

    /// <summary>Trasforma i documenti letti in una griglia — una riga per documento, una
    /// colonna per campo, più il nome del file — così si salva con lo stesso esportatore
    /// delle tabelle.</summary>
    public static PdfTable ToTable(ExtractionTemplate template, IReadOnlyList<ExtractedDocument> documents)
    {
        var header = new List<string> { "File" };
        header.AddRange(template.Fields.Select(f => f.Name));

        var rows = new List<IReadOnlyList<string>> { header };
        foreach (var document in documents)
        {
            var row = new List<string> { Path.GetFileName(document.SourcePath) };
            row.AddRange(template.Fields.Select(f => document.Fields.GetValueOrDefault(f.Name, string.Empty)));
            rows.Add(row);
        }

        var lefts = Enumerable.Range(0, header.Count).Select(i => (double)i).ToList();
        return new PdfTable(1, rows, lefts, 0);
    }
}
