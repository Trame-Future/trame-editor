# Guida di TrameEditor

TrameEditor è un editor **gratuito e open source** per file **PDF, testo (.txt) e Markdown (.md)**.
Funziona tutto **sul tuo computer**: nessun account, nessun caricamento su internet, nessuna raccolta di dati.

> **Regola d'oro**: TrameEditor non tocca mai i tuoi file originali finché non salvi tu.
> Le modifiche ai PDF avvengono su una copia di lavoro temporanea.

---

## Aprire e salvare

- **Apri** (Ctrl+O), trascina i file sulla finestra, oppure **File → Recenti**.
- All'avvio l'app **riapre i file dell'ultima sessione**.
- **Salva** (Ctrl+S) e **Salva con nome** (Ctrl+Maiusc+S). Per i PDF, "Salva con nome" applica tutte le modifiche in sospeso (pagine, testo, annotazioni).
- **Stampa** con Ctrl+P.

## Documenti di testo (.txt)

- L'app riconosce e **preserva** la codifica (UTF-8, UTF-16, ANSI) e i fine riga (CRLF/LF), mostrati nella barra blu in basso.
- Cerca e sostituisci con **Ctrl+F**.
- **Autosalvataggio**: ogni 30 secondi le modifiche non salvate vengono messe in bozza; se il PC si spegne di colpo, al riavvio TrameEditor propone di **ripristinarle**.

## Markdown (.md)

- **Anteprima affiancata** in tempo reale (pulsante "Anteprima" nella barra del documento).
- Toolbar di formattazione: grassetto, corsivo, titoli, elenchi, citazioni, link, tabelle.
- **Esporta HTML** ed **Esporta PDF** dal gruppo Strumenti del ribbon.
- La stampa usa la resa dell'anteprima (se l'anteprima è attiva).

## PDF — visualizzare

- **Zoom** con i pulsanti −/+ o **Ctrl+rotellina** (50–300%).
- **Miniature** a sinistra: clic per andare alla pagina, **selezione multipla** per le operazioni.
- **Ricerca** nella casella in alto (Invio): i risultati sono **evidenziati in giallo** sulle pagine e elencati a destra.

## PDF — pagine

Seleziona le pagine dalle miniature, poi:

- **⟲ / ⟳** ruota — **Elimina** — **▲/▼** sposta — **Estrai…** crea un nuovo PDF con le pagine scelte.
- Puoi anche **riordinare trascinando le miniature**.
- **Unisci PDF** (ribbon → Strumenti) combina più file in uno.
- Le modifiche restano "in sospeso" finché non usi **Salva con nome**.

## PDF — modificare il testo

1. Premi **✎ Modifica testo**: le righe modificabili si evidenziano in azzurro.
2. Clicca una riga, scrivi il nuovo testo nella barra gialla, **Applica**.
3. TrameEditor rimuove davvero il testo originale dal file (niente testo nascosto) e ridisegna il nuovo nella stessa posizione.

**Onestà sui font**: se il font originale non contiene i caratteri che hai scritto, l'app usa un font di sistema equivalente o un sostituto — e **te lo dice prima** di applicare.

**Limiti dichiarati**: testo ruotato o verticale, contenuti dentro moduli grafici (XObject) e scansioni senza OCR non sono modificabili; la riga riscritta perde il kerning originale.

## PDF — annotazioni

- **Evidenzia**: attiva lo strumento e clicca una riga di testo.
- **Nota**: clicca un punto della pagina e scrivi il testo (icona commento gialla).
- **Timbro**: scegli un'immagine (firma, logo) e clicca dove posizionarla.

## PDF — moduli (AcroForm)

- Pulsante **Modulo**: si apre il pannello con i campi del PDF (testo, caselle, scelte).
- Compila e premi **Applica al PDF**.
- Spunta **"Appiattisci"** per rendere i valori definitivi e non più modificabili (utile prima dell'invio).

## PDF — OCR (scansioni)

- Pulsante **OCR**: le pagine scansionate vengono riconosciute (italiano + inglese, tutto **offline**) e ricevono un layer di testo invisibile.
- Dopo l'OCR il documento diventa **ricercabile** e il testo riconosciuto si può perfino modificare.

## PDF — Compila per me ⚡

Nel pannello **Modulo** il pulsante **"⚡ Compila per me"** riempie i campi al posto tuo:

1. La prima volta compili la scheda **"I miei dati"** (ribbon → Strumenti): nome, codice
   fiscale, indirizzo, IBAN, contatti… Solo i campi che vuoi.
2. I dati restano **solo sul tuo computer**, cifrati con la protezione dell'account Windows —
   niente cloud, niente password da ricordare.
3. Apri un modulo, premi il pulsante: i campi riconosciuti dall'etichetta ("Codice fiscale",
   "Comune di residenza"…) si riempiono da soli. **I campi già compilati non vengono mai toccati.**
4. Controlli, correggi se serve, e premi "Applica al PDF".

## PDF — Anonimizza 🛡

Il pulsante **Anonimizza…** trova automaticamente i dati personali nel documento:
**codici fiscali, IBAN, email, numeri di telefono, targhe**.

1. Controlla l'elenco e deseleziona gli eventuali falsi positivi.
2. Conferma: i dati selezionati vengono **rimossi davvero** dal file (sostituiti da "X") —
   non coperti con un rettangolo che lascia il testo copiabile sotto.
3. Con la spunta attiva vengono ripuliti anche i **metadati** (autore, titolo, applicazione).

Utile prima di inviare un documento a terzi. Se qualche riga non è rimovibile
(testo dentro moduli grafici), l'app **te lo dice chiaramente**: mai una falsa sicurezza.
Le scansioni vanno prima passate con l'**OCR**.

## PDF — altri strumenti

- **Comprimi…** salva una copia alleggerita (immagini ricompresse) — utile per allegati email/PEC con limiti di peso.
- **Proteggi PDF** salva una copia **cifrata con password** (AES-256). ⚠️ Senza la password il file non sarà più apribile: conservala.
- I PDF protetti si aprono normalmente: l'app chiede la password e il file originale resta cifrato.
- **Esporta immagini** (una PNG per pagina), **Esporta testo** (.txt) e **Immagini in PDF** sono nel gruppo Strumenti.

## Chiedi al documento 💬 (AI locale)

Il pulsante **"💬 Chiedi"** apre un assistente che risponde a domande sul PDF aperto:
*"quanto devo pagare e entro quando?"*, *"qual è la durata del contratto?"*…

- L'AI gira **interamente sul tuo computer** grazie a **Ollama** (gratuito): se non
  è installato, il pannello ti guida — scarica da [ollama.com](https://ollama.com),
  poi nel terminale: `ollama pull qwen2.5:3b`.
- Ogni risposta indica le **pagine di provenienza**: clicca "pag. N" per andarci.
- Regole d'onestà: l'assistente usa **solo il contenuto del documento**, e se non
  trova la risposta lo dice. Ma può comunque sbagliare: **verifica sempre sul documento**.
- Le scansioni vanno prima passate con l'**OCR**.

## Confronta due PDF ⇄

**Ribbon → Strumenti → Confronta PDF**: scegli due versioni di un documento e vedi
riga per riga cosa è stato **aggiunto** (verde) e **rimosso** (rosso), con il numero
di pagina. Le parti identiche lunghe vengono compresse in "⋯ N righe identiche ⋯".
Puoi salvare un **rapporto HTML** da allegare o archiviare.
Il confronto riguarda il testo, non la grafica.

## Ricette (elaborazione in serie) ⚙

**Ribbon → Strumenti → Ricette**: scegli tanti PDF e applica a tutti la stessa
sequenza di passi: **OCR → Anonimizza → Comprimi → Proteggi con password**
(attivi solo quelli che ti servono). I risultati finiscono in una cartella a tua
scelta; **gli originali non vengono mai toccati**. Ogni file riceve un esito
dettagliato; i PDF già protetti da password vengono saltati e segnalati.

## Scorciatoie

| Tasti | Azione |
|-------|--------|
| Ctrl+N / Ctrl+O | Nuovo / Apri |
| Ctrl+S / Ctrl+Maiusc+S | Salva / Salva con nome |
| Ctrl+P | Stampa |
| Ctrl+F | Trova (e sostituisci nei file di testo) |
| Ctrl+W | Chiudi scheda |
| Ctrl+Z / Ctrl+Y | Annulla / Ripeti |
| Ctrl+rotellina | Zoom PDF |
| F1 | Questa guida |

---

## Informazioni

TrameEditor è un prodotto di **Trame Future srls** — [www.tramefuture.com](https://www.tramefuture.com) —
distribuito con licenza AGPL-3.0. Segnalazioni e suggerimenti sono benvenuti: se un PDF si comporta
male, inviacelo (se puoi condividerlo): ogni caso risolto rende l'app migliore per tutti.
