using System.Text;
using TrameEditor.Cli;
using TrameEditor.Cli.Mcp;
using TrameEditor.Core.Pdf;

// TrameEditor senza finestra: gli stessi servizi dell'applicazione, richiamabili da uno
// script o da un agente. Ogni comando stampa JSON su stdout e non chiede mai niente:
// nessuna domanda a cui nessuno risponderebbe.

try
{
    var line = CommandLine.Parse(args);

    // "mcp" non stampa un risultato e non finisce: resta in ascolto sull'ingresso
    // standard finché l'agente che l'ha avviato non chiude. Qui l'uscita standard è il
    // canale del protocollo e la governa il trasporto, che sa già scrivere in UTF-8:
    // non la si tocca.
    if (line.Verb == "mcp")
    {
        await McpHost.RunAsync();
        return (int)ExitCode.Ok;
    }

    // Per i comandi normali invece serve: il JSON deve uscire in UTF-8 anche quando la
    // console sta su una codepage antica, e su Windows ci sta quasi sempre. Senza questo
    // "già" diventa un byte non valido e chi legge non riesce più a interpretare il JSON.
    try
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    catch (IOException)
    {
        // stdout rediretto in un modo che non lo consente: si va avanti lo stesso.
    }

    Console.Out.WriteLine(Output.Serialize(Dispatcher.Run(line)));
    return (int)ExitCode.Ok;
}
catch (UsageException ex)
{
    Console.Out.WriteLine(Output.Error(ex.Message));
    Console.Error.WriteLine(Help.Text);
    return (int)ExitCode.Usage;
}
catch (Exception ex)
{
    // Qualsiasi cosa vada storta esce come JSON, non come traccia di errore. Elencare i tipi
    // previsti non basta: un file che non è un PDF fa lanciare a iText una sua eccezione, che
    // non è la IOException di sistema, e il programma moriva stampando lo stack.
    // Per chi legge — uno script, un agente — uno stack trace è illeggibile quanto il silenzio.
    Console.Out.WriteLine(Output.Error(Messages.Explain(ex)));
    return (int)ExitCode.Failed;
}
