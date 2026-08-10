namespace TrameEditor.Core.Invoices;

/// <summary>Un soggetto della fattura: chi emette o chi riceve.</summary>
public sealed record InvoiceParty(
    string? Denominazione,
    string? PartitaIva,
    string? CodiceFiscale,
    string? Indirizzo,
    string? Cap,
    string? Comune,
    string? Provincia,
    string? Nazione,
    string? RegimeFiscale)
{
    public string Nome => Denominazione ?? CodiceFiscale ?? PartitaIva ?? "(non indicato)";

    public string? IndirizzoCompleto
    {
        get
        {
            var parti = new[] { Indirizzo, string.Join(" ", new[] { Cap, Comune }.Where(p => !string.IsNullOrWhiteSpace(p))) }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (parti.Count == 0)
                return null;
            var riga = string.Join(", ", parti);
            return string.IsNullOrWhiteSpace(Provincia) ? riga : $"{riga} ({Provincia})";
        }
    }
}

/// <summary>Una riga di dettaglio della fattura.</summary>
public sealed record InvoiceLine(
    int? Numero,
    string Descrizione,
    decimal? Quantita,
    string? UnitaMisura,
    decimal? PrezzoUnitario,
    decimal? PrezzoTotale,
    decimal? AliquotaIva,
    string? Natura);

/// <summary>Una riga del riepilogo IVA.</summary>
public sealed record VatSummaryLine(
    decimal? Aliquota,
    string? Natura,
    decimal? Imponibile,
    decimal? Imposta,
    string? Esigibilita,
    string? RiferimentoNormativo);

public sealed record PaymentDetail(
    string? Modalita,
    DateOnly? Scadenza,
    decimal? Importo,
    string? Iban,
    string? Beneficiario);

/// <summary>Un file allegato dentro la fattura: spesso è la "copia di cortesia" in PDF.</summary>
public sealed record InvoiceAttachment(string Nome, string? Formato, string? Descrizione, byte[] Data);

/// <summary>Un documento dentro la fattura: un file può contenerne più d'uno (lotto).</summary>
public sealed record InvoiceDocument(
    string? TipoDocumento,
    string? Divisa,
    DateOnly? Data,
    string? Numero,
    decimal? ImportoTotale,
    decimal? ImportoBollo,
    IReadOnlyList<string> Causali,
    IReadOnlyList<InvoiceLine> Righe,
    IReadOnlyList<VatSummaryLine> RiepilogoIva,
    string? CondizioniPagamento,
    IReadOnlyList<PaymentDetail> Pagamenti,
    IReadOnlyList<InvoiceAttachment> Allegati)
{
    public decimal TotaleImponibile => RiepilogoIva.Sum(r => r.Imponibile ?? 0);

    public decimal TotaleImposta => RiepilogoIva.Sum(r => r.Imposta ?? 0);

    /// <summary>Il totale dichiarato dal documento se c'è, altrimenti la somma
    /// del riepilogo IVA: le fatture non sono obbligate a dichiararlo.</summary>
    public decimal TotaleCalcolato => ImportoTotale ?? TotaleImponibile + TotaleImposta;
}

/// <summary>Una fattura elettronica letta dal suo XML.</summary>
public sealed record ElectronicInvoice(
    string? FormatoTrasmissione,
    string? ProgressivoInvio,
    string? CodiceDestinatario,
    string? PecDestinatario,
    InvoiceParty Fornitore,
    InvoiceParty Cliente,
    IReadOnlyList<InvoiceDocument> Documenti);

/// <summary>Il file non è una fattura elettronica leggibile: il messaggio lo spiega.</summary>
public sealed class InvoiceReadException(string message, Exception? inner = null)
    : Exception(message, inner);
