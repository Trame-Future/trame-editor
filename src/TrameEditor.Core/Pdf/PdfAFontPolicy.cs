using iText.Kernel.Pdf;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Come trattiamo i font di un PDF rispetto al vincolo PDF/A: <b>tutti i font
/// devono essere incorporati</b>, anche quelli "di sistema" come Helvetica o Arial
/// che il PDF si limita a nominare.
/// <para>
/// Un font non incorporato lo possiamo salvare solo se sul computer esiste lo
/// stesso font (o il suo equivalente metricamente compatibile: Helvetica→Arial,
/// Times→Times New Roman, Courier→Courier New) <i>e</i> se la codifica dei
/// caratteri è WinAnsi, l'unica per cui la sostituzione è carattere-per-carattere
/// senza ambiguità. Fuori da questi casi non inventiamo: lo dichiariamo bloccante.
/// </para>
/// </summary>
internal static class PdfAFontPolicy
{
    internal sealed record FontUsage(
        string Name,
        bool IsEmbedded,
        bool HasUnicodeMapping,
        string? SubstitutePath);

    internal static FontUsage Inspect(PdfDictionary font, IReadOnlyCollection<int> charactersUsed)
    {
        var name = DisplayName(font);
        var embedded = IsEmbedded(font);
        var unicode = HasUnicodeMapping(font);
        var substitute = embedded ? null : FindSubstitute(font, charactersUsed);
        return new FontUsage(name, embedded, unicode, substitute);
    }

    /// <summary>Nome leggibile del font, senza il prefisso di subset ("ABCDEF+Arial").</summary>
    internal static string DisplayName(PdfDictionary font)
    {
        var baseFont = font.GetAsName(PdfName.BaseFont)?.GetValue() ?? "(senza nome)";
        if (baseFont.Length > 7 && baseFont[6] == '+' && baseFont.Take(6).All(char.IsAsciiLetterUpper))
            return baseFont[7..];
        return baseFont;
    }

    internal static bool IsEmbedded(PdfDictionary font)
    {
        var subtype = font.GetAsName(PdfName.Subtype);

        // I font Type3 hanno i glifi come flussi dentro il documento stesso.
        if (PdfName.Type3.Equals(subtype))
            return true;

        if (PdfName.Type0.Equals(subtype))
        {
            var descendant = font.GetAsArray(PdfName.DescendantFonts)?.GetAsDictionary(0);
            return descendant is not null && HasFontProgram(descendant.GetAsDictionary(PdfName.FontDescriptor));
        }

        return HasFontProgram(font.GetAsDictionary(PdfName.FontDescriptor));
    }

    private static bool HasFontProgram(PdfDictionary? descriptor) =>
        descriptor is not null &&
        (descriptor.ContainsKey(PdfName.FontFile) ||
         descriptor.ContainsKey(PdfName.FontFile2) ||
         descriptor.ContainsKey(PdfName.FontFile3));

    /// <summary>
    /// Il testo scritto con questo font è estraibile in modo affidabile?
    /// È la differenza fra PDF/A-2b e PDF/A-2u.
    /// </summary>
    internal static bool HasUnicodeMapping(PdfDictionary font)
    {
        if (font.ContainsKey(PdfName.ToUnicode))
            return true;

        // Senza ToUnicode restano affidabili solo le codifiche standard a un byte.
        var encoding = font.Get(PdfName.Encoding);
        return encoding switch
        {
            PdfName name => IsStandardEncodingName(name),
            PdfDictionary dictionary =>
                dictionary.GetAsName(PdfName.BaseEncoding) is { } baseEncoding &&
                IsStandardEncodingName(baseEncoding) &&
                !dictionary.ContainsKey(PdfName.Differences),
            _ => false,
        };
    }

    private static bool IsStandardEncodingName(PdfName name) =>
        PdfName.WinAnsiEncoding.Equals(name) || PdfName.MacRomanEncoding.Equals(name);

    /// <summary>
    /// Percorso del font di sistema con cui possiamo incorporare questo font non
    /// incorporato, oppure null se non è sostituibile in sicurezza.
    /// </summary>
    /// <param name="charactersUsed">I caratteri scritti con questo font nel
    /// documento: la compatibilità va verificata solo su quelli.</param>
    internal static string? FindSubstitute(PdfDictionary font, IReadOnlyCollection<int> charactersUsed)
    {
        // Solo font semplici: per i Type0/CID la corrispondenza codice→glifo
        // dipende dal CMap del font originale, che non abbiamo.
        var subtype = font.GetAsName(PdfName.Subtype);
        if (!PdfName.Type1.Equals(subtype) && !PdfName.TrueType.Equals(subtype))
            return null;

        // Solo WinAnsi: è l'unica codifica per cui il rimpiazzo è
        // carattere-per-carattere senza reinterpretare il content stream.
        if (font.Get(PdfName.Encoding) is not PdfName encoding ||
            !PdfName.WinAnsiEncoding.Equals(encoding))
            return null;

        var candidate = PdfTextReplacer.FindSystemFontFor(DisplayName(font));
        if (candidate is null)
            return null;

        // Non basta che il font esista: sui caratteri usati deve avere le stesse
        // metriche, altrimenti il testo si sposterebbe. La risposta deve essere la
        // stessa che darà la conversione, così il rapporto all'utente non mente.
        return PdfAFontEmbedder.CanEmbed(font, candidate, charactersUsed, out _) ? candidate : null;
    }
}
