using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TrameEditor.Cli.Mcp;

/// <summary>
/// Il server MCP: è così che gli agenti prendono strumenti esterni. Parla sull'ingresso e
/// sull'uscita standard, quindi non apre nessuna porta e non esce dal computer — resta un
/// programma che l'agente avvia, come <c>trameeditor</c> a mano.
/// </summary>
public static class McpHost
{
    private static string Version =>
        typeof(McpHost).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static async Task RunAsync(CancellationToken token = default)
    {
        var builder = Host.CreateApplicationBuilder();

        // stdout è il canale del protocollo: un solo messaggio di log finito lì
        // renderebbe illeggibile tutto il dialogo. I log vanno su stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer(options =>
            {
                // La versione si legge dall'assembly: scritta a mano si disallineerebbe al
                // primo rilascio, e l'agente si vedrebbe dichiarare una versione che non è.
                options.ServerInfo = new() { Name = "trameeditor", Version = Version };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync(token);
    }
}
