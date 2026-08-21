using System.ComponentModel;
using ModelContextProtocol.Server;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Mcp;

/// <summary>
/// Gli strumenti che il server offre all'agente. Sono gli stessi comandi della riga di
/// comando, chiamati per la stessa strada: qui non c'è logica sui documenti, solo la
/// traduzione dei parametri.
/// <para>
/// Le descrizioni contano quanto il codice: sono l'unica cosa che l'agente legge per
/// decidere se e quando usare uno strumento. Dicono anche i limiti, perché un agente non
/// vede le finestre di avviso.
/// </para>
/// </summary>
[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "righe")]
    [Description("""
        Elenca le righe di testo di una pagina PDF, con la posizione e se si possono
        modificare. È il primo passo prima di qualsiasi modifica: l'"indice" di ogni riga
        è il numero da passare a "sostituisci".
        Attenzione: quello che il documento mostra come una riga di tabella (descrizione,
        quantità, prezzo) qui compare spesso come righe separate, una per colonna.
        """)]
    public static string Righe(
        [Description("Percorso completo del file PDF")] string file,
        [Description("Numero della pagina, da 1")] int pagina = 1,
        [Description("Tutte le pagine invece di una sola")] bool tutte = false,
        [Description("Salta le righe non modificabili")] bool soloModificabili = false)
    {
        var options = new Dictionary<string, string?> { ["pagina"] = pagina.ToString() };
        if (tutte)
            options["tutte"] = null;
        if (soloModificabili)
            options["solo-modificabili"] = null;
        return Run("righe", [file], options);
    }

    [McpServerTool(Name = "sostituisci")]
    [Description("""
        Riscrive una riga di testo dentro un PDF, scrivendo il risultato in un file nuovo.
        Il testo vecchio viene tolto davvero dal file, non coperto.
        Indica la riga con "indiceRiga" (preso da "righe", più sicuro) oppure con
        "testoEsatto"; se quel testo compare più volte nella pagina lo strumento si ferma e
        dice quali indici scegliere, invece di decidere da sé.
        Nella risposta guarda sempre il blocco "carattere": dice se è stato riusato il font
        originale o un sostituto, e con un sostituto il risultato si vede diverso.
        """)]
    public static string Sostituisci(
        [Description("PDF di partenza")] string file,
        [Description("Dove scrivere il risultato: un file che non esiste ancora")] string destinazione,
        [Description("Il testo nuovo della riga")] string testoNuovo,
        [Description("Numero della pagina, da 1")] int pagina = 1,
        [Description("Indice della riga avuto da \"righe\"")] int? indiceRiga = null,
        [Description("In alternativa all'indice: il testo esatto della riga da cambiare")] string? testoEsatto = null,
        [Description("Sostituire il file di destinazione se esiste già")] bool sovrascrivi = false)
    {
        var options = new Dictionary<string, string?>
        {
            ["pagina"] = pagina.ToString(),
            ["nuovo"] = testoNuovo,
        };
        if (indiceRiga is not null)
            options["riga"] = indiceRiga.Value.ToString();
        if (testoEsatto is not null)
            options["testo"] = testoEsatto;
        if (sovrascrivi)
            options["sovrascrivi"] = null;
        return Run("sostituisci", [file, destinazione], options);
    }

    [McpServerTool(Name = "anonimizza")]
    [Description("""
        Toglie i dati personali da un PDF scrivendone una copia: codici fiscali, IBAN,
        email, telefoni, targhe. I dati sono rimossi dal contenuto, non coperti da un
        rettangolo, quindi non restano copiabili sotto.
        Nella risposta guarda "completo" e "righeSaltate": se qualcosa non si è potuto
        togliere il file prodotto lo contiene ancora, e va detto a chi te l'ha chiesto.
        """)]
    public static string Anonimizza(
        [Description("PDF di partenza")] string file,
        [Description("Dove scrivere la copia anonimizzata")] string destinazione,
        [Description("Tipi da togliere, separati da virgola: cf, iban, email, telefono, targa. Vuoto = tutti")] string? tipi = null,
        [Description("Ripulire anche i metadati del documento (autore, titolo)")] bool metadati = false,
        [Description("Sostituire il file di destinazione se esiste già")] bool sovrascrivi = false)
    {
        var options = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(tipi))
            options["tipi"] = tipi;
        if (metadati)
            options["metadati"] = null;
        if (sovrascrivi)
            options["sovrascrivi"] = null;
        return Run("anonimizza", [file, destinazione], options);
    }

    [McpServerTool(Name = "firme")]
    [Description("""
        Dice chi ha firmato un documento e se è stato alterato dopo la firma. Funziona sulle
        buste firmate .p7m e sui PDF con firma incorporata, e sa estrarre il documento vero
        da dentro un .p7m.
        Limite da riferire sempre: verifica l'integrità del documento, NON la validità
        legale della firma (non controlla revoche né accreditamento dell'ente).
        """)]
    public static string Firme(
        [Description("File .p7m o PDF firmato")] string file,
        [Description("Cartella dove estrarre il documento contenuto in un .p7m")] string? cartellaEstrazione = null)
    {
        var options = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(cartellaEstrazione))
            options["estrai"] = cartellaEstrazione;
        return Run("firme", [file], options);
    }

    [McpServerTool(Name = "fattura")]
    [Description("""
        Legge una fattura elettronica italiana (FatturaPA) e la restituisce in chiaro:
        fornitore, cliente, righe, riepilogo IVA, pagamenti, allegati. Accetta sia l'XML sia
        il file dentro una busta firmata .p7m.
        Nota sui totali: "totaleDichiarato" è quello scritto nella fattura e spesso manca;
        "totale" è quello ricalcolato dal riepilogo IVA. Non confonderli.
        """)]
    public static string Fattura(
        [Description("File .xml o .p7m della fattura")] string file,
        [Description("Cartella dove salvare gli allegati contenuti nella fattura")] string? cartellaAllegati = null)
    {
        var options = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(cartellaAllegati))
            options["allegati"] = cartellaAllegati;
        return Run("fattura", [file], options);
    }

    /// <summary>
    /// Un errore previsto torna all'agente come JSON con il suo messaggio, non come
    /// eccezione: «il file di destinazione esiste già, aggiungi sovrascrivi» è
    /// un'istruzione che l'agente può seguire da solo, un errore di protocollo no.
    /// </summary>
    private static string Run(string verb, string[] positional, Dictionary<string, string?> options)
    {
        try
        {
            return Output.Serialize(Dispatcher.Run(CommandLine.Of(verb, positional, options)));
        }
        catch (UsageException ex)
        {
            return Output.Error(ex.Message);
        }
        catch (Exception ex)
        {
            // Nemmeno un errore imprevisto deve arrivare all'agente come guasto del
            // protocollo: gli serve una frase su cui ragionare, non una traccia di stack.
            return Output.Error(Messages.Explain(ex));
        }
    }
}
