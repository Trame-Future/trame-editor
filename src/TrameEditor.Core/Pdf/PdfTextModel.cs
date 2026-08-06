namespace TrameEditor.Core.Pdf;

/// <summary>
/// Una riga (o segmento di riga) di testo di una pagina PDF, in coordinate
/// utente PDF (origine in basso a sinistra, unità in punti).
/// </summary>
public sealed record PdfTextLine(
    int PageNumber,
    string Text,
    double Left,
    double Bottom,
    double Width,
    double Height,
    double BaselineX,
    double BaselineY,
    string FontName,
    double FontSizePt,
    double ColorR,
    double ColorG,
    double ColorB,
    bool IsEditable,
    string? NotEditableReason)
{
    public double Top => Bottom + Height;
}

public enum PdfFontStrategy
{
    /// <summary>Il font incorporato nel PDF contiene tutti i glifi del nuovo testo.</summary>
    ReuseEmbedded,

    /// <summary>Font equivalente trovato tra quelli installati nel sistema.</summary>
    SystemFont,

    /// <summary>Nessuno dei due: font standard metricamente simile, dichiarato all'utente.</summary>
    Substitute,
}

public sealed record PdfFontPlan(
    PdfFontStrategy Strategy,
    string Description,
    string? SystemFontPath,
    string? StandardFontName);

/// <summary>Esito di una sostituzione multi-riga: le righe saltate (operatori non
/// trovati nel flusso pagina) vanno sempre mostrate all'utente.</summary>
public sealed record PdfReplaceManyResult(int LinesReplaced, IReadOnlyList<PdfTextLine> SkippedLines);

/// <summary>Modifica non applicabile: il messaggio spiega il motivo all'utente.</summary>
public sealed class PdfTextEditException : Exception
{
    public PdfTextEditException(string message) : base(message)
    {
    }
}
