namespace TrameEditor.Cli;

/// <summary>
/// Controlli sui percorsi, fatti prima di aprire qualsiasi cosa. La destinazione non
/// si sovrascrive mai da sola: la regola "mai perdere lavoro dell'utente" vale a maggior
/// ragione qui, dove a digitare il percorso può essere un programma.
/// </summary>
public static class Paths
{
    public static string ExistingFile(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new UsageException($"Il file non esiste: {full}");
        return full;
    }

    public static string Target(string path, bool overwrite)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (File.Exists(full) && !overwrite)
            throw new UsageException(
                $"Il file di destinazione esiste già: {full}. Aggiungi --sovrascrivi per sostituirlo.");

        var directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            throw new UsageException($"La cartella di destinazione non esiste: {directory}");
        return full;
    }

    public static string Folder(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        return full;
    }
}
