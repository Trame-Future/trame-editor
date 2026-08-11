namespace TrameEditor.Core.Ui;

/// <summary>
/// L'elenco completo delle funzioni di TrameEditor, raggruppate per classi
/// omogenee (i menu classici: File, Modifica, Visualizza, Pagine, Converti,
/// Sicurezza, Strumenti, ?).
/// </summary>
/// <remarks>
/// <para>Regola che tiene in piedi tutto il resto: <b>ogni funzione sta in
/// esattamente un menu</b>. Il menu classico viene generato da qui, quindi
/// non può esistere una funzione irraggiungibile — nemmeno dopo che l'utente
/// ha svuotato la barra multifunzione personalizzandola.</para>
/// <para>Gli identificatori finiscono nel file di personalizzazione
/// dell'utente: si possono aggiungere voci, non rinominare quelle esistenti.</para>
/// <para>I glifi <c>\uEnnn</c> vengono da Segoe Fluent Icons / Segoe MDL2 Assets.</para>
/// </remarks>
public static class CommandCatalog
{
    public const string MenuFile = "File";
    public const string MenuEdit = "Modifica";
    public const string MenuView = "Visualizza";
    public const string MenuPages = "Pagine";
    public const string MenuConvert = "Converti";
    public const string MenuSecurity = "Sicurezza";
    public const string MenuTools = "Strumenti";
    public const string MenuHelp = "?";

    /// <summary>I menu nell'ordine in cui compaiono nella barra dei menu.</summary>
    public static IReadOnlyList<string> Menus { get; } = new[]
    {
        MenuFile, MenuEdit, MenuView, MenuPages, MenuConvert, MenuSecurity, MenuTools, MenuHelp,
    };

    /// <summary>
    /// L'intestazione del menu con la lettera di scelta rapida: Alt+F apre
    /// File, Alt+M Modifica… Il trattino basso è la convenzione WPF.
    /// </summary>
    public static string MenuHeader(string menu) => menu switch
    {
        MenuFile => "_File",
        MenuEdit => "_Modifica",
        MenuView => "_Visualizza",
        MenuPages => "_Pagine",
        MenuConvert => "_Converti",
        MenuSecurity => "_Sicurezza",
        MenuTools => "S_trumenti",
        MenuHelp => "_?",
        _ => menu,
    };

    public static IReadOnlyList<UiCommand> All { get; } = new UiCommand[]
    {
        // ── File ────────────────────────────────────────────────────────────
        new()
        {
            Id = "new", Label = "Nuovo", MenuLabel = "Nuovo", Menu = MenuFile,
            Description = "Nuovo documento di testo", Glyph = "\uE7C3", Shortcut = "Ctrl+N",
        },
        new()
        {
            Id = "open", Label = "Apri", MenuLabel = "Apri…", Menu = MenuFile,
            Description = "Apri un file PDF, di testo o Markdown", Glyph = "\uE8E5", Shortcut = "Ctrl+O",
        },
        new()
        {
            Id = "save", SeparatorBefore = true, Label = "Salva", MenuLabel = "Salva", Menu = MenuFile,
            Description = "Salva il documento aperto", Glyph = "\uE74E", Shortcut = "Ctrl+S",
        },
        new()
        {
            Id = "save-as", Label = "Salva con nome", MenuLabel = "Salva con nome…", Menu = MenuFile,
            Description = "Salva una copia con un altro nome", Glyph = "\uE792",
            Shortcut = "Ctrl+Maiusc+S",
        },
        new()
        {
            Id = "print", SeparatorBefore = true, Label = "Stampa", MenuLabel = "Stampa…", Menu = MenuFile,
            Description = "Stampa il documento aperto", Glyph = "\uE749", Shortcut = "Ctrl+P",
        },
        new()
        {
            Id = "close-tab", SeparatorBefore = true, Label = "Chiudi scheda", MenuLabel = "Chiudi scheda", Menu = MenuFile,
            Description = "Chiudi il documento aperto", Glyph = "\uE711", Shortcut = "Ctrl+W",
        },

        // ── Modifica ────────────────────────────────────────────────────────
        new()
        {
            Id = "undo", Label = "Annulla", MenuLabel = "Annulla", Menu = MenuEdit,
            Description = "Annulla l'ultima modifica", Glyph = "\uE7A7", Shortcut = "Ctrl+Z",
        },
        new()
        {
            Id = "redo", Label = "Ripeti", MenuLabel = "Ripeti", Menu = MenuEdit,
            Description = "Rifai la modifica annullata", Glyph = "\uE7A6", Shortcut = "Ctrl+Y",
        },
        new()
        {
            Id = "find", Label = "Trova", MenuLabel = "Trova nel documento", Menu = MenuEdit,
            Description = "Cerca del testo nel documento aperto", Glyph = "\uE721", Shortcut = "Ctrl+F",
        },
        new()
        {
            Id = "search-folder", SeparatorBefore = true, Label = "Cerca in cartella", MenuLabel = "Cerca in una cartella di PDF…",
            Menu = MenuEdit, Glyph = "\uE721",
            Description = "Cerca una parola o un codice dentro tutti i PDF di una cartella",
        },

        // ── Visualizza ──────────────────────────────────────────────────────
        new()
        {
            Id = "word-wrap", Label = "A capo automatico", MenuLabel = "A capo automatico", Menu = MenuView,
            Description = "Manda a capo le righe lunghe invece di scorrere in orizzontale",
            IsToggle = true,
        },
        new()
        {
            Id = "line-numbers", Label = "Numeri di riga", MenuLabel = "Numeri di riga", Menu = MenuView,
            Description = "Mostra i numeri di riga nell'editor", IsToggle = true,
        },
        new()
        {
            Id = "markdown-preview", Label = "Anteprima Markdown", MenuLabel = "Anteprima Markdown",
            Menu = MenuView, Description = "Mostra l'anteprima accanto al testo Markdown", IsToggle = true,
        },
        new()
        {
            Id = "zoom-in", SeparatorBefore = true, Label = "Ingrandisci", MenuLabel = "Ingrandisci", Menu = MenuView,
            Description = "Ingrandisci la pagina del PDF", Glyph = "\uE8A3",
        },
        new()
        {
            Id = "zoom-out", Label = "Riduci", MenuLabel = "Riduci", Menu = MenuView,
            Description = "Rimpicciolisci la pagina del PDF", Glyph = "\uE71F",
        },
        new()
        {
            Id = "customize-ribbon", SeparatorBefore = true, Label = "Personalizza barra",
            MenuLabel = "Personalizza la barra multifunzione…", Menu = MenuView,
            Glyph = "☰", GlyphFont = GlyphFont.Text,
            Description = "Scegli quali pulsanti mettere nella barra multifunzione, e dove",
        },
        new()
        {
            Id = "reset-ribbon", Label = "Ripristina barra",
            MenuLabel = "Ripristina la barra predefinita", Menu = MenuView,
            Glyph = "↺", GlyphFont = GlyphFont.Text,
            Description = "Rimette la barra multifunzione come era all'installazione",
        },

        // ── Pagine ──────────────────────────────────────────────────────────
        new()
        {
            Id = "rotate-left", Label = "Ruota a sinistra", MenuLabel = "Ruota a sinistra", Menu = MenuPages,
            Description = "Ruota a sinistra le pagine selezionate", Glyph = "⟲", GlyphFont = GlyphFont.Text,
        },
        new()
        {
            Id = "rotate-right", Label = "Ruota a destra", MenuLabel = "Ruota a destra", Menu = MenuPages,
            Description = "Ruota a destra le pagine selezionate", Glyph = "⟳", GlyphFont = GlyphFont.Text,
        },
        new()
        {
            Id = "page-up", Label = "Sposta su", MenuLabel = "Sposta la pagina in su", Menu = MenuPages,
            Description = "Sposta in su le pagine selezionate", Glyph = "\uE74A",
        },
        new()
        {
            Id = "page-down", Label = "Sposta giù", MenuLabel = "Sposta la pagina in giù", Menu = MenuPages,
            Description = "Sposta in giù le pagine selezionate", Glyph = "\uE74B",
        },
        new()
        {
            Id = "page-delete", Label = "Elimina pagine", MenuLabel = "Elimina le pagine selezionate",
            Menu = MenuPages, Description = "Elimina dal PDF le pagine selezionate", Glyph = "\uE74D",
        },
        new()
        {
            Id = "page-extract", Label = "Estrai pagine", MenuLabel = "Estrai le pagine selezionate…",
            Menu = MenuPages, Description = "Salva le pagine selezionate in un PDF nuovo", Glyph = "\uE8C8",
        },
        new()
        {
            Id = "merge", SeparatorBefore = true, Label = "Unisci PDF", MenuLabel = "Unisci più PDF…", Menu = MenuPages,
            Description = "Unisci più PDF in un unico file", Glyph = "\uEA37",
        },
        new()
        {
            Id = "images-to-pdf", Label = "Immagini in PDF", MenuLabel = "Immagini in PDF…", Menu = MenuPages,
            Description = "Unisce una o più immagini in un PDF", Glyph = "\uE91B",
        },

        // ── Converti ────────────────────────────────────────────────────────
        new()
        {
            Id = "export-pdf", Label = "Esporta PDF", MenuLabel = "Esporta in PDF…", Menu = MenuConvert,
            Description = "Salva il documento di testo o Markdown come PDF", Glyph = "\uE8A5",
        },
        new()
        {
            Id = "pdfa", Label = "Converti in PDF/A", MenuLabel = "Converti in PDF/A (archiviazione)…",
            Menu = MenuConvert, Glyph = "\uE81C",
            Description = "Salva una copia in PDF/A-2, il formato per l'archiviazione a lungo termine",
        },
        new()
        {
            Id = "export-html", Label = "Esporta HTML", MenuLabel = "Esporta l'anteprima in HTML…",
            Menu = MenuConvert, Description = "Esporta l'anteprima Markdown come pagina HTML",
            Glyph = "\uE774",
        },
        new()
        {
            Id = "export-images", Label = "Esporta immagini", MenuLabel = "Esporta le pagine come immagini…",
            Menu = MenuConvert, Description = "Salva ogni pagina del PDF aperto come immagine PNG",
            Glyph = "\uE78A",
        },
        new()
        {
            Id = "export-text", Label = "Esporta testo", MenuLabel = "Esporta il testo…", Menu = MenuConvert,
            Description = "Estrae il testo del PDF aperto in un file .txt", Glyph = "\uE8A4",
        },
        new()
        {
            Id = "ocr", SeparatorBefore = true, Label = "Riconosci testo", MenuLabel = "Riconosci il testo (OCR)", Menu = MenuConvert,
            Description = "Riconosce il testo delle pagine scansionate (OCR offline ita+eng)",
            Glyph = "OCR", GlyphFont = GlyphFont.Text,
        },
        new()
        {
            Id = "compress", Label = "Comprimi", MenuLabel = "Comprimi il PDF…", Menu = MenuConvert,
            Description = "Salva una copia più leggera, anche puntando a una dimensione precisa",
            Glyph = "\uE73F",
        },

        // ── Sicurezza ───────────────────────────────────────────────────────
        new()
        {
            Id = "redact", Label = "Anonimizza", MenuLabel = "Anonimizza…", Menu = MenuSecurity,
            Description = "Trova e rimuove davvero i dati personali (CF, IBAN, email, telefoni, targhe) e i metadati",
            Glyph = "🛡", GlyphFont = GlyphFont.Emoji,
        },
        new()
        {
            Id = "protect", Label = "Proteggi PDF", MenuLabel = "Proteggi con password…", Menu = MenuSecurity,
            Description = "Salva una copia del PDF protetta da password (AES-256)", Glyph = "\uE72E",
        },
        new()
        {
            Id = "signatures", Label = "Firme", MenuLabel = "Firme del documento…", Menu = MenuSecurity,
            Description = "Chi ha firmato questo PDF, quando, e se il documento è ancora quello firmato",
            Glyph = "\uE8A9",
        },
        new()
        {
            Id = "profile", SeparatorBefore = true, Label = "I miei dati", MenuLabel = "I miei dati…", Menu = MenuSecurity,
            Description = "I tuoi dati per la compilazione automatica dei moduli (cifrati sul PC)",
            Glyph = "\uE77B",
        },

        // ── Strumenti ───────────────────────────────────────────────────────
        new()
        {
            Id = "compare", Label = "Confronta documenti", MenuLabel = "Confronta due documenti…",
            Menu = MenuTools, Glyph = "⇄", GlyphFont = GlyphFont.Text,
            Description = "Confronta il testo di due versioni di un documento (PDF, testo o Markdown, anche misti)",
        },
        new()
        {
            Id = "decorate", Label = "Numeri e filigrana", MenuLabel = "Numeri di pagina e filigrana…",
            Menu = MenuTools, Glyph = "\uE8A1",
            Description = "Aggiungi numeri di pagina, una filigrana o intestazioni al PDF aperto",
        },
        new()
        {
            Id = "batch", SeparatorBefore = true, Label = "Ricette", MenuLabel = "Ricette: molti file in una volta…", Menu = MenuTools,
            Glyph = "⚙", GlyphFont = GlyphFont.Text,
            Description = "Ricetta sui PDF, oppure estrazione dei documenti dai file firmati (.p7m)",
        },
        new()
        {
            Id = "settings", SeparatorBefore = true, Label = "Impostazioni", MenuLabel = "Impostazioni…", Menu = MenuTools,
            Glyph = "\uE713",
            Description = "Componenti opzionali: assistente AI (Ollama) e validatore PDF/A (veraPDF)",
        },

        // ── ? ───────────────────────────────────────────────────────────────
        new()
        {
            Id = "help", Label = "Guida", MenuLabel = "Guida di TrameEditor", Menu = MenuHelp,
            Description = "Apri la guida di TrameEditor", Glyph = "\uE897", Shortcut = "F1",
        },
        new()
        {
            // Senza glifo: nella barra multifunzione mostra il logo Trame Future.
            Id = "about", Label = "Trame Future", MenuLabel = "Informazioni su TrameEditor…",
            Menu = MenuHelp, Description = "Versione, licenza e contatti",
        },
    };

    private static readonly Dictionary<string, UiCommand> ById =
        All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Ids => ById.Keys;

    public static UiCommand? Find(string id) =>
        ById.TryGetValue(id, out var command) ? command : null;

    /// <summary>I comandi di un menu, nell'ordine di dichiarazione.</summary>
    public static IReadOnlyList<UiCommand> OfMenu(string menu) =>
        All.Where(c => c.Menu == menu).ToList();
}
