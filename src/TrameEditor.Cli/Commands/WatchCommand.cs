using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Lavora in un colpo solo i PDF pronti in una cartella, applicando una Ricetta, e sposta gli
/// originali in «fatti» o «non riusciti» tenendo un registro.
/// <para>
/// Da riga di comando la sorveglianza è <b>una passata</b>, non un programma che resta in
/// ascolto: chi vuole la ripetizione la mette dove sa già farlo — l'Utilità di pianificazione
/// di Windows — invece di tenere aperta una finestra di terminale e sperare che nessuno la
/// chiuda. Nell'applicazione, invece, la cartella si guarda finché la finestra è aperta.
/// </para>
/// </summary>
public static class WatchCommand
{
    public static object Run(CommandLine line)
    {
        var inbox = line.At(0, "cartella da sorvegliare");
        if (!Directory.Exists(inbox))
            throw new UsageException($"La cartella non esiste: {Path.GetFullPath(inbox)}");

        var recipe = RecipeOf(line);
        if (recipe.IsContradictory)
            throw new UsageException(
                "Un PDF/A non può essere cifrato: lo dice lo standard. Scegli --pdfa oppure " +
                "--proteggi, non tutti e due.");

        var layout = WatchedFolderLayout.Under(inbox);
        var folder = new WatchedFolder(layout, (source, outputFolder) =>
        {
            // L'OCR qui non si fa: richiede il render delle pagine, che vive nell'applicazione.
            // Dirlo è meglio che fingere una ricetta eseguita a metà.
            var result = BatchProcessor.ProcessFile(source, outputFolder, recipe);
            return new WatchedFileOutcome(Path.GetFileName(source), result.Success,
                result.Outcome, result.OutputPath, DateTime.UtcNow);
        });

        var outcomes = folder.ProcessReady();

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "sorveglia",
            ["cartella"] = layout.Inbox,
            ["lavorati"] = outcomes.Count,
            ["riusciti"] = outcomes.Count(o => o.Success),
            ["registro"] = layout.LogPath,
            ["nota"] = "Una passata sola: per ripeterla nel tempo, pianificala con l'Utilità di " +
                       "pianificazione di Windows.",
            ["esiti"] = outcomes.Select(o => new Dictionary<string, object?>
            {
                ["file"] = o.FileName,
                ["riuscito"] = o.Success,
                ["esito"] = o.Outcome,
                ["prodotto"] = o.OutputPath,
            }).ToList(),
        };
    }

    private static BatchRecipe RecipeOf(CommandLine line)
    {
        var recipe = new BatchRecipe(
            RunOcr: false,
            Redact: line.Has("anonimizza"),
            Compress: line.Has("comprimi"),
            ProtectPassword: line.Value("proteggi"),
            ConvertToPdfA: line.Has("pdfa"));

        if (!recipe.Redact && !recipe.Compress && !recipe.ConvertToPdfA &&
            string.IsNullOrEmpty(recipe.ProtectPassword))
            throw new UsageException(
                "Indica cosa fare ai file: --anonimizza, --comprimi, --pdfa, --proteggi <password> " +
                "(anche più d'uno). Senza, i file verrebbero spostati senza essere lavorati.");
        return recipe;
    }
}
