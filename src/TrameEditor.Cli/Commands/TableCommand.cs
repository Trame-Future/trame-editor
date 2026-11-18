using TrameEditor.Core.Pdf;
using TrameEditor.Core.Tables;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Riconosce le tabelle di un PDF e le restituisce, oppure le salva in CSV o Excel.
/// Senza destinazione la griglia esce nel JSON: è la forma che serve a un agente, che deve
/// leggere i dati e non aprire un foglio di calcolo.
/// </summary>
public static class TableCommand
{
    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "file.pdf"));
        var destination = line.Positional.Count > 1
            ? Paths.Target(line.At(1, "destinazione"), line.Has("sovrascrivi"))
            : null;

        var tables = Read(source, line);
        var found = tables.Where(t => !t.IsEmpty).ToList();

        var result = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "tabella",
            ["file"] = source,
            ["pagineLette"] = tables.Count,
            ["tabelleTrovate"] = found.Count,
            ["tabelle"] = found.Select(Describe).ToList(),
        };

        if (destination is not null)
        {
            if (found.Count == 0)
                throw new InvalidOperationException(
                    "Non è stata riconosciuta nessuna tabella: non c'è niente da salvare. " +
                    "Una tabella si riconosce da almeno due righe incolonnate allo stesso modo.");

            TableExporter.Export(found, destination, separator: SeparatorOf(line));
            result["destinazione"] = destination;
            result["formato"] = TableExporter.FormatOf(destination).ToString().ToUpperInvariant();
        }

        return result;
    }

    private static List<PdfTable> Read(string source, CommandLine line)
    {
        using var inspector = new PdfTextInspector(source);
        var pages = line.Has("tutte")
            ? Enumerable.Range(1, inspector.PageCount).ToList()
            : [LinesCommand.Page(line, inspector.PageCount)];

        return [.. pages.Select(page => PdfTableReader.Read(source, page))];
    }

    private static char SeparatorOf(CommandLine line)
    {
        var separator = line.Value("separatore");
        if (string.IsNullOrEmpty(separator))
            return ';';
        if (separator.Length != 1)
            throw new UsageException(
                $"Il separatore è un carattere solo, non «{separator}».");
        return separator[0];
    }

    private static object Describe(PdfTable table) => new Dictionary<string, object?>
    {
        ["pagina"] = table.PageNumber,
        ["righe"] = table.RowCount,
        ["colonne"] = table.ColumnCount,
        // Le righe della pagina che non sono finite nella tabella: titoli, prosa, piè di
        // pagina. Chi legge deve sapere che non ha in mano tutta la pagina.
        ["righeFuoriTabella"] = table.IgnoredLines,
        ["dati"] = table.Rows,
    };
}
