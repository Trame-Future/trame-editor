using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Unisce più PDF in un fascicolo con segnalibri e indice cliccabile. L'ordine è quello in
/// cui i file sono indicati; puntando una cartella, l'ordine è alfabetico.
/// </summary>
public static class DossierCommand
{
    public static object Run(CommandLine line)
    {
        var target = Paths.Target(line.At(0, "fascicolo.pdf"), line.Has("sovrascrivi"));
        var sources = SourcesOf(line);

        var result = PdfDossierBuilder.Build(sources, target,
            withIndexPage: !line.Has("senza-indice"),
            indexTitle: line.Value("titolo-indice") ?? "Indice");

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "fascicolo",
            ["destinazione"] = target,
            ["documenti"] = result.Entries.Count,
            ["pagineTotali"] = result.TotalPages,
            ["conIndice"] = result.HasIndexPage,
            ["avvisi"] = result.Warnings,
            ["indice"] = result.Entries.Select(e => new Dictionary<string, object?>
            {
                ["titolo"] = e.Title,
                ["daPagina"] = e.FirstPage,
                ["pagine"] = e.PageCount,
            }).ToList(),
        };
    }

    /// <summary>I documenti: quelli elencati dopo la destinazione, oppure quelli di una
    /// cartella indicata con --cartella.</summary>
    private static List<string> SourcesOf(CommandLine line)
    {
        if (line.Has("cartella"))
        {
            var folder = line.Required("cartella");
            if (!Directory.Exists(folder))
                throw new UsageException($"La cartella non esiste: {Path.GetFullPath(folder)}");

            var found = Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (found.Count < 2)
                throw new InvalidOperationException(
                    $"Nella cartella ci sono {found.Count} PDF: per un fascicolo ne servono almeno due.");
            return found;
        }

        var listed = line.Positional.Skip(1).Select(Paths.ExistingFile).ToList();
        if (listed.Count < 2)
            throw new UsageException(
                "Elenca i PDF da unire dopo il nome del fascicolo, oppure indica --cartella <percorso>.");
        return listed;
    }
}
