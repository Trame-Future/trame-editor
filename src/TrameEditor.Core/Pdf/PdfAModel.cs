namespace TrameEditor.Core.Pdf;

/// <summary>Livello PDF/A prodotto. Ci fermiamo alla parte 2, quella ammessa
/// dalle Linee Guida AgID e la più adatta all'archiviazione: <b>2b</b> garantisce
/// la resa visiva nel tempo, <b>2u</b> garantisce in più che tutto il testo sia
/// estraibile (mappatura Unicode) — quello che un archivio vuole davvero.</summary>
public enum PdfALevel
{
    A2b,
    A2u,
}

public enum PdfAConversionMethod
{
    /// <summary>Il documento è stato riscritto conservando il testo vettoriale.</summary>
    Fedele,

    /// <summary>Le pagine sono state rasterizzate (immagine + eventuale layer OCR):
    /// conformità garantita, testo originale perduto.</summary>
    Raster,
}

/// <summary>Quanto un problema pesa sulla conversione.</summary>
public enum PdfAIssueSeverity
{
    /// <summary>Impedisce la conversione fedele: resta solo la via raster.</summary>
    Bloccante,

    /// <summary>Non impedisce la conformità di base, ma fa scendere da 2u a 2b
    /// (una parte del testo non è estraibile in modo affidabile).</summary>
    Declassa,

    /// <summary>Lo sistemiamo noi durante la conversione: va comunque detto,
    /// perché il file prodotto sarà diverso dall'originale.</summary>
    Corretto,
}

public sealed record PdfAIssue(PdfAIssueSeverity Severity, string Description, string? Where = null)
{
    public override string ToString() =>
        Where is null ? Description : $"{Description} ({Where})";
}

/// <summary>
/// Esito dell'esame preventivo di un PDF rispetto ai vincoli PDF/A-2.
/// È anche il rapporto di verifica che rieseguiamo <b>sul file prodotto</b>.
/// </summary>
public sealed record PdfAAnalysisReport(
    IReadOnlyList<PdfAIssue> Issues,
    IReadOnlyList<string> NonEmbeddedFonts,
    IReadOnlyList<string> FontsWithoutUnicode,
    int PageCount)
{
    /// <summary>Vero se la conversione fedele è possibile: nessun ostacolo
    /// che non sappiamo togliere senza rasterizzare.</summary>
    public bool CanConvertFaithfully =>
        Issues.All(i => i.Severity != PdfAIssueSeverity.Bloccante);

    /// <summary>Il livello migliore raggiungibile per via fedele.</summary>
    public PdfALevel BestLevel =>
        Issues.Any(i => i.Severity == PdfAIssueSeverity.Declassa) ? PdfALevel.A2b : PdfALevel.A2u;

    public IReadOnlyList<PdfAIssue> Blocking =>
        [.. Issues.Where(i => i.Severity == PdfAIssueSeverity.Bloccante)];
}

public sealed record PdfAConversionResult(
    PdfAConversionMethod Method,
    PdfALevel Level,
    IReadOnlyList<string> Changes,
    PdfAAnalysisReport Verification)
{
    /// <summary>Vero se la ri-verifica del file prodotto non ha trovato residui:
    /// è la nostra verifica interna, <b>non</b> una validazione formale.</summary>
    public bool VerificationClean =>
        Verification.NonEmbeddedFonts.Count == 0 &&
        Verification.Issues.All(i => i.Severity == PdfAIssueSeverity.Corretto);
}

/// <summary>Conversione non possibile per un motivo che va detto all'utente.</summary>
public sealed class PdfAConversionException(string message) : Exception(message);
