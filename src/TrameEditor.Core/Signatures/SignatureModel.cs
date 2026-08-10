namespace TrameEditor.Core.Signatures;

/// <summary>Chi ha firmato, quando, e se la firma regge il controllo.</summary>
public sealed record SignerDetail(
    string Subject,
    string Issuer,
    DateTime? SignedAt,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IntegrityVerified,
    string? Problem = null)
{
    /// <summary>Nome leggibile del firmatario, estratto dal campo CN del certificato.</summary>
    public string DisplayName => CommonNameOf(Subject);

    public string IssuerName => CommonNameOf(Issuer);

    /// <summary>Il certificato era scaduto (o non ancora valido) al momento della firma?
    /// Se la data di firma non c'è, si guarda a oggi.</summary>
    public bool CertificateValidAtSigning
    {
        get
        {
            var moment = SignedAt ?? DateTime.UtcNow;
            return moment >= ValidFrom && moment <= ValidTo;
        }
    }

    internal static string CommonNameOf(string distinguishedName)
    {
        foreach (var part in distinguishedName.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].Trim();
        }
        return distinguishedName;
    }
}

/// <summary>Una firma trovata dentro un PDF.</summary>
public sealed record PdfSignatureDetail(
    string FieldName,
    SignerDetail Signer,
    bool CoversWholeDocument,
    string Algorithm,
    string? DeclaredName,
    string? Reason,
    string? Location);

/// <summary>Contenuto estratto da un file firmato (.p7m) e chi lo ha firmato.</summary>
public sealed record P7mContent(
    byte[] Data,
    string SuggestedFileName,
    IReadOnlyList<SignerDetail> Signers)
{
    /// <summary>Che cosa c'era dentro la busta: serve a decidere come aprirlo.</summary>
    public bool IsPdf => Data.Length > 4 && Data[0] == '%' && Data[1] == 'P' && Data[2] == 'D' && Data[3] == 'F';
}

/// <summary>Il file non è una busta firmata leggibile: il messaggio lo spiega.</summary>
public sealed class SignatureReadException(string message, Exception? inner = null)
    : Exception(message, inner);
