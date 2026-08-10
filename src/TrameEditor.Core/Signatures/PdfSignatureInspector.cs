using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Signatures;

/// <summary>
/// Legge le firme digitali presenti dentro un PDF (PAdES) e dice, per ciascuna,
/// chi ha firmato, quando, e se il documento è rimasto quello firmato.
/// <para>
/// Quello che verifichiamo: che la firma corrisponda al contenuto (documento non
/// alterato), se copre tutto il file o solo una sua revisione, e le date di
/// validità del certificato. Quello che <b>non</b> verifichiamo: se il
/// certificato sia stato revocato e se l'ente emittente sia accreditato — per
/// quello serve un verificatore qualificato. La differenza va detta all'utente,
/// non nascosta dietro un bollino verde.
/// </para>
/// </summary>
public static class PdfSignatureInspector
{
    /// <summary>Le firme trovate nel documento, nell'ordine in cui sono state apposte.</summary>
    public static IReadOnlyList<PdfSignatureDetail> Inspect(string path)
    {
        var reader = new PdfReader(path);
        reader.SetUnethicalReading(true);
        using var document = new PdfDocument(reader);
        var util = new iText.Signatures.SignatureUtil(document);

        var found = new List<PdfSignatureDetail>();
        foreach (var name in util.GetSignatureNames())
            found.Add(Describe(util, name));
        return found;
    }

    public static bool HasSignatures(string path)
    {
        try
        {
            return Inspect(path).Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static PdfSignatureDetail Describe(iText.Signatures.SignatureUtil util, string name)
    {
        var signature = util.GetSignature(name);
        var declaredName = signature?.GetName();
        var reason = Blank(signature?.GetReason());
        var location = Blank(signature?.GetLocation());

        iText.Signatures.PdfPKCS7 pkcs7;
        try
        {
            pkcs7 = util.ReadSignatureData(name);
        }
        catch (Exception ex)
        {
            var unreadable = new SignerDetail("(non leggibile)", "(non leggibile)", null,
                DateTime.MinValue, DateTime.MinValue, false,
                $"firma non interpretabile: {ex.Message}");
            return new PdfSignatureDetail(name, unreadable, false, "(sconosciuto)",
                declaredName, reason, location);
        }

        bool integrity;
        string? problem = null;
        try
        {
            integrity = pkcs7.VerifySignatureIntegrityAndAuthenticity();
            if (!integrity)
                problem = "la firma non corrisponde al contenuto: il documento è stato " +
                    "modificato dopo essere stato firmato";
        }
        catch (Exception ex)
        {
            integrity = false;
            problem = $"verifica non riuscita: {ex.Message}";
        }

        var certificate = pkcs7.GetSigningCertificate();
        var signedAt = pkcs7.GetSignDate();
        var signer = new SignerDetail(
            certificate?.GetSubjectDN().ToString() ?? "(certificato assente)",
            certificate?.GetIssuerDN().ToString() ?? "(sconosciuto)",
            signedAt == DateTime.MinValue ? null : signedAt.ToUniversalTime(),
            certificate?.GetNotBefore().ToUniversalTime() ?? DateTime.MinValue,
            certificate?.GetNotAfter().ToUniversalTime() ?? DateTime.MinValue,
            integrity,
            problem);

        var coversAll = false;
        try
        {
            coversAll = util.SignatureCoversWholeDocument(name);
        }
        catch (Exception)
        {
            // resta false: lo diciamo come "copre solo una revisione"
        }

        return new PdfSignatureDetail(name, signer, coversAll,
            Blank(pkcs7.GetSignatureAlgorithmName()) ?? "(sconosciuto)",
            declaredName, reason, location);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Frase che accompagna sempre l'esito: quello che sappiamo dire e quello che
    /// non sappiamo dire. Sta in Core perché deve essere identica ovunque.
    /// </summary>
    public const string LegalDisclaimer =
        "Verifichiamo che il documento non sia stato alterato dopo la firma e le date del " +
        "certificato. NON verifichiamo se il certificato sia stato revocato né se l'ente " +
        "che l'ha emesso sia accreditato: questo non è un accertamento di validità legale. " +
        "Per quello usa un verificatore qualificato (per esempio quello dell'AgID).";

    /// <summary>Nome di file suggerito per il contenuto estratto da una busta.</summary>
    public static string SuggestExtractedName(string p7mPath) => P7mReader.SuggestedName(p7mPath);

    internal static string DescribeFile(string path) => Path.GetFileName(path);
}
