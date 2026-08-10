using System.Globalization;
using System.Text;

namespace TrameEditor.Core.Invoices;

/// <summary>
/// Trasforma una fattura elettronica in un documento leggibile da un essere
/// umano. Il risultato è Markdown, così si vede subito nell'anteprima e si
/// esporta in PDF o PDF/A con gli strumenti che ci sono già.
/// <para>
/// In testa al documento c'è sempre una riga che ricorda che questa è una
/// <b>vista</b>: il documento che fa fede resta il file XML firmato.
/// </para>
/// </summary>
public static class FatturaRenderer
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string ToMarkdown(ElectronicInvoice invoice, string? sourceFileName = null)
    {
        var text = new StringBuilder();
        var primo = invoice.Documenti[0];

        text.AppendLine($"# {FatturaCodes.TipoDocumento(primo.TipoDocumento)} n. {primo.Numero ?? "—"}");
        text.AppendLine();
        text.AppendLine("> **Vista leggibile** generata da TrameEditor. Il documento che fa fede è " +
            "il file XML" + (sourceFileName is null ? "" : $" `{sourceFileName}`") +
            ": questa è una trascrizione dei suoi contenuti, comoda da leggere e da archiviare.");
        text.AppendLine();

        AppendParties(text, invoice);

        foreach (var (documento, indice) in invoice.Documenti.Select((d, i) => (d, i)))
        {
            if (invoice.Documenti.Count > 1)
            {
                text.AppendLine($"---");
                text.AppendLine();
                text.AppendLine($"## Documento {indice + 1} di {invoice.Documenti.Count}");
                text.AppendLine();
            }
            AppendDocument(text, documento);
        }

        AppendTransmission(text, invoice);
        return text.ToString();
    }

    private static void AppendParties(StringBuilder text, ElectronicInvoice invoice)
    {
        text.AppendLine("## Chi emette e chi riceve");
        text.AppendLine();
        text.AppendLine("| | Fornitore | Cliente |");
        text.AppendLine("|---|---|---|");
        text.AppendLine($"| **Denominazione** | {Cell(invoice.Fornitore.Nome)} | {Cell(invoice.Cliente.Nome)} |");
        text.AppendLine($"| Partita IVA | {Cell(invoice.Fornitore.PartitaIva)} | {Cell(invoice.Cliente.PartitaIva)} |");
        text.AppendLine($"| Codice fiscale | {Cell(invoice.Fornitore.CodiceFiscale)} | {Cell(invoice.Cliente.CodiceFiscale)} |");
        text.AppendLine($"| Indirizzo | {Cell(invoice.Fornitore.IndirizzoCompleto)} | {Cell(invoice.Cliente.IndirizzoCompleto)} |");
        if (invoice.Fornitore.RegimeFiscale is not null)
            text.AppendLine($"| Regime fiscale | {Cell(FatturaCodes.RegimeFiscale(invoice.Fornitore.RegimeFiscale))} | |");
        text.AppendLine();
    }

    private static void AppendDocument(StringBuilder text, InvoiceDocument documento)
    {
        text.AppendLine("## Il documento");
        text.AppendLine();
        text.AppendLine("| Voce | Valore |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| Tipo | {Cell(FatturaCodes.TipoDocumento(documento.TipoDocumento))} |");
        text.AppendLine($"| Numero | {Cell(documento.Numero)} |");
        text.AppendLine($"| Data | {Cell(Format(documento.Data))} |");
        text.AppendLine($"| Divisa | {Cell(documento.Divisa)} |");
        if (documento.ImportoBollo is { } bollo)
            text.AppendLine($"| Bollo | {Cell(Money(bollo, documento.Divisa))} |");
        text.AppendLine($"| **Totale documento** | **{Money(documento.TotaleCalcolato, documento.Divisa)}**" +
            (documento.ImportoTotale is null ? " *(calcolato dal riepilogo IVA)*" : string.Empty) + " |");
        text.AppendLine();

        if (documento.Causali.Count > 0)
        {
            text.AppendLine("**Causale**");
            text.AppendLine();
            foreach (var causale in documento.Causali)
                text.AppendLine($"- {causale}");
            text.AppendLine();
        }

        AppendLines(text, documento);
        AppendVatSummary(text, documento);
        AppendPayments(text, documento);
        AppendAttachments(text, documento);
    }

    private static void AppendLines(StringBuilder text, InvoiceDocument documento)
    {
        if (documento.Righe.Count == 0)
            return;

        text.AppendLine("## Righe della fattura");
        text.AppendLine();
        text.AppendLine("| # | Descrizione | Quantità | Prezzo unitario | IVA | Totale |");
        text.AppendLine("|---:|---|---:|---:|---:|---:|");
        foreach (var riga in documento.Righe)
        {
            var quantita = riga.Quantita is null
                ? string.Empty
                : Number(riga.Quantita.Value) + (riga.UnitaMisura is null ? string.Empty : $" {riga.UnitaMisura}");
            var iva = riga.AliquotaIva is > 0
                ? $"{Number(riga.AliquotaIva.Value)}%"
                : riga.Natura is not null ? FatturaCodes.Natura(riga.Natura) : string.Empty;
            text.AppendLine($"| {riga.Numero} | {Cell(riga.Descrizione)} | {quantita} | " +
                $"{MoneyOrEmpty(riga.PrezzoUnitario, documento.Divisa)} | {iva} | " +
                $"{MoneyOrEmpty(riga.PrezzoTotale, documento.Divisa)} |");
        }
        text.AppendLine();
    }

    private static void AppendVatSummary(StringBuilder text, InvoiceDocument documento)
    {
        if (documento.RiepilogoIva.Count == 0)
            return;

        text.AppendLine("## Riepilogo IVA");
        text.AppendLine();
        text.AppendLine("| Aliquota | Imponibile | Imposta | Esigibilità |");
        text.AppendLine("|---|---:|---:|---|");
        foreach (var riga in documento.RiepilogoIva)
        {
            var aliquota = riga.Aliquota is > 0
                ? $"{Number(riga.Aliquota.Value)}%"
                : FatturaCodes.Natura(riga.Natura);
            text.AppendLine($"| {aliquota} | {MoneyOrEmpty(riga.Imponibile, documento.Divisa)} | " +
                $"{MoneyOrEmpty(riga.Imposta, documento.Divisa)} | " +
                $"{(riga.Esigibilita is null ? string.Empty : FatturaCodes.EsigibilitaIva(riga.Esigibilita))} |");
        }
        text.AppendLine($"| **Totale** | **{Money(documento.TotaleImponibile, documento.Divisa)}** | " +
            $"**{Money(documento.TotaleImposta, documento.Divisa)}** | |");
        text.AppendLine();

        var normativi = documento.RiepilogoIva
            .Select(r => r.RiferimentoNormativo)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();
        if (normativi.Count > 0)
        {
            foreach (var riferimento in normativi)
                text.AppendLine($"*{riferimento}*");
            text.AppendLine();
        }
    }

    private static void AppendPayments(StringBuilder text, InvoiceDocument documento)
    {
        if (documento.Pagamenti.Count == 0 && documento.CondizioniPagamento is null)
            return;

        text.AppendLine("## Pagamento");
        text.AppendLine();
        if (documento.CondizioniPagamento is not null)
        {
            text.AppendLine($"Condizioni: **{FatturaCodes.CondizioniPagamento(documento.CondizioniPagamento)}**");
            text.AppendLine();
        }

        if (documento.Pagamenti.Count == 0)
            return;

        text.AppendLine("| Modalità | Scadenza | Importo | IBAN |");
        text.AppendLine("|---|---|---:|---|");
        foreach (var pagamento in documento.Pagamenti)
        {
            text.AppendLine($"| {FatturaCodes.ModalitaPagamento(pagamento.Modalita)} | " +
                $"{Format(pagamento.Scadenza)} | {MoneyOrEmpty(pagamento.Importo, documento.Divisa)} | " +
                $"{Cell(pagamento.Iban)} |");
        }
        text.AppendLine();

        var beneficiari = documento.Pagamenti
            .Select(p => p.Beneficiario)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct()
            .ToList();
        foreach (var beneficiario in beneficiari)
        {
            text.AppendLine($"Beneficiario: {beneficiario}");
            text.AppendLine();
        }
    }

    private static void AppendAttachments(StringBuilder text, InvoiceDocument documento)
    {
        if (documento.Allegati.Count == 0)
            return;

        text.AppendLine("## Allegati");
        text.AppendLine();
        foreach (var allegato in documento.Allegati)
        {
            var formato = allegato.Formato is null ? string.Empty : $" · {allegato.Formato}";
            var descrizione = allegato.Descrizione is null ? string.Empty : $" — {allegato.Descrizione}";
            text.AppendLine($"- **{allegato.Nome}**{formato} ({allegato.Data.Length / 1024} KB){descrizione}");
        }
        text.AppendLine();
    }

    private static void AppendTransmission(StringBuilder text, ElectronicInvoice invoice)
    {
        text.AppendLine("## Dati di trasmissione");
        text.AppendLine();
        text.AppendLine("| Voce | Valore |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| Formato | {Cell(invoice.FormatoTrasmissione)} |");
        text.AppendLine($"| Progressivo invio | {Cell(invoice.ProgressivoInvio)} |");
        text.AppendLine($"| Codice destinatario | {Cell(invoice.CodiceDestinatario)} |");
        if (invoice.PecDestinatario is not null)
            text.AppendLine($"| PEC destinatario | {Cell(invoice.PecDestinatario)} |");
        text.AppendLine();
    }

    // ----- Formattazione -----

    private static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Replace("|", "\\|").Replace("\n", " ");

    private static string Format(DateOnly? date) =>
        date is null ? "—" : date.Value.ToString("dd/MM/yyyy", Italian);

    private static string Number(decimal value) =>
        value == Math.Truncate(value)
            ? value.ToString("0.##", Italian)
            : value.ToString("0.00##", Italian);

    private static string Money(decimal value, string? divisa) =>
        $"{value.ToString("N2", Italian)} {divisa ?? "EUR"}";

    private static string MoneyOrEmpty(decimal? value, string? divisa) =>
        value is null ? string.Empty : Money(value.Value, divisa);
}
