namespace TrameEditor.Core.Pdf;

/// <summary>Quanto pesa un problema di accessibilità, e chi lo può risolvere.</summary>
public enum PdfUaSeverity
{
    /// <summary>Impedisce la conformità e <b>non lo possiamo mettere a posto noi</b>:
    /// richiede di marcare il contenuto (titoli, tabelle, testi alternativi), cioè
    /// decisioni che solo chi conosce il documento può prendere.</summary>
    Bloccante,

    /// <summary>Impedisce la conformità ma lo sistemiamo qui, senza inventare niente
    /// (lingua del documento, titolo, come il titolo viene mostrato).</summary>
    Rimediabile,

    /// <summary>Già a posto.</summary>
    Corretto,
}

public sealed record PdfUaIssue(PdfUaSeverity Severity, string Description, string? Where = null)
{
    public override string ToString() =>
        Where is null ? Description : $"{Description} ({Where})";
}

/// <summary>
/// Che cosa abbiamo trovato guardando un PDF con gli occhi dell'accessibilità.
/// </summary>
/// <param name="Language">La lingua dichiarata nel documento, se c'è.</param>
/// <param name="Title">Il titolo del documento, se c'è.</param>
public sealed record PdfUaReport(
    IReadOnlyList<PdfUaIssue> Issues,
    bool IsTagged,
    string? Language,
    string? Title,
    int PageCount)
{
    /// <summary>Vero se restano solo cose che sappiamo mettere a posto noi.</summary>
    public bool CanFixHere =>
        Issues.Any(i => i.Severity == PdfUaSeverity.Rimediabile) &&
        Issues.All(i => i.Severity != PdfUaSeverity.Bloccante);

    public IReadOnlyList<PdfUaIssue> Blocking =>
        [.. Issues.Where(i => i.Severity == PdfUaSeverity.Bloccante)];

    public IReadOnlyList<PdfUaIssue> Fixable =>
        [.. Issues.Where(i => i.Severity == PdfUaSeverity.Rimediabile)];

    /// <summary>Vero se non abbiamo trovato niente da segnalare. Attenzione:
    /// è la <b>nostra</b> verifica, non la conformità PDF/UA.</summary>
    public bool NothingFound =>
        Issues.All(i => i.Severity == PdfUaSeverity.Corretto);
}
