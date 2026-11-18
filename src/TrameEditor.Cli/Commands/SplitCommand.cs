using TrameEditor.Core.Tables;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Divide una stampa cumulativa ogni volta che cambia il valore letto in un'area della
/// pagina. L'area si indica con un modello (il campo da usare) oppure a mano in punti PDF.
/// </summary>
public static class SplitCommand
{
    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "file.pdf"));
        var folder = Paths.Folder(line.At(1, "cartella di destinazione"));
        var field = FieldOf(line);

        var result = PdfSplitter.Split(source, field, folder,
            line.Value("prefisso"), line.Has("sovrascrivi"));

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "dividi",
            ["origine"] = source,
            ["destinazione"] = folder,
            ["documentiProdotti"] = result.Parts.Count,
            ["pagineSenzaValore"] = result.PagesWithoutValue,
            // Gli avvisi non sono decorazione: dicono quando due blocchi con lo stesso valore
            // sono stati numerati, o quante pagine sono rimaste attaccate a quella prima.
            ["avvisi"] = result.Warnings,
            ["documenti"] = result.Parts.Select(p => new Dictionary<string, object?>
            {
                ["valore"] = p.Value,
                ["daPagina"] = p.FirstPage,
                ["aPagina"] = p.LastPage,
                ["file"] = p.OutputPath,
            }).ToList(),
        };
    }

    /// <summary>L'area da leggere: dal campo di un modello, o indicata a mano.</summary>
    private static TemplateField FieldOf(CommandLine line)
    {
        if (line.Has("modello"))
        {
            var template = ExtractionTemplate.Load(Paths.ExistingFile(line.Required("modello")));
            var name = line.Value("campo");
            if (name is null)
                return template.Fields[0];

            return template.Fields.FirstOrDefault(f =>
                       string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new UsageException(
                       $"Nel modello «{template.Name}» non c'è un campo «{name}». " +
                       $"Ci sono: {string.Join(", ", template.Fields.Select(f => f.Name))}.");
        }

        if (!line.Has("area"))
            throw new UsageException(
                "Indica dove leggere il valore su cui dividere: --modello <file.json> " +
                "(con --campo <nome>), oppure --area \"sinistra,base,larghezza,altezza\" in punti PDF.");

        var parts = line.Required("area").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts.All(p => double.TryParse(p,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)))
            throw new UsageException(
                "L'area vuole quattro numeri separati da virgola: sinistra,base,larghezza,altezza " +
                "(in punti PDF, origine in basso a sinistra). Il punto è il separatore decimale.");

        var numbers = parts.Select(p => double.Parse(p,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new TemplateField("area", 1, numbers[0], numbers[1], numbers[2], numbers[3]);
    }
}
