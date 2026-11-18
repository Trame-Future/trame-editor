using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Cerca un testo in tutto il documento e, se viene indicata una destinazione, lo sostituisce.
/// Senza destinazione si limita a dire dove si trova: è il modo di guardare prima di toccare.
/// </summary>
public static class FindReplaceCommand
{
    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "file.pdf"));
        var destination = line.Positional.Count > 1
            ? Paths.Target(line.At(1, "destinazione.pdf"), line.Has("sovrascrivi"))
            : null;

        var search = line.Required("cerca");
        var replacement = line.Value("nuovo") ?? string.Empty;
        var caseSensitive = line.Has("maiuscole");
        var wholeWord = line.Has("parola-intera");

        var matches = PdfFindReplace.Find(source, search, replacement, caseSensitive, wholeWord);

        var result = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "trova",
            ["file"] = source,
            ["cercato"] = search,
            ["righeTrovate"] = matches.Count,
            ["occorrenzeTrovate"] = matches.Sum(m => m.Occurrences),
            ["occorrenze"] = matches.Select(m => new Dictionary<string, object?>
            {
                ["pagina"] = m.Line.PageNumber,
                ["indice"] = m.Index,
                ["volte"] = m.Occurrences,
                ["prima"] = m.Before,
                ["dopo"] = m.After,
                ["modificabile"] = m.Line.IsEditable,
                ["motivo"] = m.Line.NotEditableReason,
            }).ToList(),
        };

        if (destination is null)
            return result;

        if (!line.Has("nuovo"))
            throw new UsageException(
                "Per sostituire serve --nuovo \"<testo>\" (anche vuoto, per cancellare).");
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"«{search}» non compare in questo documento: non c'è niente da sostituire.");

        var applied = PdfFindReplace.Replace(source, destination, matches);
        result["destinazione"] = destination;
        result["righeRiscritte"] = applied.LinesChanged;
        result["occorrenzeSostituite"] = applied.OccurrencesReplaced;
        result["completo"] = applied.Complete;
        result["avvisi"] = applied.Warnings;
        result["righeSaltate"] = applied.SkippedLines
            .Select(l => new Dictionary<string, object?>
            {
                ["pagina"] = l.PageNumber,
                ["testo"] = l.Text,
                ["motivo"] = l.NotEditableReason ?? "il testo non è stato trovato nel contenuto della pagina",
            })
            .ToList();
        return result;
    }
}
