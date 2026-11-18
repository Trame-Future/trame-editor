using TrameEditor.Core.Tables;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Applica un modello di estrazione a un documento o a una cartella: i campi segnati una
/// volta tornano come dati, documento dopo documento.
/// </summary>
public static class ExtractCommand
{
    public static object Run(CommandLine line)
    {
        var template = ExtractionTemplate.Load(Paths.ExistingFile(line.Required("modello")));
        var source = line.At(0, "file.pdf o cartella");
        var destination = line.Positional.Count > 1
            ? Paths.Target(line.At(1, "destinazione"), line.Has("sovrascrivi"))
            : null;

        var documents = Directory.Exists(source)
            ? TemplateExtractor.ApplyToFolder(template, Path.GetFullPath(source), line.Has("sottocartelle"))
            : [TemplateExtractor.Apply(template, Paths.ExistingFile(source))];

        var result = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "estrai",
            ["modello"] = template.Name,
            ["origine"] = Path.GetFullPath(source),
            ["documenti"] = documents.Count,
            // Quanti sono usciti con tutti i campi pieni: è il numero da guardare per sapere
            // se il modello è ancora buono per i documenti di oggi.
            ["completi"] = documents.Count(d => d.Complete),
            ["dati"] = documents.Select(d => new Dictionary<string, object?>
            {
                ["file"] = Path.GetFileName(d.SourcePath),
                ["completo"] = d.Complete,
                ["campiVuoti"] = d.MissingFields,
                ["campi"] = d.Fields,
            }).ToList(),
        };

        if (destination is not null)
        {
            TableExporter.Export(TemplateExtractor.ToTable(template, documents), destination);
            result["destinazione"] = destination;
            result["formato"] = TableExporter.FormatOf(destination).ToString().ToUpperInvariant();
        }
        return result;
    }
}
