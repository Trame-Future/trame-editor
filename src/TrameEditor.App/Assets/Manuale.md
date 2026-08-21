# Guida di TrameEditor

TrameEditor è un editor **gratuito e open source** per file **PDF, testo (.txt) e Markdown (.md)**.
Funziona tutto **sul tuo computer**: nessun account, nessun caricamento su internet, nessuna raccolta di dati.

> **Regola d'oro**: TrameEditor non tocca mai i tuoi file originali finché non salvi tu.
> Le modifiche ai PDF avvengono su una copia di lavoro temporanea.

---

## Trovare i comandi: il menu e la barra

Le funzioni si raggiungono in due modi, e sono le stesse.

- Il **menu classico** in alto — *File, Modifica, Visualizza, Pagine, Converti,
  Sicurezza, Strumenti, ?* — contiene **tutte** le funzioni, sempre. Si apre anche
  con Alt più la lettera sottolineata (Alt+F per File). Le voci che non valgono per
  il documento aperto (ruotare le pagine di un file di testo) sono grigie.
- La **barra multifunzione** sotto tiene a portata di clic quelle che usi di più,
  divise nelle schede *Home* e *Strumenti*.

### Personalizzare la barra multifunzione

**Visualizza → Personalizza la barra multifunzione…** apre una finestra con, a
sinistra, tutte le funzioni raggruppate come nei menu e, a destra, la barra: schede,
riquadri e pulsanti. Puoi aggiungere e togliere pulsanti, spostarli, creare schede e
riquadri nuovi, rinominarli e scegliere se un pulsante è grande o piccolo.

Togliere un pulsante dalla barra **non toglie la funzione**: resta nel menu classico.

Se la barra non ti piace più, **Ripristina impostazioni predefinite** (nella stessa
finestra, oppure *Visualizza → Ripristina la barra predefinita*) la rimette come era
all'installazione. Da quel momento la barra torna anche ad aggiornarsi da sola con le
funzioni aggiunte dai prossimi aggiornamenti: finché non la personalizzi, è
TrameEditor a tenerla al passo.

> La personalizzazione è tua e sta in un file leggibile:
> `%APPDATA%\TrameEditor\barra-multifunzione.json`. Finché non tocchi niente il file
> non esiste nemmeno.

### Il tasto destro su file e cartelle

TrameEditor può aggiungersi al menu che compare col **tasto destro** in Esplora risorse:

- su un **PDF**: *Apri con TrameEditor*, *Converti in PDF/A*, *Anonimizza*;
- su un **.p7m**, un **XML** di fattura, un **.txt** o un **.md**: *Apri con TrameEditor*;
- su una **cartella**: *Cerca nei PDF di questa cartella* e *Estrai dai file firmati (.p7m)*.

Si attiva dall'installazione, oppure in qualsiasi momento da **Strumenti → Impostazioni →
Menu di Esplora risorse**, dove si toglie con la stessa spunta.

> Sono **voci di menu, non associazioni**: il programma con cui i PDF si aprono facendo
> doppio clic **non cambia**. Vengono scritte solo per il tuo utente e non servono diritti
> di amministratore.
>
> **Su Windows 11** le voci stanno sotto **"Mostra altre opzioni"** (o Maiusc+F10): il menu
> breve accetta solo le estensioni installate dallo Store, e TrameEditor non è distribuito
> in quel modo.

---

## Aprire e salvare

- **Apri** (Ctrl+O), trascina i file sulla finestra, oppure **File → Apri recenti**.
- All'avvio l'app **riapre i file dell'ultima sessione**.
- **Salva** (Ctrl+S) e **Salva con nome** (Ctrl+Maiusc+S). Per i PDF, "Salva con nome" applica tutte le modifiche in sospeso (pagine, testo, annotazioni).
- **Stampa** con Ctrl+P.

## Documenti di testo (.txt)

- L'app riconosce e **preserva** la codifica (UTF-8, UTF-16, ANSI) e i fine riga (CRLF/LF), mostrati nella barra blu in basso.
- Cerca e sostituisci con **Ctrl+F**.
- **Autosalvataggio**: ogni 30 secondi le modifiche non salvate vengono messe in bozza; se il PC si spegne di colpo, al riavvio TrameEditor propone di **ripristinarle**.

## Salvare testo e Markdown in PDF (e in PDF/A)

Da un documento di testo o Markdown puoi produrre direttamente un PDF:

- **Esporta PDF** (menu *Converti → Esporta in PDF…*) salva una copia in PDF.
  Il **Markdown** viene impaginato come lo vedi nell'anteprima; un **.txt** resta testo
  semplice a spaziatura fissa — un asterisco resta un asterisco e una riga che comincia
  con `#` non diventa un titolo.
- **Converti in PDF/A** (menu *Converti → Converti in PDF/A…*) salta il passaggio
  intermedio e ti dà direttamente il formato per l'**archiviazione a lungo termine**,
  con lo stesso rapporto preventivo dei PDF.

## Markdown (.md)

- **Anteprima affiancata** in tempo reale (pulsante "Anteprima" nella barra del documento).
- Toolbar di formattazione: grassetto, corsivo, titoli, elenchi, citazioni, link, tabelle.
- **Esporta HTML** ed **Esporta PDF** dal menu **Converti** (o dalla scheda Strumenti della barra).
- La stampa usa la resa dell'anteprima (se l'anteprima è attiva).

## PDF — visualizzare

- **Zoom** con i pulsanti −/+ o **Ctrl+rotellina** (50–300%).
- **Miniature** a sinistra: clic per andare alla pagina, **selezione multipla** per le operazioni.
- **Ricerca** nella casella in alto (Invio): i risultati sono **evidenziati in giallo** sulle pagine e elencati a destra.

## PDF — pagine

Seleziona le pagine dalle miniature, poi:

- **⟲ / ⟳** ruota — **Elimina** — **▲/▼** sposta — **Estrai…** crea un nuovo PDF con le pagine scelte.
- Puoi anche **riordinare trascinando le miniature**.
- **Unisci PDF** (menu **Pagine**) combina più file in uno.
- Le modifiche restano "in sospeso" finché non usi **Salva con nome**.

## PDF — modificare il testo

1. Premi **✎ Modifica testo**: le righe modificabili si evidenziano in azzurro.
2. Clicca una riga, scrivi il nuovo testo nella barra gialla, **Applica**.
3. TrameEditor rimuove davvero il testo originale dal file (niente testo nascosto) e ridisegna il nuovo nella stessa posizione.

**Onestà sui font**: se il font originale non contiene i caratteri che hai scritto, l'app usa un font di sistema equivalente o un sostituto — e **te lo dice prima** di applicare.

**Testo dentro i moduli grafici**: molti gestionali disegnano il corpo di documenti di trasporto e fatture dentro blocchi riusabili (i "form XObject"). Quel testo **si modifica** come il resto della pagina: il blocco viene copiato e solo la pagina che stai modificando usa la copia, così le altre pagine che lo condividono restano com'erano.

**Righe di tabella a colonne**: nei documenti di trasporto e nelle fatture una riga come
`MELINDA 75/80 · 64 · 10` è spesso scritta nel file come un blocco unico, anche se tu ne vedi
tre pezzi separati. TrameEditor te li presenta come tre righe distinte e **ti lascia cambiare
una colonna per volta**: le altre restano dove sono, senza spostarsi di un punto. Vale anche
per la prima colonna, che è il caso in cui l'errore si nota meno.

**Limiti dichiarati**: testo ruotato o verticale e scansioni senza OCR non sono modificabili; la riga riscritta perde il kerning originale. Se in quel punto non c'è testo che la pagina disegna davvero, l'app te lo dice invece di modificarlo a metà.

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

1. La prima volta compili la scheda **"I miei dati"** (menu **Sicurezza**): nome, codice
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

## Fatture elettroniche (XML) 🧾

Una **fattura elettronica** è un file XML: aperto con un programma qualsiasi è illeggibile.
Apri il file con TrameEditor (doppio clic, *Apri*, o trascinandolo) e al suo posto compare
una **trascrizione in italiano**: chi emette e chi riceve, tipo e numero del documento,
righe, riepilogo IVA, pagamento e scadenza, dati di trasmissione.

I **codici** vengono tradotti: *TD01* diventa "Fattura", *MP05* diventa "Bonifico", *N1*
diventa "Escluse ex art. 15". Un codice che non conosciamo viene mostrato **com'è**: su un
documento fiscale inventare una descrizione sarebbe peggio che non darla.

Se la fattura contiene una **copia di cortesia in PDF** (molti fornitori la allegano), viene
estratta e aperta in una scheda a parte.

La vista leggibile è un documento Markdown come gli altri: puoi stamparla o salvarla in **PDF**
e in **PDF/A** con i comandi soliti. In testa c'è sempre scritto che **il documento che fa fede
resta il file XML**: questa è una trascrizione, comoda ma non sostitutiva.

Le fatture arrivano quasi sempre dentro una **busta firmata `.p7m`**: aprila e basta, il
programma toglie la busta, verifica la firma e mostra la fattura (vedi qui sotto).

## Documenti firmati digitalmente (.p7m) ✍

I file **`.p7m`** sono "buste" firmate digitalmente: arrivano di continuo dalla pubblica
amministrazione, dai fornitori e dai commercialisti, e nessun programma comune sa aprirli.

**Aprilo come un file qualsiasi** (doppio clic, *Apri*, o trascinandolo sulla finestra):
TrameEditor tira fuori il documento che c'è dentro, lo apre, e ti mostra **chi l'ha firmato**,
quando, e se il documento è ancora quello firmato. Da lì puoi trattarlo come qualunque altro
documento: anonimizzarlo, convertirlo in PDF/A, confrontarlo, farci domande.

Sui **PDF già firmati** il pulsante **Firme** (gruppo Strumenti) mostra lo stesso riquadro.
Se un PDF firmato è stato modificato dopo la firma, te lo dice: *"la firma non copre tutto il
file"*.

**Che cosa verifichiamo e che cosa no.** Verifichiamo che il documento **non sia stato
alterato** dopo la firma e le date di validità del certificato. **Non** verifichiamo se il
certificato sia stato revocato né se l'ente che l'ha emesso sia accreditato: questo **non è un
accertamento di validità legale**. Per quello serve un verificatore qualificato (per esempio
quello dell'AgID). Il riquadro lo scrive ogni volta.

TrameEditor **non firma** i documenti: la firma digitale qualificata richiede il tuo
dispositivo (smart card o token) e il software del certificatore.

## PDF — Converti in PDF/A (archiviazione) 🗄

Il **PDF/A** è la versione del PDF pensata per durare: tutto ciò che serve a rileggere il
documento fra vent'anni deve stare dentro il file. È il formato richiesto dalla
**conservazione a norma** e dagli archivi pubblici.

Il pulsante **Converti in PDF/A** (gruppo Strumenti) prima **esamina** il documento e ti
mostra che cosa ha trovato, poi ti fa scegliere:

- **Conversione fedele** — il documento resta com'è: testo selezionabile e cercabile.
  Si può fare quando i font sono già incorporati, oppure quando sul tuo computer c'è lo
  stesso font (o un equivalente con le stesse identiche misure: Helvetica↔Arial,
  Times↔Times New Roman, Courier↔Courier New). Il livello prodotto è **PDF/A-2u**, che
  garantisce anche l'estraibilità del testo, oppure **PDF/A-2b** se qualche font non ha
  una mappatura Unicode affidabile.
- **Conversione per immagine** — ogni pagina diventa un'immagine con sotto il testo
  riconosciuto dall'**OCR**. Riesce sempre, ma il testo originale è perduto e il file
  pesa di più. È la strada per i documenti che non si possono convertire fedelmente.

Durante la conversione il programma toglie ciò che il PDF/A non ammette e **te lo elenca**:
JavaScript, file allegati, azioni automatiche, annotazioni senza aspetto grafico, cifratura.
I **moduli compilabili vengono appiattiti**: i valori restano, i campi non sono più modificabili.

**Colori CMYK.** I documenti nati per la stampa usano i colori della quadricromia (CMYK), che
in un PDF/A con profilo sRGB non avrebbero un significato definito: vengono quindi **tradotti
in sRGB**. La traduzione è colorimetrica, fatta con i profili colore di Windows, e il **nero e
i grigi di solo nero restano tali** (il testo nero resta nero, non diventa grigio). Restano
fuori portata — e vengono dichiarati come ostacoli — le immagini JPEG in CMYK e le sfumature
definite in CMYK: per quei documenti resta la conversione per immagine.

**Un limite dichiarato:** la verifica che facciamo è interna — ricontrolla sul file prodotto
gli stessi punti dell'analisi. **Non è una validazione formale.** E se un font usato nel
documento non è incorporato né sostituibile senza spostare il testo, l'app **rifiuta** la
conversione fedele invece di consegnarti un archivio diverso dall'originale.

### Validazione formale con veraPDF (per il deposito a norma)

Se il file ti serve per una **conservazione a norma**, la parola definitiva ce l'ha
**veraPDF**, il validatore libero di riferimento. Non è incluso nel programma — come per
l'assistente AI, si installa solo se serve: **Strumenti → Impostazioni → Validazione PDF/A**,
pulsante *Installa veraPDF automaticamente*. Servono una connessione e circa 200 MB una volta
sola (veraPDF gira su Java: se manca, viene installato anche quello).

Da quel momento ogni conversione in PDF/A termina con il **verdetto formale**: file conforme,
oppure l'elenco delle regole non rispettate. Senza veraPDF tutto il resto continua a
funzionare esattamente come prima.

## PDF — Accessibilità (PDF/UA) ♿

Menu **Converti → Verifica l'accessibilità (PDF/UA)…**. Un PDF accessibile è un documento
che una sintesi vocale sa leggere nell'ordine giusto, sapendo che cosa è un titolo, che cosa
è una tabella e che cosa mostra un'immagine.

La finestra elenca che cosa manca, distinguendo due cose:

- **Quello che possiamo sistemare noi**: la **lingua** del documento (senza, la sintesi
  vocale non sa con che pronuncia leggerlo), il **titolo**, e la richiesta di mostrare il
  titolo al posto del nome del file. Si salva una copia, l'originale non si tocca.
- **Quello che non possiamo inventare**: la **marcatura del contenuto** e i **testi
  alternativi** delle immagini. Dire che cosa è un titolo o che cosa mostra una foto è una
  decisione di chi conosce il documento, non una cosa da indovinare. Si fa nel programma con
  cui il documento è stato scritto — Word, LibreOffice e InDesign esportano PDF marcati.

Se hai installato **veraPDF** puoi chiedere il verdetto formale **PDF/UA-1** con il pulsante
apposito.

> **Il confine, detto chiaro.** veraPDF verifica le regole *controllabili da una macchina*:
> è un limite dello standard, non del programma. Un documento che le supera tutte può essere
> ancora poco accessibile, se le marcature ci sono ma sono sbagliate. La prova definitiva
> resta provarlo con una sintesi vocale.
## PDF — altri strumenti

- **Comprimi…** salva una copia alleggerita (immagini ricompresse). Puoi comprimere al meglio, oppure chiedere che il file **stia sotto un certo peso** (il limite della PEC): si prova per gradi e ci si ferma appena rientra. Se nemmeno al massimo della compressione ci sta, te lo diciamo invece di far finta di esserci riusciti.
- **Proteggi PDF** salva una copia **cifrata con password** (AES-256). ⚠️ Senza la password il file non sarà più apribile: conservala.
- I PDF protetti si aprono normalmente: l'app chiede la password e il file originale resta cifrato.
- **Esporta immagini** (una PNG per pagina), **Esporta testo** (.txt) e **Immagini in PDF** sono nel gruppo Strumenti.

## Chiedi al documento 💬 (AI locale)

Il pulsante **"💬 Chiedi"** apre un assistente che risponde a domande sul documento aperto:
*"quanto devo pagare e entro quando?"*, *"qual è la durata del contratto?"*…
L'AI gira **interamente sul tuo computer**: nessun documento viene caricato da nessuna parte.

Funziona su **PDF, testo e Markdown**. Cambia solo come cita le fonti: nei PDF indica la
**pagina**, nei file di testo e Markdown la **riga** — e in entrambi i casi il pulsantino della
citazione ti porta esattamente lì. Nei documenti di testo l'assistente legge quello che hai
nell'editor, comprese le modifiche non ancora salvate.

**Cosa serve (questa è l'unica funzione con dei requisiti):**

- **Ollama**, il motore gratuito per l'AI locale, più un modello (~2 GB su disco).
  Installazione: scarica da [ollama.com](https://ollama.com), poi nel terminale:
  `ollama pull qwen2.5:3b` — oppure, se usi Docker:
  `docker run -d --name ollama --restart unless-stopped -p 11434:11434 -v ollama:/root/.ollama ollama/ollama`
  e poi `docker exec ollama ollama pull qwen2.5:3b`.
- **Un PC ben dotato**: almeno **8 GB di RAM** (16 consigliati) e un processore recente.
  Senza scheda video dedicata funziona, ma le risposte possono richiedere **decine di
  secondi**; con una GPU NVIDIA sono molto più rapide. Su PC modesti conviene
  semplicemente non usare questa funzione: tutto il resto dell'app ne è indipendente.
- Facoltativo: `ollama pull nomic-embed-text` migliora la ricerca del contesto
  nei documenti lunghi.

**Installazione e configurazione: tutto dall'app.** Menu **Strumenti** →
**Impostazioni**: la finestra verifica se il tuo PC ha i requisiti, e col pulsante
**"Installa e configura tutto automaticamente"** scarica Ollama e il modello al
posto tuo (serve internet solo per questa operazione: dopo, l'AI funziona per
sempre offline). Lì trovi anche l'indirizzo di Ollama con "Prova connessione" —
da cambiare solo se gira su un'altra porta o su un altro PC della rete.

**Usare l'AI anche fuori da TrameEditor.** Il modello installato è tuo:
- **Chat grafica**: apri l'app **Ollama** dal menu Start (o dall'icona vicino all'orologio)
- **Terminale**: `ollama run qwen2.5:3b` (per uscire: `/bye`)
- **Per sviluppatori**: API HTTP su `http://localhost:11434`

**Come funziona:**

- Ogni risposta indica il **punto di provenienza**: clicca "pag. N" (o "riga N") per andarci.
- Regole d'onestà: l'assistente usa **solo il contenuto del documento**, e se non
  trova la risposta lo dice. Ma può comunque sbagliare: **verifica sempre sul documento**.
- Le scansioni vanno prima passate con l'**OCR**.

## Confronta due documenti ⇄

**Menu **Strumenti → Confronta due documenti** (o la scheda Strumenti della barra)**: scegli due versioni e vedi riga per riga
cosa è stato **aggiunto** (verde) e **rimosso** (rosso). Funziona su **PDF, testo e
Markdown**, e anche fra tipi diversi (per esempio un PDF contro il suo sorgente .md):
il confronto guarda il testo, non il formato — e non guarda la grafica.

I riferimenti seguono il tipo di documento: **pagina** per i PDF, **riga** per i file di
testo. Le parti identiche lunghe vengono compresse in "⋯ N righe identiche ⋯", e puoi
salvare un **rapporto HTML** da allegare o archiviare.

## Numeri di pagina, filigrana, intestazioni 🔢

**Menu **Strumenti → Numeri di pagina e filigrana** (o la scheda Strumenti della barra)**. Puoi aggiungere, insieme o separatamente:

- **numeri di pagina** — formato libero (`{n}` è il numero, `{tot}` il totale), posizione a
  scelta, numerazione che può cominciare da un numero diverso e saltare la copertina;
- **filigrana in diagonale** — *COPIA*, *RISERVATO*, *BOZZA*…;
- **intestazione e piè di pagina** — testo fisso in cima e in fondo a ogni pagina.

Le scritte vengono sovrapposte: il contenuto originale non viene toccato, e il risultato
va in una copia. Il carattere usato viene incorporato, quindi il file resta convertibile
in PDF/A.

## Cerca in una cartella di PDF 🔎

**Menu **Modifica → Cerca in una cartella di PDF** (o la scheda Strumenti della barra)**: scegli una cartella e una parola (o un codice
fiscale, un numero di fattura…) e TrameEditor guarda dentro **tutti** i PDF, anche nelle
sottocartelle se lo chiedi. I risultati mostrano file, pagina e la frase intorno; doppio
clic apre il documento a quella pagina.

I file **senza testo** (scansioni non passate dall'OCR) vengono contati a parte e
segnalati: in quelli non è stato possibile cercare, e non sarebbe onesto farli passare per
documenti in cui la parola non c'è.

## Ricette (molti file in una volta) ⚙

**Menu **Strumenti → Ricette** (o la scheda Strumenti della barra)**. Due modi di lavorare a mucchi:

**Ricetta sui PDF** — scegli tanti PDF (o una cartella) e applica a tutti la stessa
sequenza: **OCR → Anonimizza → Comprimi → Converti in PDF/A → Proteggi con password**
(solo i passi che servono). Ogni file riceve un esito dettagliato; i PDF già protetti
vengono saltati e segnalati.

Sul **PDF/A in serie** due cose da sapere. La conversione è solo quella **fedele**:
rasterizzare cinquanta file senza guardarli, perdendo il testo, non è una cosa da fare a
mucchi — i file che non si possono convertire così vengono elencati, e li apri uno per uno
per scegliere. E **PDF/A e password si escludono**: un file cifrato non è un PDF/A, quindi
le due spunte si spengono a vicenda.

**Estrai da file firmati (.p7m)** — metti in una cartella tutti i documenti firmati
digitalmente e tirane fuori in un colpo solo i documenti veri, apribili con qualunque
lettore. Per ogni file viene detto **chi ha firmato e se la firma è integra**. Se dentro
c'è una **fattura elettronica**, oltre all'XML viene salvata anche la sua versione
leggibile in PDF: senza quella otterresti un file illeggibile esattamente come prima.

In entrambi i casi **gli originali non vengono mai toccati**: i risultati finiscono nella
cartella che scegli, e i nomi già presenti non vengono sovrascritti.

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
| Alt+F, Alt+M, Alt+V… | Apre i menu (File, Modifica, Visualizza…) |
| F1 | Questa guida |

---

## Da riga di comando e dagli assistenti AI 🤖

Accanto al programma viene installato **`trameeditor-cli.exe`**: lo stesso TrameEditor senza
finestra, che risponde in JSON. Serve per gli script e perché **un assistente AI possa usarlo
come strumento** — non facendogli premere i pulsanti, ma chiamandolo.

Si trova nella cartella di installazione (di solito `C:\Program Files\TrameEditor\`). Se
durante l'installazione hai scelto *"Aggiungi la cartella al PATH"*, basta scrivere
`trameeditor-cli` in un terminale qualsiasi.

### Provalo a mano

```
trameeditor-cli righe documento.pdf
trameeditor-cli sostituisci documento.pdf corretto.pdf --testo "64" --nuovo "99"
trameeditor-cli anonimizza documento.pdf pulito.pdf --tipi cf,iban
trameeditor-cli firme documento.p7m
trameeditor-cli fattura fattura.xml
```

Il primo comando elenca le righe con un **indice**: è il numero da passare a `sostituisci`.
Nessun comando sovrascrive un file esistente: se serve, si aggiunge `--sovrascrivi`.

### Collegarlo a un assistente

`trameeditor-cli mcp` avvia un **server MCP**, il modo con cui gli assistenti prendono
strumenti esterni. Non apre porte e non manda niente su internet: è un programma che
l'assistente avvia sul tuo computer.

> **Da sapere prima di collegarlo.** Il server resta qui, ma l'assistente no: quello che gli
> fai leggere — il testo di una riga, i dati di una fattura — arriva a lui e al servizio che
> lo fa funzionare. Il file non viene caricato da nessuna parte, ma **il contenuto che
> l'assistente legge esce di casa**. Per i documenti riservati usa `trameeditor-cli` a mano.

**Claude Desktop** — nel file `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "trameeditor": {
      "command": "C:\\Program Files\\TrameEditor\\trameeditor-cli.exe",
      "args": ["mcp"]
    }
  }
}
```

**Claude Code** — da terminale, una riga sola:

```
claude mcp add trameeditor -- "C:\Program Files\TrameEditor\trameeditor-cli.exe" mcp
```

**DeepSeek** — DeepSeek è un modello, non un programma con gli strumenti: serve un'app che
sappia usare l'uno e gli altri. Una che va bene è **Cherry Studio** (gratuita): in
*Impostazioni → Fornitori* scegli DeepSeek e metti la tua chiave, poi in *Impostazioni → MCP*
aggiungi un server di tipo **stdio** con comando `C:\Program Files\TrameEditor\trameeditor-cli.exe`
e argomento `mcp`. Lo stesso vale per gli altri programmi che supportano MCP.

**Codex di OpenAI** (app, CLI o estensione per l'editor) — sì, e con lo stesso meccanismo di
Claude. Nel file `%USERPROFILE%\.codex\config.toml` aggiungi in fondo:

```toml
[mcp_servers.trameeditor]
command = 'C:\Program Files\TrameEditor\trameeditor-cli.exe'
args = ["mcp"]
```

Le tre applicazioni Codex leggono lo stesso file, quindi si configura una volta sola.

**ChatGPT** (il sito e l'app di chat) — **non si collega**, e non è un limite di TrameEditor:
i suoi connettori non partono dal tuo computer ma **dai server di OpenAI**, che vanno a
bussare a un indirizzo pubblico HTTPS. Per loro `127.0.0.1` è la *loro* macchina, non la tua:
far ascoltare TrameEditor su un indirizzo locale non servirebbe a niente. L'unico modo
sarebbe pubblicare il server su internet — cioè rendere i tuoi documenti raggiungibili da
fuori, cosa che è meglio non fare. Con la chat conviene usare `trameeditor-cli` a mano e
incollarle il risultato; se vuoi che sia un programma di OpenAI a lavorare da solo sui file,
quello è Codex.

### Cosa aspettarsi dalle risposte

Ogni comando risponde in JSON e **dice anche quello che non ha potuto fare**: `sostituisci`
dichiara quale carattere ha usato, `anonimizza` elenca le righe che non è riuscito a
ripulire, `firme` ricorda ogni volta che verifica l'integrità e non la validità legale.
Sono le stesse cose che l'applicazione ti direbbe in una finestra: un assistente le finestre
non le vede, quindi gliele diciamo nei dati.

---

## Avviso di versione nuova

TrameEditor lavora **offline**: i tuoi documenti non escono mai dal computer. L'unica cosa che
fa in rete — se glielo permetti — è guardare se è uscita una versione più recente.

- La **prima volta** te lo chiede con una riga in alto: finché non rispondi, non si collega a nulla.
- Se accetti, guarda **al più una volta al giorno**. Quando c'è una versione nuova lo dice con
  la stessa riga, e il pulsante apre la pagina di download nel browser: **scarichi tu, quando vuoi**.
- Non viene inviato niente su di te, sul computer o sui documenti aperti. Al sito arriva il tuo
  indirizzo IP, come quando apri una pagina qualsiasi.
- Puoi cambiare idea quando vuoi da **Strumenti → Impostazioni**, in un senso o nell'altro.

Se la connessione manca, il controllo tace e riprova un altro giorno: non blocca né rallenta
l'avvio del programma.

---

## Informazioni

TrameEditor è un prodotto di **Trame Future srls** — [www.tramefuture.com](https://www.tramefuture.com) —
distribuito con licenza AGPL-3.0. Segnalazioni e suggerimenti sono benvenuti: se un PDF si comporta
male, inviacelo (se puoi condividerlo): ogni caso risolto rende l'app migliore per tutti.
