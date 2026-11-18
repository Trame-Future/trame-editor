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

    [McpServerTool(Name = "tabella")]
    [Description("""
        Riconosce le tabelle di un PDF e restituisce le righe e le colonne come dati.
        Con "destinazione" le salva invece in CSV o in Excel, secondo l'estensione del file.
        Serve per estrarre righe di documenti di trasporto, fatture, listini ed estratti conto
        senza doverli ricopiare a mano.
        Guarda "righeFuoriTabella": sono le righe della pagina rimaste fuori dalla griglia
        (titoli, prosa, piè di pagina). Se il numero è alto e ti aspettavi più dati, la tabella
        potrebbe essere fatta in un modo che il riconoscimento non coglie.
        Limite: serve almeno una seconda riga incolonnata allo stesso modo — da una riga sola
        non si distingue una tabella da una frase con spazi larghi.
        """)]
    public static string Tabella(
        [Description("Percorso completo del file PDF")] string file,
        [Description("Dove salvare: un file .csv o .xlsx. Se manca, i dati tornano nella risposta")] string? destinazione = null,
        [Description("Numero della pagina, da 1")] int pagina = 1,
        [Description("Cerca tabelle in tutte le pagine")] bool tutte = false,
        [Description("Separatore del CSV: punto e virgola per Excel in italiano")] string? separatore = null,
        [Description("Sostituire il file di destinazione se esiste già")] bool sovrascrivi = false)
    {
        var positional = destinazione is null ? new[] { file } : [file, destinazione];
        var options = new Dictionary<string, string?> { ["pagina"] = pagina.ToString() };
        if (tutte)
            options["tutte"] = null;
        if (!string.IsNullOrEmpty(separatore))
            options["separatore"] = separatore;
        if (sovrascrivi)
            options["sovrascrivi"] = null;
        return Run("tabella", positional, options);
    }

    [McpServerTool(Name = "estrai")]
    [Description("""
        Applica un modello di estrazione a un PDF o a una cartella di PDF e restituisce i
        campi come dati: una riga per documento. Il modello è un file JSON che dice dove
        stanno i campi sulla pagina, e si prepara una volta sola nell'applicazione.
        Serve quando gli stessi dati vanno presi da molti documenti fatti allo stesso modo:
        fatture di un fornitore, bollette, estratti conto.
        Guarda "completi" e "campiVuoti": un campo vuoto può esserlo davvero, ma se molti
        documenti hanno campi vuoti il modello non corrisponde più a come sono fatti oggi.
        """)]
    public static string Estrai(
        [Description("File PDF, oppure la cartella dei PDF da leggere")] string file,
        [Description("Il file .json del modello")] string modello,
        [Description("Dove salvare la tabella dei risultati (.csv o .xlsx); se manca, i dati tornano nella risposta")] string? destinazione = null,
        [Description("Leggere anche le sottocartelle")] bool sottocartelle = false,
        [Description("Sostituire il file di destinazione se esiste già")] bool sovrascrivi = false)
    {
        var positional = destinazione is null ? new[] { file } : [file, destinazione];
        var options = new Dictionary<string, string?> { ["modello"] = modello };
        if (sottocartelle)
            options["sottocartelle"] = null;
        if (sovrascrivi)
            options["sovrascrivi"] = null;
        return Run("estrai", positional, options);
    }

    [McpServerTool(Name = "dividi")]
    [Description("""
        Divide una stampa cumulativa — molti documenti in un PDF solo — ogni volta che cambia
        il valore letto in una certa area della pagina (il numero del documento, il codice
        cliente). Il nome di ogni file prodotto viene da quel valore.
        L'area si indica con "modello" (più "campo"), oppure con "area" scritta come
        "sinistra,base,larghezza,altezza" in punti PDF, origine in basso a sinistra.
        Le pagine senza quel valore restano attaccate al documento che le precede: le fatture
        di più pagine ripetono il numero solo sulla prima.
        Leggi sempre "avvisi" prima di dire che è andato tutto bene.
        """)]
    public static string Dividi(
        [Description("Il PDF cumulativo da dividere")] string file,
        [Description("Cartella dove scrivere i documenti separati")] string cartella,
        [Description("File .json del modello da cui prendere l'area")] string? modello = null,
        [Description("Nome del campo del modello da usare; se manca, il primo")] string? campo = null,
        [Description("Area a mano: \"sinistra,base,larghezza,altezza\" in punti PDF")] string? area = null,
        [Description("Testo da mettere davanti al nome di ogni file prodotto")] string? prefisso = null,
        [Description("Sostituire i file che esistono già")] bool sovrascrivi = false)
    {
        var options = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(modello))
            options["modello"] = modello;
        if (!string.IsNullOrEmpty(campo))
            options["campo"] = campo;
        if (!string.IsNullOrEmpty(area))
            options["area"] = area;
        if (!string.IsNullOrEmpty(prefisso))
            options["prefisso"] = prefisso;
        if (sovrascrivi)
            options["sovrascrivi"] = null;
        return Run("dividi", [file, cartella], options);
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
