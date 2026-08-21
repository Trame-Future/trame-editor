using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Riscrive una riga di testo dentro il PDF. La riga si indica con l'indice avuto da
/// <c>righe</c>, oppure con il suo testo esatto: se quel testo compare più volte il
/// comando si ferma ed elenca gli indici, invece di scegliere per conto proprio.
/// </summary>
public static class ReplaceCommand
{
    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "origine.pdf"));
        var target = Paths.Target(line.At(1, "destinazione.pdf"), line.Has("sovrascrivi"));
        var newText = line.Required("nuovo");

        PdfTextLine chosen;
        int index;
        using (var inspector = new PdfTextInspector(source))
        {
            var page = LinesCommand.Page(line, inspector.PageCount);
            var lines = inspector.GetLines(page);
            (chosen, index) = Select(line, lines);
        }

        if (!chosen.IsEditable)
            throw new InvalidOperationException(
                $"Questa riga non è modificabile: {chosen.NotEditableReason ?? "motivo non dichiarato"}.");

        var plan = PdfTextReplacer.PlanFor(source, chosen, newText);
        PdfTextReplacer.Replace(source, target, chosen, newText, plan);

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "sostituisci",
            ["origine"] = source,
            ["destinazione"] = target,
            ["pagina"] = chosen.PageNumber,
            ["indice"] = index,
            ["testoPrecedente"] = chosen.Text,
            ["testoNuovo"] = newText,
            // Quale carattere è stato usato non è un dettaglio: se l'originale non aveva i
            // glifi che servivano, il risultato si vede diverso e chi legge deve saperlo.
            ["carattere"] = new Dictionary<string, object?>
            {
                ["strategia"] = plan.Strategy.ToString(),
                ["descrizione"] = plan.Description,
                ["originaleRiusato"] = plan.Strategy == PdfFontStrategy.ReuseEmbedded,
            },
        };
    }

    private static (PdfTextLine Line, int Index) Select(CommandLine line, IReadOnlyList<PdfTextLine> lines)
    {
        if (line.Has("riga"))
        {
            var index = line.RequiredInt("riga");
            if (index < 0 || index >= lines.Count)
                throw new UsageException(
                    $"La riga {index} non esiste: in questa pagina ce ne sono {lines.Count} (da 0 a {lines.Count - 1}).");
            return (lines[index], index);
        }

        var wanted = line.Value("testo")
            ?? throw new UsageException("Indica quale riga cambiare, con --riga <indice> oppure --testo \"<testo esatto>\".");

        var matches = lines.Select((text, index) => (text, index))
            .Where(pair => pair.text.Text == wanted)
            .ToList();

        return matches.Count switch
        {
            1 => (matches[0].text, matches[0].index),
            0 => throw new UsageException($"In questa pagina non c'è nessuna riga con il testo «{wanted}»."),
            _ => throw new UsageException(
                $"Il testo «{wanted}» compare {matches.Count} volte in questa pagina " +
                $"(indici: {string.Join(", ", matches.Select(m => m.index))}). Indica quale con --riga."),
        };
    }
}
