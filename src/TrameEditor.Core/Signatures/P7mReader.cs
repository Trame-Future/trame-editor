using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using Path = System.IO.Path;

namespace TrameEditor.Core.Signatures;

/// <summary>
/// Apre i file <c>.p7m</c>, le "buste" firmate digitalmente che in Italia
/// arrivano di continuo (contratti, fatture, atti) e che nessun programma
/// normale sa aprire.
/// <para>
/// Dentro la busta c'è il documento vero: lo tiriamo fuori così com'è e
/// riportiamo chi l'ha firmato. Verifichiamo che la firma corrisponda al
/// contenuto — cioè che il documento <b>non sia stato alterato</b> dopo la
/// firma — e le date del certificato. <b>Non</b> controlliamo se il certificato
/// sia stato revocato né se l'ente che l'ha emesso sia accreditato: per quello
/// serve un verificatore qualificato, e lo diciamo all'utente.
/// </para>
/// </summary>
public static class P7mReader
{
    /// <summary>Orario di firma dichiarato dal firmatario (pkcs9-at-signingTime).</summary>
    private static readonly DerObjectIdentifier SigningTimeOid = new("1.2.840.113549.1.9.5");

    public static bool IsP7m(string path) =>
        Path.GetExtension(path).Equals(".p7m", StringComparison.OrdinalIgnoreCase);

    public static P7mContent Read(string path)
    {
        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new SignatureReadException($"Impossibile leggere \"{Path.GetFileName(path)}\": {ex.Message}", ex);
        }

        return Read(raw, SuggestedName(path));
    }

    internal static P7mContent Read(byte[] raw, string suggestedFileName)
    {
        CmsSignedData signed;
        try
        {
            signed = new CmsSignedData(Decode(raw));
        }
        catch (Exception ex)
        {
            throw new SignatureReadException(
                "Il file non sembra una busta firmata (.p7m) leggibile: " +
                "potrebbe essere danneggiato o di un tipo diverso.", ex);
        }

        if (signed.SignedContent is null)
        {
            throw new SignatureReadException(
                "La busta contiene solo la firma, non il documento (firma \"detached\"): " +
                "serve anche il file originale a cui si riferisce.");
        }

        using var buffer = new MemoryStream();
        signed.SignedContent.Write(buffer);
        var data = buffer.ToArray();
        var certificates = signed.GetCertificates();
        var signers = signed.GetSignerInfos().GetSigners()
            .Select(signer => Describe(signer, certificates))
            .ToList();

        return new P7mContent(data, suggestedFileName, signers);
    }

    /// <summary>Il nome del file una volta tolta la busta: "contratto.pdf.p7m" → "contratto.pdf".</summary>
    internal static string SuggestedName(string path)
    {
        var name = Path.GetFileName(path);
        return IsP7m(path) ? Path.GetFileNameWithoutExtension(name) : name;
    }

    /// <summary>
    /// Alcuni mittenti spediscono il .p7m codificato in Base64 invece che in
    /// binario: se non è DER, si prova a decodificarlo prima di arrendersi.
    /// </summary>
    private static byte[] Decode(byte[] raw)
    {
        // 0x30 è la sequenza ASN.1: il DER comincia sempre così.
        if (raw.Length > 0 && raw[0] == 0x30)
            return raw;

        try
        {
            var text = Encoding.ASCII.GetString(raw);
            var body = text
                .Replace("-----BEGIN PKCS7-----", string.Empty)
                .Replace("-----END PKCS7-----", string.Empty)
                .Trim();
            return Convert.FromBase64String(body);
        }
        catch (Exception)
        {
            return raw; // non è Base64: si riprova come binario e sarà il CMS a protestare
        }
    }

    private static SignerDetail Describe(SignerInformation signer, IStore<X509Certificate> certificates)
    {
        var certificate = certificates.EnumerateMatches(signer.SignerID).FirstOrDefault();
        if (certificate is null)
        {
            return new SignerDetail("(firmatario sconosciuto)", "(sconosciuto)", SigningTimeOf(signer),
                DateTime.MinValue, DateTime.MinValue, false,
                "il certificato del firmatario non è incluso nella busta");
        }

        bool integrity;
        string? problem = null;
        try
        {
            // Si verifica con la sola chiave pubblica: passando il certificato,
            // BouncyCastle boccerebbe anche le firme integre fatte con un
            // certificato oggi scaduto. Sono due cose diverse e vanno riportate
            // separatamente — la scadenza la valuta CertificateValidAtSigning.
            integrity = signer.Verify(certificate.GetPublicKey());
            if (!integrity)
                problem = ContentAlteredMessage;
        }
        catch (Exception ex)
        {
            integrity = false;
            // Quando il contenuto è stato toccato, la libreria non risponde
            // "falso": solleva un errore sul digest. È il caso più importante
            // di tutti e all'utente va detto in italiano, non in gergo.
            problem = LooksLikeTampering(ex)
                ? ContentAlteredMessage
                : $"verifica non riuscita: {ex.Message}";
        }

        return new SignerDetail(
            certificate.SubjectDN.ToString(),
            certificate.IssuerDN.ToString(),
            SigningTimeOf(signer),
            certificate.NotBefore.ToUniversalTime(),
            certificate.NotAfter.ToUniversalTime(),
            integrity,
            problem);
    }

    private const string ContentAlteredMessage =
        "la firma non corrisponde al contenuto: il documento è stato alterato dopo la firma";

    private static bool LooksLikeTampering(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("message-digest") || message.Contains("digest attribute") ||
            message.Contains("does not match");
    }

    private static DateTime? SigningTimeOf(SignerInformation signer)
    {
        var attribute = signer.SignedAttributes?[SigningTimeOid];
        if (attribute?.AttrValues.Count is not > 0)
            return null;
        try
        {
            return Org.BouncyCastle.Asn1.Cms.Time.GetInstance(attribute.AttrValues[0]).ToDateTime()
                .ToUniversalTime();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
