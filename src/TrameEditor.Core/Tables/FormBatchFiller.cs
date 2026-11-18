using ClosedXML.Excel;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tables;

/// <summary>Un documento prodotto dalla compilazione in serie.</summary>
public sealed record FilledDocument(int RowNumber, string OutputPath, IReadOnlyList<string> EmptyFields);

/// <summary>Esito della compilazione in serie.</summary>
public sealed record FormBatchResult(
    IReadOnlyList<FilledDocument> Documents,
    IReadOnlyList<string> UnmatchedColumns,
    IReadOnlyList<string> UnfilledFields,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Compila un modulo PDF una volta per ogni riga di una tabella: un modello e un elenco di
/// dati diventano N documenti.
/// <para>
/// L'abbinamento fra colonne e campi è per <b>nome</b>, ignorando maiuscole e spazi. Quello
/// che non si abbina non viene indovinato: le colonne avanzate e i campi rimasti vuoti si
/// dichiarano, perché un modulo compilato a metà senza dirlo è peggio di uno non compilato.
/// </para>
/// </summary>
public static class FormBatchFiller
{
    /// <summary>Legge i dati da un CSV (separatore punto e virgola o virgola) o da un XLSX:
    /// la prima riga sono i nomi delle colonne.</summary>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) ReadData(string path)
    {
        var rows = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsx(path)
            : ReadCsv(path);

        if (rows.Count == 0)
            throw new InvalidOperationException($"Il file dei dati è vuoto: {path}");

        var columns = rows[0].Select(c => c.Trim()).ToList();
        if (columns.All(string.IsNullOrEmpty))
            throw new InvalidOperationException(
                "La prima riga del file dei dati deve contenere i nomi delle colonne.");
        return (columns, rows.Skip(1).ToList());
    }

    public static FormBatchResult Fill(string templatePdf, string dataPath, string outputFolder,
        string? nameColumn = null, bool flatten = false, bool overwrite = false)
    {
        Directory.CreateDirectory(outputFolder);
        var (columns, rows) = ReadData(dataPath);
        var fields = PdfFormService.GetFields(templatePdf);
        if (fields.Count == 0)
            throw new InvalidOperationException(
                "Questo PDF non ha campi da compilare: non è un modulo (AcroForm).");

        var fieldByKey = fields.ToDictionary(f => Key(f.Name), f => f.Name);
        var columnToField = new Dictionary<int, string>();
        var unmatchedColumns = new List<string>();

        for (var i = 0; i < columns.Count; i++)
        {
            if (fieldByKey.TryGetValue(Key(columns[i]), out var fieldName))
                columnToField[i] = fieldName;
            else if (!string.IsNullOrWhiteSpace(columns[i]))
                unmatchedColumns.Add(columns[i]);
        }

        if (columnToField.Count == 0)
            throw new InvalidOperationException(
                "Nessuna colonna corrisponde a un campo del modulo. " +
                $"Colonne: {string.Join(", ", columns.Where(c => c.Length > 0))}. " +
                $"Campi del modulo: {string.Join(", ", fields.Select(f => f.Name))}.");

        var nameIndex = NameColumnIndex(columns, nameColumn);
        var warnings = new List<string>();
        var documents = new List<FilledDocument>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
                continue; // righe vuote in fondo al foglio: capitano sempre

            var values = new Dictionary<string, string>();
            var empty = new List<string>();
            foreach (var (index, fieldName) in columnToField)
            {
                var value = index < row.Count ? row[index].Trim() : string.Empty;
                values[fieldName] = value;
                if (value.Length == 0)
                    empty.Add(fieldName);
            }

            var baseName = nameIndex >= 0 && nameIndex < row.Count && row[nameIndex].Trim().Length > 0
                ? PdfSplitter.SafeName(row[nameIndex].Trim())
                : $"documento-{r + 1:D3}";

            var name = baseName;
            if (used.TryGetValue(baseName, out var times))
            {
                used[baseName] = times + 1;
                name = $"{baseName}-{times + 1}";
                warnings.Add($"Il nome «{baseName}» compare più di una volta nei dati: " +
                             $"il documento della riga {r + 2} è stato chiamato «{name}».");
            }
            else
            {
                used[baseName] = 1;
            }

            var target = Path.Combine(outputFolder, name + ".pdf");
            if (File.Exists(target) && !overwrite)
                throw new InvalidOperationException(
                    $"Esiste già un file con questo nome: {target}. " +
                    "Scegli un'altra cartella, oppure chiedi di sovrascrivere.");

            PdfFormService.Fill(templatePdf, target, values, flatten);
            // +2: la prima riga sono i nomi delle colonne, e si conta da 1 come nel foglio.
            documents.Add(new FilledDocument(r + 2, Path.GetFullPath(target), empty));
        }

        var unfilled = fields
            .Select(f => f.Name)
            .Where(name => !columnToField.Values.Contains(name))
            .ToList();

        if (unmatchedColumns.Count > 0)
            warnings.Add($"Colonne senza un campo corrispondente nel modulo, ignorate: " +
                         $"{string.Join(", ", unmatchedColumns)}.");
        if (unfilled.Count > 0)
            warnings.Add($"Campi del modulo rimasti vuoti in tutti i documenti, perché nei dati " +
                         $"non c'è una colonna con quel nome: {string.Join(", ", unfilled)}.");

        return new FormBatchResult(documents, unmatchedColumns, unfilled, warnings);
    }

    /// <summary>La colonna da cui prendere il nome dei file: quella chiesta, o la prima.</summary>
    private static int NameColumnIndex(IReadOnlyList<string> columns, string? nameColumn)
    {
        if (string.IsNullOrWhiteSpace(nameColumn))
            return 0;

        for (var i = 0; i < columns.Count; i++)
        {
            if (Key(columns[i]) == Key(nameColumn))
                return i;
        }
        throw new InvalidOperationException(
            $"Nei dati non c'è una colonna «{nameColumn}» da cui prendere il nome dei file. " +
            $"Colonne: {string.Join(", ", columns.Where(c => c.Length > 0))}.");
    }

    /// <summary>Confronto fra nomi: maiuscole e spazi non devono far fallire un abbinamento
    /// che per una persona è evidente.</summary>
    private static string Key(string name) =>
        new(name.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());

    private static List<List<string>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var separator = lines.Length > 0 && lines[0].Count(c => c == ';') >= lines[0].Count(c => c == ',')
            ? ';'
            : ',';
        return [.. lines.Where(l => l.Length > 0).Select(line => ParseCsvLine(line, separator))];
    }

    /// <summary>Una riga di CSV, rispettando le virgolette: una cella può contenere il
    /// separatore se è fra virgolette, e due virgolette di fila ne valgono una.</summary>
    private static List<string> ParseCsvLine(string line, char separator)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    cell.Append(c);
                continue;
            }

            if (c == '"')
                quoted = true;
            else if (c == separator)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
                cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    private static List<List<string>> ReadXlsx(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var rows = new List<List<string>>();
        var used = sheet.RangeUsed();
        if (used is null)
            return rows;

        foreach (var row in used.Rows())
            rows.Add([.. row.Cells().Select(c => c.GetFormattedString())]);
        return rows;
    }
}
