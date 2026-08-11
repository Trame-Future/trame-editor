namespace TrameEditor.Core.Ui;

/// <summary>Un pulsante nella barra multifunzione.</summary>
public sealed class RibbonItem
{
    public string CommandId { get; set; } = string.Empty;

    /// <summary>Pulsante grande (icona sopra, etichetta sotto) invece che piccolo.</summary>
    public bool Large { get; set; }

    public RibbonItem() { }

    public RibbonItem(string commandId, bool large = false)
    {
        CommandId = commandId;
        Large = large;
    }

    public RibbonItem Clone() => new(CommandId, Large);
}

/// <summary>Un riquadro della barra multifunzione (es. "Documento").</summary>
public sealed class RibbonGroup
{
    public string Title { get; set; } = string.Empty;
    public List<RibbonItem> Items { get; set; } = new();

    public RibbonGroup() { }

    public RibbonGroup(string title, params RibbonItem[] items)
    {
        Title = title;
        Items = items.ToList();
    }

    public RibbonGroup Clone() => new()
    {
        Title = Title,
        Items = Items.Select(i => i.Clone()).ToList(),
    };
}

/// <summary>Una scheda della barra multifunzione (es. "Home").</summary>
public sealed class RibbonTab
{
    public string Title { get; set; } = string.Empty;
    public List<RibbonGroup> Groups { get; set; } = new();

    public RibbonTab() { }

    public RibbonTab(string title, params RibbonGroup[] groups)
    {
        Title = title;
        Groups = groups.ToList();
    }

    public RibbonTab Clone() => new()
    {
        Title = Title,
        Groups = Groups.Select(g => g.Clone()).ToList(),
    };
}

/// <summary>
/// La disposizione della barra multifunzione: schede, riquadri, pulsanti.
/// È l'unica cosa che l'utente personalizza; il menu classico resta completo
/// in ogni caso, così nessuna funzione può diventare irraggiungibile.
/// </summary>
public sealed class RibbonLayout
{
    public List<RibbonTab> Tabs { get; set; } = new();

    public IEnumerable<string> CommandIds =>
        Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items).Select(i => i.CommandId);

    public RibbonLayout Clone() => new() { Tabs = Tabs.Select(t => t.Clone()).ToList() };

    /// <summary>La barra così come nasce all'installazione.</summary>
    public static RibbonLayout Default() => new()
    {
        Tabs =
        {
            new RibbonTab("Home",
                new RibbonGroup("Documento",
                    new RibbonItem("new", large: true),
                    new RibbonItem("open", large: true),
                    new RibbonItem("save", large: true),
                    new RibbonItem("save-as"),
                    new RibbonItem("close-tab"),
                    new RibbonItem("print")),
                new RibbonGroup("Modifica",
                    new RibbonItem("undo"),
                    new RibbonItem("redo"),
                    new RibbonItem("find")),
                new RibbonGroup("Visualizza",
                    new RibbonItem("word-wrap"),
                    new RibbonItem("line-numbers"),
                    new RibbonItem("markdown-preview")),
                new RibbonGroup("Informazioni",
                    new RibbonItem("help", large: true),
                    new RibbonItem("about", large: true))),

            // Gli strumenti stanno in una scheda loro: in una sola riga
            // finivano fuori dalla finestra, e un pulsante che non si vede
            // è un pulsante che non esiste.
            new RibbonTab("Strumenti",
                new RibbonGroup("Converti",
                    new RibbonItem("export-pdf", large: true),
                    new RibbonItem("export-html", large: true),
                    new RibbonItem("pdfa", large: true),
                    new RibbonItem("images-to-pdf"),
                    new RibbonItem("export-images"),
                    new RibbonItem("export-text")),
                new RibbonGroup("Documenti",
                    new RibbonItem("merge", large: true),
                    new RibbonItem("compare"),
                    new RibbonItem("decorate"),
                    new RibbonItem("search-folder"),
                    new RibbonItem("batch")),
                new RibbonGroup("Sicurezza",
                    new RibbonItem("signatures"),
                    new RibbonItem("protect"),
                    new RibbonItem("profile")),
                new RibbonGroup("Impostazioni",
                    new RibbonItem("settings", large: true))),
        },
    };

    /// <summary>
    /// Ripulisce una disposizione arrivata dal disco: butta i comandi che non
    /// esistono più (una versione vecchia del file, o una modifica a mano),
    /// i riquadri e le schede rimasti vuoti. Se non resta niente di usabile
    /// torna quella predefinita: una barra vuota sarebbe solo un guasto.
    /// </summary>
    public RibbonLayout Sanitize(IEnumerable<string>? knownIds = null)
    {
        var known = new HashSet<string>(knownIds ?? CommandCatalog.Ids, StringComparer.Ordinal);
        var cleaned = new RibbonLayout();

        foreach (var tab in Tabs)
        {
            var keptGroups = new List<RibbonGroup>();
            foreach (var group in tab.Groups)
            {
                var items = group.Items
                    .Where(i => !string.IsNullOrWhiteSpace(i.CommandId) && known.Contains(i.CommandId))
                    .Select(i => i.Clone())
                    .ToList();
                if (items.Count > 0)
                    keptGroups.Add(new RibbonGroup { Title = group.Title, Items = items });
            }

            if (keptGroups.Count > 0)
                cleaned.Tabs.Add(new RibbonTab { Title = tab.Title, Groups = keptGroups });
        }

        return cleaned.Tabs.Count > 0 ? cleaned : Default();
    }
}
