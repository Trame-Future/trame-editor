using TrameEditor.Core.Tables;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Compila un modulo PDF una volta per ogni riga di una tabella: modello più dati uguale N
/// documenti.
/// </summary>
public static class FormsCommand
{
    public static object Run(CommandLine line)
    {
        var template = Paths.ExistingFile(line.At(0, "modulo.pdf"));
        var data = Paths.ExistingFile(line.At(1, "dati.csv o .xlsx"));
        var folder = Paths.Folder(line.At(2, "cartella di destinazione"));

        var result = FormBatchFiller.Fill(template, data, folder,
            line.Value("nome-da"), line.Has("appiattisci"), line.Has("sovrascrivi"));

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "moduli",
            ["modulo"] = template,
            ["dati"] = data,
            ["destinazione"] = folder,
            ["documentiProdotti"] = result.Documents.Count,
            // Quello che non si è abbinato: un modulo compilato a metà senza dirlo è peggio
            // di uno non compilato.
            ["colonneSenzaCampo"] = result.UnmatchedColumns,
            ["campiSenzaColonna"] = result.UnfilledFields,
            ["avvisi"] = result.Warnings,
            ["documenti"] = result.Documents.Select(d => new Dictionary<string, object?>
            {
                ["riga"] = d.RowNumber,
                ["file"] = d.OutputPath,
                ["campiVuoti"] = d.EmptyFields,
            }).ToList(),
        };
    }
}
