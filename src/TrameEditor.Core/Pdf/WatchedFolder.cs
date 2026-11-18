namespace TrameEditor.Core.Pdf;

/// <summary>Cosa è successo a un file passato dalla cartella sorvegliata.</summary>
public sealed record WatchedFileOutcome(
    string FileName,
    bool Success,
    string Outcome,
    string? OutputPath,
    DateTime WhenUtc);

/// <summary>Le cartelle di lavoro: dove si guarda, dove finisce il fatto, dove il fallito.</summary>
public sealed record WatchedFolderLayout(string Inbox, string Done, string Failed, string LogPath)
{
    public static WatchedFolderLayout Under(string inbox)
    {
        var full = Path.GetFullPath(inbox);
        return new WatchedFolderLayout(
            full,
            Path.Combine(full, "fatti"),
            Path.Combine(full, "non riusciti"),
            Path.Combine(full, "registro.csv"));
    }

    public void Create()
    {
        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Done);
        Directory.CreateDirectory(Failed);
    }
}

/// <summary>
/// Lavora da sola i PDF che compaiono in una cartella, applicando una Ricetta, e sposta
/// l'originale in «fatti» o «non riusciti» tenendo un registro.
/// <para>
/// <b>Non è un servizio di Windows</b>: gira dentro l'applicazione e guarda solo finché
/// l'applicazione è aperta. Farlo credere continuo sarebbe la bugia più comoda e più
/// dannosa — chi lascia file in una cartella la sera si aspetta di ritrovarli lavorati.
/// </para>
/// <para>
/// Il problema vero di questa funzione non è lavorare i file, ma capire <b>quando</b> un file
/// è finito di arrivare: una copia da rete o uno scanner creano il file e lo riempiono dopo.
/// Si aspetta che la dimensione resti ferma e che il file si lasci aprire in scrittura.
/// </para>
/// </summary>
public sealed class WatchedFolder(WatchedFolderLayout layout, Func<string, string, WatchedFileOutcome> process)
{
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quanto deve restare ferma la dimensione prima di considerare il file arrivato.</summary>
    public TimeSpan StableFor { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>I PDF pronti per essere lavorati: fermi e non bloccati da chi li sta scrivendo.</summary>
    public IReadOnlyList<string> ReadyFiles(Func<string, long>? sizeOf = null)
    {
        if (!Directory.Exists(layout.Inbox))
            return [];

        sizeOf ??= path => new FileInfo(path).Length;
        var ready = new List<string>();

        foreach (var path in Directory.GetFiles(layout.Inbox, "*.pdf", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (_inProgress.Contains(path))
                continue;
            try
            {
                var first = sizeOf(path);
                if (first == 0)
                    continue; // file appena creato e ancora vuoto

                Thread.Sleep(StableFor);
                if (sizeOf(path) != first)
                    continue; // sta ancora crescendo: si riguarda al giro dopo

                // Chi scrive tiene il file aperto: se si apre in scrittura, ha finito.
                using var test = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                ready.Add(path);
            }
            catch (IOException)
            {
                // ancora in uso: non è un errore, è un «non adesso»
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return ready;
    }

    /// <summary>
    /// Lavora i file pronti. Restituisce un esito per file; l'originale viene spostato in
    /// «fatti» o «non riusciti» e il registro aggiornato.
    /// </summary>
    public IReadOnlyList<WatchedFileOutcome> ProcessReady(Func<string, long>? sizeOf = null)
    {
        layout.Create();
        var outcomes = new List<WatchedFileOutcome>();

        foreach (var path in ReadyFiles(sizeOf))
        {
            _inProgress.Add(path);
            WatchedFileOutcome outcome;
            try
            {
                outcome = process(path, layout.Done);
            }
            catch (Exception ex)
            {
                outcome = new WatchedFileOutcome(Path.GetFileName(path), false,
                    $"non riuscito: {ex.Message}", null, DateTime.UtcNow);
            }
            finally
            {
                _inProgress.Remove(path);
            }

            var destination = outcome.Success ? layout.Done : layout.Failed;
            try
            {
                MoveWithoutOverwriting(path, destination);
            }
            catch (Exception ex)
            {
                // Il file lavorato c'è già; non poter spostare l'originale è un guaio minore,
                // ma tacerlo vorrebbe dire rilavorarlo per sempre a ogni giro.
                outcome = outcome with
                {
                    Outcome = outcome.Outcome + $" (l'originale non è stato spostato: {ex.Message})",
                };
            }

            outcomes.Add(outcome);
            Append(outcome);
        }
        return outcomes;
    }

    /// <summary>Sposta senza sovrascrivere: se il nome è occupato si aggiunge un numero.
    /// Lo stesso file può tornare domani, e la copia di ieri non va persa.</summary>
    internal static string MoveWithoutOverwriting(string path, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var target = Path.Combine(destinationFolder, name + extension);

        var attempt = 2;
        while (File.Exists(target))
            target = Path.Combine(destinationFolder, $"{name} ({attempt++}){extension}");

        File.Move(path, target);
        return target;
    }

    /// <summary>Il registro: una riga per file, in CSV con il punto e virgola come il resto
    /// del programma, così si apre in Excel senza fatica.</summary>
    private void Append(WatchedFileOutcome outcome)
    {
        try
        {
            var isNew = !File.Exists(layout.LogPath);
            using var writer = new StreamWriter(layout.LogPath, append: true,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew));
            if (isNew)
                writer.WriteLine("Quando;File;Esito;Dettaglio;Prodotto");

            writer.WriteLine(string.Join(';', new[]
            {
                outcome.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Escape(outcome.FileName),
                outcome.Success ? "fatto" : "non riuscito",
                Escape(outcome.Outcome),
                Escape(outcome.OutputPath ?? string.Empty),
            }));
        }
        catch (IOException)
        {
            // Registro aperto in Excel proprio adesso: la lavorazione non si ferma per questo.
        }
    }

    private static string Escape(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
