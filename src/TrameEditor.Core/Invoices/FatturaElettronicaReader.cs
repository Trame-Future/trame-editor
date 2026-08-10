using System.Globalization;
using System.Xml.Linq;
using Path = System.IO.Path;

namespace TrameEditor.Core.Invoices;

/// <summary>
/// Legge una fattura elettronica italiana (FatturaPA) dal suo XML.
/// <para>
/// Gli elementi si cercano <b>per nome locale</b>, ignorando il prefisso del
/// namespace: gli stessi dati arrivano come <c>p:FatturaElettronica</c>,
/// <c>ns2:FatturaElettronica</c> o senza prefisso a seconda di chi ha emesso il
/// file, e una lettura rigida fallirebbe su metà delle fatture vere.
/// </para>
/// </summary>
public static class FatturaElettronicaReader
{
    /// <summary>Il file ha l'aria di una fattura elettronica?
    /// Guarda il contenuto, non l'estensione.</summary>
    public static bool LooksLikeInvoice(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 40 * 1024 * 1024)
                return false;
            using var reader = new StreamReader(path);
            var head = new char[4096];
            var read = reader.Read(head, 0, head.Length);
            return new string(head, 0, read).Contains("FatturaElettronica", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static ElectronicInvoice Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (InvoiceReadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvoiceReadException(
                $"Impossibile leggere \"{Path.GetFileName(path)}\": {ex.Message}", ex);
        }
    }

    public static ElectronicInvoice Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new InvoiceReadException(
                "Il file non è un XML leggibile: potrebbe essere danneggiato.", ex);
        }

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Contains("FatturaElettronica", StringComparison.Ordinal))
            throw new InvoiceReadException(
                "Questo XML non è una fattura elettronica (manca l'elemento FatturaElettronica).");

        var header = root.Child("FatturaElettronicaHeader");
        var trasmissione = header.Child("DatiTrasmissione");

        var invoice = new ElectronicInvoice(
            trasmissione.Value("FormatoTrasmissione"),
            trasmissione.Value("ProgressivoInvio"),
            trasmissione.Value("CodiceDestinatario"),
            trasmissione.Value("PECDestinatario"),
            ReadParty(header.Child("CedentePrestatore")),
            ReadParty(header.Child("CessionarioCommittente")),
            [.. root.Children("FatturaElettronicaBody").Select(ReadDocument)]);

        if (invoice.Documenti.Count == 0)
            throw new InvoiceReadException(
                "La fattura non contiene documenti (manca FatturaElettronicaBody).");

        return invoice;
    }

    private static InvoiceParty ReadParty(XElement? party)
    {
        var anagrafici = party.Child("DatiAnagrafici");
        var anagrafica = anagrafici.Child("Anagrafica");
        var sede = party.Child("Sede");

        var denominazione = anagrafica.Value("Denominazione");
        if (string.IsNullOrWhiteSpace(denominazione))
        {
            var nome = anagrafica.Value("Nome");
            var cognome = anagrafica.Value("Cognome");
            denominazione = string.Join(" ", new[] { nome, cognome }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            if (denominazione.Length == 0)
                denominazione = null;
        }

        var iva = anagrafici.Child("IdFiscaleIVA");
        var partitaIva = iva is null
            ? null
            : string.Join(string.Empty, new[] { iva.Value("IdPaese"), iva.Value("IdCodice") }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

        return new InvoiceParty(
            denominazione,
            string.IsNullOrWhiteSpace(partitaIva) ? null : partitaIva,
            anagrafici.Value("CodiceFiscale"),
            JoinAddress(sede.Value("Indirizzo"), sede.Value("NumeroCivico")),
            sede.Value("CAP"),
            sede.Value("Comune"),
            sede.Value("Provincia"),
            sede.Value("Nazione"),
            anagrafici.Value("RegimeFiscale"));
    }

    private static string? JoinAddress(string? indirizzo, string? civico)
    {
        if (string.IsNullOrWhiteSpace(indirizzo))
            return null;
        return string.IsNullOrWhiteSpace(civico) ? indirizzo : $"{indirizzo} {civico}";
    }

    private static InvoiceDocument ReadDocument(XElement body)
    {
        var generali = body.Child("DatiGenerali").Child("DatiGeneraliDocumento");
        var beniServizi = body.Child("DatiBeniServizi");

        return new InvoiceDocument(
            generali.Value("TipoDocumento"),
            generali.Value("Divisa"),
            ReadDate(generali.Value("Data")),
            generali.Value("Numero"),
            ReadDecimal(generali.Value("ImportoTotaleDocumento")),
            ReadDecimal(generali.Child("DatiBollo").Value("ImportoBollo")),
            [.. generali.Children("Causale").Select(c => c.Value.Trim()).Where(c => c.Length > 0)],
            [.. beniServizi.Children("DettaglioLinee").Select(ReadLine)],
            [.. beniServizi.Children("DatiRiepilogo").Select(ReadVatSummary)],
            body.Child("DatiPagamento").Value("CondizioniPagamento"),
            [.. body.Children("DatiPagamento")
                .SelectMany(p => p.Children("DettaglioPagamento"))
                .Select(ReadPayment)],
            [.. body.Children("Allegati").Select(ReadAttachment).OfType<InvoiceAttachment>()]);
    }

    private static InvoiceLine ReadLine(XElement line) => new(
        ReadInt(line.Value("NumeroLinea")),
        line.Value("Descrizione") ?? "(senza descrizione)",
        ReadDecimal(line.Value("Quantita")),
        line.Value("UnitaMisura"),
        ReadDecimal(line.Value("PrezzoUnitario")),
        ReadDecimal(line.Value("PrezzoTotale")),
        ReadDecimal(line.Value("AliquotaIVA")),
        line.Value("Natura"));

    private static VatSummaryLine ReadVatSummary(XElement summary) => new(
        ReadDecimal(summary.Value("AliquotaIVA")),
        summary.Value("Natura"),
        ReadDecimal(summary.Value("ImponibileImporto")),
        ReadDecimal(summary.Value("Imposta")),
        summary.Value("EsigibilitaIVA"),
        summary.Value("RiferimentoNormativo"));

    private static PaymentDetail ReadPayment(XElement payment) => new(
        payment.Value("ModalitaPagamento"),
        ReadDate(payment.Value("DataScadenzaPagamento")),
        ReadDecimal(payment.Value("ImportoPagamento")),
        payment.Value("IBAN"),
        payment.Value("Beneficiario"));

    private static InvoiceAttachment? ReadAttachment(XElement attachment)
    {
        var name = attachment.Value("NomeAttachment");
        var content = attachment.Value("Attachment");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
            return null;
        try
        {
            var data = Convert.FromBase64String(content.Trim());
            return new InvoiceAttachment(name.Trim(), attachment.Value("FormatoAttachment"),
                attachment.Value("DescrizioneAttachment"), data);
        }
        catch (FormatException)
        {
            return null; // allegato illeggibile: meglio ometterlo che mostrarlo corrotto
        }
    }

    // ----- Conversioni tolleranti -----

    private static DateOnly? ReadDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static decimal? ReadDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}

/// <summary>Ricerca degli elementi per nome locale: il prefisso del namespace
/// cambia da fornitore a fornitore e non deve contare.</summary>
internal static class XmlLocalNameExtensions
{
    internal static XElement? Child(this XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    internal static IEnumerable<XElement> Children(this XElement? parent, string localName) =>
        parent?.Elements().Where(e => e.Name.LocalName == localName) ?? [];

    internal static string? Value(this XElement? parent, string localName)
    {
        var value = parent.Child(localName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
