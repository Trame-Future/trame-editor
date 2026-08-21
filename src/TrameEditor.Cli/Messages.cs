namespace TrameEditor.Cli;

/// <summary>
/// Traduce in italiano comprensibile gli errori che arrivano dalle librerie. Le loro
/// spiegazioni sono in inglese e pensate per chi le ha scritte: «PDF header not found» è
/// vero ma non dice a nessuno cosa fare. Quando non si sa fare di meglio si riporta il
/// messaggio originale — meglio uno oscuro che uno inventato.
/// </summary>
public static class Messages
{
    public static string Explain(Exception ex)
    {
        var message = ex.Message;

        // Lo stesso problema visto da due librerie diverse: iText dice una cosa, PdfPig
        // un'altra. Per chi legge è lo stesso guaio, e la risposta dev'essere la stessa.
        if (message.Contains("PDF header not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("version header comment", StringComparison.OrdinalIgnoreCase))
            return "Questo file non è un PDF (manca l'intestazione che ogni PDF ha in testa). "
                 + "Controlla il percorso: forse è un file di testo, un'immagine o un documento "
                 + "di un altro tipo.";

        if (message.Contains("Bad user password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("BadPasswordException", StringComparison.OrdinalIgnoreCase))
            return "Il PDF è protetto da password: va aperta prima di poterlo modificare.";

        if (message.Contains("Trailer not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Rebuild failed", StringComparison.OrdinalIgnoreCase))
            return "Il PDF è danneggiato o incompleto: non si riesce a leggerne la struttura.";

        // Il tipo aiuta chi deve capire cosa è successo; il messaggio resta quello vero.
        return $"{message} [{ex.GetType().Name}]";
    }
}
