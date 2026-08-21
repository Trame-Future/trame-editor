namespace TrameEditor.Cli;

/// <summary>Il promemoria d'uso, su stderr per non sporcare il JSON di stdout.</summary>
public static class Help
{
    public const string Text = """
        trameeditor-cli — TrameEditor senza finestra. Ogni comando stampa JSON su stdout.

          righe <file.pdf> [--pagina N] [--tutte] [--solo-modificabili]
              Elenca le righe di testo con posizione e se sono modificabili.
              Da qui si prende l'indice da passare a "sostituisci".

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
