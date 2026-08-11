namespace TrameEditor.Core.Ui;

/// <summary>Da quale famiglia di caratteri arriva l'icona di un comando.</summary>
public enum GlyphFont
{
    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets (codice esadecimale).</summary>
    Fluent,
    /// <summary>Un carattere normale (frecce, simboli) reso col font di interfaccia.</summary>
    Text,
    /// <summary>Segoe UI Emoji.</summary>
    Emoji,
}

/// <summary>Un comando dell'applicazione così come lo vede l'utente.</summary>
/// <remarks>
/// Il catalogo è la fonte unica: il <b>menu classico</b> e la <b>barra
/// multifunzione</b> sono due modi di mostrare le stesse voci. Qui c'è solo
/// la descrizione (etichette, icona, gruppo logico); il collegamento al
/// comando vero vive nell'applicazione WPF.
/// </remarks>
public sealed record UiCommand
{
    /// <summary>Identificatore stabile: finisce nel file di personalizzazione,
    /// quindi non va mai cambiato una volta rilasciato.</summary>
    public required string Id { get; init; }

    /// <summary>Etichetta breve, per il pulsante nella barra multifunzione.</summary>
    public required string Label { get; init; }

    /// <summary>Etichetta estesa per il menu classico (con i puntini di
    /// sospensione quando la voce apre una finestra).</summary>
    public required string MenuLabel { get; init; }

    /// <summary>Menu classico in cui compare la voce: è anche la classe
    /// logica con cui i comandi sono raggruppati nella personalizzazione.</summary>
    public required string Menu { get; init; }

    /// <summary>Spiegazione mostrata come suggerimento.</summary>
    public required string Description { get; init; }

    public string Glyph { get; init; } = string.Empty;

    public GlyphFont GlyphFont { get; init; } = GlyphFont.Fluent;

    /// <summary>Vero per le voci che accendono/spengono qualcosa
    /// (casella di spunta nel menu, interruttore nella barra).</summary>
    public bool IsToggle { get; init; }

    /// <summary>Scorciatoia da tastiera, se c'è (solo testo da mostrare).</summary>
    public string Shortcut { get; init; } = string.Empty;

    /// <summary>Vero se nel menu classico la voce apre un nuovo blocco
    /// (viene disegnata una riga di separazione sopra).</summary>
    public bool SeparatorBefore { get; init; }
}
