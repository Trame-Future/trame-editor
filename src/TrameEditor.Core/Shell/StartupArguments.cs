namespace TrameEditor.Core.Shell;

/// <summary>Che cosa deve fare TrameEditor appena aperto.</summary>
public enum StartupVerb
{
    /// <summary>Apre e basta i file indicati (comportamento normale).</summary>
    Open,
    /// <summary>Apre il PDF e avvia subito la conversione in PDF/A.</summary>
    ConvertToPdfA,
    /// <summary>Apre il PDF e avvia subito Anonimizza.</summary>
    Redact,
    /// <summary>Apre la ricerca nei PDF della cartella indicata.</summary>
    SearchFolder,
    /// <summary>Apre le Ricette sull'estrazione dai file firmati della cartella indicata.</summary>
    ExtractSigned,
}

public sealed record StartupRequest(StartupVerb Verb, IReadOnlyList<string> Paths)
{
    public string? FirstPath => Paths.Count > 0 ? Paths[0] : null;
}

/// <summary>
/// Legge la riga di comando con cui l'applicazione è stata avviata.
/// </summary>
/// <remarks>
/// Serve al menu contestuale di Esplora risorse: la voce "Converti in PDF/A"
/// lancia <c>TrameEditor.exe --pdfa "C:\…\documento.pdf"</c>. Qui c'è solo la
/// lettura degli argomenti — nessun accesso al disco — così è verificabile.
/// </remarks>
public static class StartupArguments
{
    private static readonly Dictionary<string, StartupVerb> Switches =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["--pdfa"] = StartupVerb.ConvertToPdfA,
            ["--anonimizza"] = StartupVerb.Redact,
            ["--cerca"] = StartupVerb.SearchFolder,
            ["--estrai-firmati"] = StartupVerb.ExtractSigned,
            ["--apri"] = StartupVerb.Open,
        };

    /// <summary>L'opzione che chiede una certa azione, per costruire i comandi
    /// del menu contestuale.</summary>
    public static string SwitchFor(StartupVerb verb) =>
        verb == StartupVerb.Open
            ? string.Empty
            : Switches.First(pair => pair.Value == verb).Key;

    /// <summary>
    /// Gli argomenti <b>senza</b> il nome dell'eseguibile. Un'opzione che non
    /// conosciamo viene ignorata invece di far fallire l'avvio: l'utente
    /// vuole comunque aprire i suoi file.
    /// </summary>
    public static StartupRequest Parse(IEnumerable<string>? args)
    {
        var verb = StartupVerb.Open;
        var paths = new List<string>();

        foreach (var argument in args ?? [])
        {
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (Switches.TryGetValue(argument.Trim(), out var found))
                    verb = found;
                continue;
            }

            paths.Add(argument.Trim().Trim('"'));
        }

        return new StartupRequest(verb, paths);
    }
}
