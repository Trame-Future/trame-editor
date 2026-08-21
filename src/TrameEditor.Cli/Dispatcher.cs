using TrameEditor.Cli.Commands;

namespace TrameEditor.Cli;

/// <summary>
/// L'elenco dei comandi, in un posto solo: lo usano sia la riga di comando sia il server
/// MCP, così non esistono due liste che possono divergere.
/// </summary>
public static class Dispatcher
{
    public static object Run(CommandLine line) => line.Verb switch
    {
        "righe" => LinesCommand.Run(line),
        "sostituisci" => ReplaceCommand.Run(line),
        "anonimizza" => RedactCommand.Run(line),
        "firme" => SignaturesCommand.Run(line),
        "fattura" => InvoiceCommand.Run(line),
        "" => throw new UsageException("Manca il comando."),
        _ => throw new UsageException($"Comando sconosciuto: «{line.Verb}»."),
    };
}
