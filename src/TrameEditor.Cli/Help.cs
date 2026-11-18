namespace TrameEditor.Cli;

/// <summary>Il promemoria d'uso, su stderr per non sporcare il JSON di stdout.</summary>
public static class Help
{
    public const string Text = """
        trameeditor-cli — TrameEditor senza finestra. Ogni comando stampa JSON su stdout.

          righe <file.pdf> [--pagina N] [--tutte] [--solo-modificabili]
              Elenca le righe di testo con posizione e se sono modificabili.
              Da qui si prende l'indice da passare a "sostituisci".

          tabella <file.pdf> [<destinazione.csv|.xlsx>] [--pagina N] [--tutte]
                  [--separatore ";"] [--sovrascrivi]
              Riconosce le tabelle e le restituisce, o le salva in CSV/Excel.

          sorveglia <cartella> (--anonimizza | --comprimi | --pdfa | --proteggi <pwd>)
              Lavora una volta i PDF pronti nella cartella e li sposta in fatti/non riusciti.

          fascicolo <fascicolo.pdf> <doc1.pdf> <doc2.pdf> ... | --cartella <percorso>
                    [--senza-indice] [--titolo-indice "<testo>"] [--sovrascrivi]
              Unisce con segnalibri e indice cliccabile.

          trova <file.pdf> [<destinazione.pdf>] --cerca "<testo>" [--nuovo "<testo>"]
                [--maiuscole] [--parola-intera] [--sovrascrivi]
              Cerca in tutte le pagine; con la destinazione sostituisce.

          moduli <modulo.pdf> <dati.csv|.xlsx> <cartella> [--nome-da <colonna>]
                 [--appiattisci] [--sovrascrivi]
              Compila il modulo una volta per riga della tabella.

          estrai <file.pdf|cartella> [<destinazione.csv|.xlsx>] --modello <modello.json>
                 [--sottocartelle] [--sovrascrivi]
              Legge i campi segnati in un modello da uno o più documenti uguali.

          dividi <file.pdf> <cartella> (--modello <m.json> [--campo <nome>] | --area "l,b,w,h")
                 [--prefisso <testo>] [--sovrascrivi]
              Divide una stampa cumulativa quando cambia il valore letto in un'area.

          sostituisci <origine.pdf> <destinazione.pdf> --nuovo "<testo>"
                      (--riga <indice> | --testo "<testo esatto>") [--pagina N] [--sovrascrivi]
              Riscrive una riga. Dichiara sempre quale carattere è stato usato.

          anonimizza <origine.pdf> <destinazione.pdf> [--tipi cf,iban,email,telefono,targa]
                     [--metadati] [--sovrascrivi]
              Toglie davvero i dati personali dal contenuto, non li copre.

          firme <file.pdf|.p7m> [--estrai <cartella>]
              Chi ha firmato e se il documento è stato alterato dopo la firma.

          fattura <file.xml|.p7m> [--allegati <cartella>]
              Legge una fattura elettronica FatturaPA.

        Uscita: 0 fatto, 1 argomenti sbagliati, 2 il documento non si è lasciato fare.
        """;
}
