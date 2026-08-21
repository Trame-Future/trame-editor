using TrameEditor.Core.Invoices;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Una fattura elettronica FatturaPA letta in chiaro: l'XML che nessuno riesce a leggere
/// diventa dati con i nomi delle cose. Funziona anche sui file dentro una busta firmata.
/// </summary>
public static class InvoiceCommand
{
    public static object Run(CommandLine line)
    {
        var path = Paths.ExistingFile(line.At(0, "file"));
        if (!FatturaElettronicaReader.LooksLikeInvoice(path))
            throw new InvalidOperationException(
                "Questo file non sembra una fattura elettronica FatturaPA.");

        var invoice = FatturaElettronicaReader.Read(path);
        var attachmentsFolder = line.Has("allegati") ? Paths.Folder(line.Required("allegati")) : null;

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "fattura",
            ["file"] = path,
            ["formatoTrasmissione"] = invoice.FormatoTrasmissione,
            ["progressivoInvio"] = invoice.ProgressivoInvio,
            ["codiceDestinatario"] = invoice.CodiceDestinatario,
            ["pecDestinatario"] = invoice.PecDestinatario,
            ["fornitore"] = Describe(invoice.Fornitore),
            ["cliente"] = Describe(invoice.Cliente),
            ["documenti"] = invoice.Documenti.Select(d => Describe(d, attachmentsFolder)).ToList(),
        };
    }

    private static object Describe(InvoiceParty party) => new Dictionary<string, object?>
    {
        ["nome"] = party.Nome,
        ["partitaIva"] = party.PartitaIva,
        ["codiceFiscale"] = party.CodiceFiscale,
        ["indirizzo"] = party.IndirizzoCompleto,
        ["nazione"] = party.Nazione,
        ["regimeFiscale"] = party.RegimeFiscale,
    };

    private static object Describe(InvoiceDocument document, string? attachmentsFolder)
    {
        var described = new Dictionary<string, object?>
        {
            ["tipo"] = document.TipoDocumento,
            ["numero"] = document.Numero,
            ["data"] = document.Data?.ToString("yyyy-MM-dd"),
            ["divisa"] = document.Divisa,
            ["imponibile"] = document.TotaleImponibile,
            ["imposta"] = document.TotaleImposta,
            ["totale"] = document.TotaleCalcolato,
            // Il totale dichiarato dal documento manca spesso: distinguerlo da quello
            // ricalcolato evita di far passare una somma nostra per un dato della fattura.
            ["totaleDichiarato"] = document.ImportoTotale,
            ["causali"] = document.Causali,
            ["condizioniPagamento"] = document.CondizioniPagamento,
            ["righe"] = document.Righe.Select(riga => new Dictionary<string, object?>
            {
                ["numero"] = riga.Numero,
                ["descrizione"] = riga.Descrizione,
                ["quantita"] = riga.Quantita,
                ["unita"] = riga.UnitaMisura,
                ["prezzoUnitario"] = riga.PrezzoUnitario,
                ["totale"] = riga.PrezzoTotale,
                ["aliquotaIva"] = riga.AliquotaIva,
            }).ToList(),
            ["riepilogoIva"] = document.RiepilogoIva.Select(r => new Dictionary<string, object?>
            {
                ["aliquota"] = r.Aliquota,
                ["imponibile"] = r.Imponibile,
                ["imposta"] = r.Imposta,
                ["natura"] = r.Natura,
            }).ToList(),
        };

        described["allegati"] = document.Allegati.Select(allegato =>
        {
            var descritto = new Dictionary<string, object?>
            {
                ["nome"] = allegato.Nome,
                ["formato"] = allegato.Formato,
                ["descrizione"] = allegato.Descrizione,
                ["byte"] = allegato.Data.Length,
            };
            if (attachmentsFolder is not null)
            {
                var written = System.IO.Path.Combine(attachmentsFolder, SafeName(allegato.Nome));
                File.WriteAllBytes(written, allegato.Data);
                descritto["salvatoIn"] = written;
            }
            return descritto;
        }).ToList();

        return described;
    }

    /// <summary>Il nome dell'allegato arriva dalla fattura, cioè da fuori: ripulirlo prima
    /// di usarlo come nome di file evita che scriva dove non deve.</summary>
    private static string SafeName(string name)
    {
        var cleaned = new string(name.Select(c =>
            System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "allegato" : cleaned;
    }
}
