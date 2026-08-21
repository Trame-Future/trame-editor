namespace TrameEditor.Cli;

/// <summary>Errore d'uso: argomenti sbagliati o mancanti. Il messaggio è quello che
/// finisce sotto gli occhi di chi ha digitato il comando — o dell'agente che l'ha
/// composto, che sbaglia i percorsi più spesso di una persona.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Lettura della riga di comando: <c>trameeditor &lt;comando&gt; [posizionali] [--opzione valore]</c>.
/// Solo lettura di stringhe, nessun accesso al disco: così è verificabile per intero.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    private CommandLine(string verb) => Verb = verb;

    /// <summary>Il comando richiesto, in minuscolo. Vuoto se non è stato indicato.</summary>
    public string Verb { get; }

    public IReadOnlyList<string> Positional => _positional;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new CommandLine(string.Empty);

        var line = new CommandLine(args[0].ToLowerInvariant());
        for (var i = 1; i < args.Count; i++)
        {
            var argument = args[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                line._positional.Add(argument);
                continue;
            }

            var name = argument[2..];
            // Si accettano entrambe le forme: "--pagina 2" e "--pagina=2".
            var separator = name.IndexOf('=');
            if (separator >= 0)
            {
                line._options[name[..separator]] = name[(separator + 1)..];
                continue;
            }

            // Un'opzione seguita da qualcosa che non è un'altra opzione ne è il valore;
            // altrimenti vale come interruttore acceso.
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            line._options[name] = hasValue ? args[++i] : null;
        }
        return line;
    }

    /// <summary>
    /// Costruisce un comando senza passare da una riga di testo: lo usa il server MCP, che
    /// riceve già i parametri separati. Un'opzione con valore <c>null</c> è un interruttore
    /// acceso; un'opzione assente dal dizionario non è stata chiesta.
    /// </summary>
    public static CommandLine Of(string verb, IEnumerable<string>? positional = null,
        IEnumerable<KeyValuePair<string, string?>>? options = null)
    {
        var line = new CommandLine(verb.ToLowerInvariant());
        line._positional.AddRange(positional ?? []);
        foreach (var option in options ?? [])
            line._options[option.Key] = option.Value;
        return line;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public string Required(string name) =>
        Value(name) ?? throw new UsageException($"Manca l'opzione --{name}.");

    public int RequiredInt(string name)
    {
        var raw = Required(name);
        return int.TryParse(raw, out var value)
            ? value
            : throw new UsageException($"L'opzione --{name} vuole un numero, non «{raw}».");
    }

    public int IntOr(string name, int fallback)
    {
        if (!Has(name))
            return fallback;
        return RequiredInt(name);
    }

    /// <summary>Un argomento posizionale obbligatorio, con il nome che compare
    /// nell'errore quando manca.</summary>
    public string At(int index, string name) =>
        index < _positional.Count
            ? _positional[index]
            : throw new UsageException($"Manca l'argomento «{name}».");
}
