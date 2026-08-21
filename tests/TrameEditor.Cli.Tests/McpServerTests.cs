using System.Diagnostics;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace TrameEditor.Cli.Tests;

/// <summary>
/// Il server MCP provato come lo userebbe un agente: si avvia il programma, si parla
/// JSON-RPC sull'ingresso standard e si legge la risposta dall'uscita.
/// <para>
/// La pipe va tenuta aperta: se l'ingresso finisce subito il server chiude prima di
/// scrivere, e sembra che non risponda — un tranello che è costato un giro di indagine
/// quando lo si provava mandandogli un file.
/// </para>
/// </summary>
public class McpServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-mcp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static McpServerTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new("Arial#");
    }

    /// <summary>Un dialogo con il server: si apre, si parla, si chiude.</summary>
    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private int _id;

        public Session()
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "trameeditor-cli.exe");
            Assert.True(File.Exists(exe), $"trameeditor-cli.exe non è accanto ai test: {exe}");

            _process = Process.Start(new ProcessStartInfo(exe, "mcp")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;

            Send("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
            ReadLine();
            Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        }

        private void Send(string message)
        {
            _process.StandardInput.WriteLine(message);
            _process.StandardInput.Flush();
        }

        private string ReadLine()
        {
            var read = Task.Run(() => _process.StandardOutput.ReadLine());
            Assert.True(read.Wait(TimeSpan.FromSeconds(30)), "il server non ha risposto entro 30 secondi");
            return read.Result ?? throw new InvalidOperationException("il server ha chiuso l'uscita");
        }

        public JsonElement Request(string method, string paramsJson = "{}")
        {
            var id = ++_id;
            Send($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""");
            return JsonDocument.Parse(ReadLine()).RootElement.GetProperty("result");
        }

        /// <summary>Il testo che l'agente riceve davvero: il JSON del comando, annidato
        /// dentro la risposta del protocollo.</summary>
        public JsonElement CallTool(string name, string argumentsJson)
        {
            var result = Request("tools/call",
                $$"""{"name":"{{name}}","arguments":{{argumentsJson}}}""");
            var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000))
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // già uscito
            }
            _process.Dispose();
        }
    }

    private string SamplePdf()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString("MELINDA 75/80                64                10",
            new XFont("Arial", 12), XBrushes.Black, new XPoint(50, 100));
        document.Save(path);
        return path;
    }

    private static string Json(string path) => JsonSerializer.Serialize(path);

    [Fact]
    public void Offers_TheFiveTools_WithDescriptions()
    {
        using var session = new Session();

        var tools = session.Request("tools/list").GetProperty("tools");

        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(
            ["anonimizza", "fattura", "firme", "righe", "sostituisci"],
            names.Order().ToList());
        // Senza descrizione un agente non sa quando usare uno strumento: vale come il codice.
        Assert.All(tools.EnumerateArray(), tool =>
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString())));
    }

    [Fact]
    public void Lines_ThenReplace_IsTheWholeRoundTrip()
    {
        var pdf = SamplePdf();
        var target = Path.Combine(_dir, "corretto.pdf");
        using var session = new Session();

        var lines = session.CallTool("righe", $$"""{"file":{{Json(pdf)}}}""");
        Assert.True(lines.GetProperty("ok").GetBoolean());
        var row = lines.GetProperty("righe").EnumerateArray()
            .First(r => r.GetProperty("testo").GetString() == "64");

        // Su una riga sola: nel protocollo un messaggio è una riga, e un a capo qui dentro
        // lo spezzerebbe in due messaggi rotti.
        var replaced = session.CallTool("sostituisci", $$"""{"file":{{Json(pdf)}},"destinazione":{{Json(target)}},"indiceRiga":{{row.GetProperty("indice").GetInt32()}},"testoNuovo":"99"}""");

        Assert.True(replaced.GetProperty("ok").GetBoolean());
        Assert.Equal("64", replaced.GetProperty("testoPrecedente").GetString());
        // Quale font è stato usato deve arrivare all'agente: non ci sono finestre da leggere.
        Assert.True(replaced.GetProperty("carattere").TryGetProperty("originaleRiusato", out _));
        Assert.True(File.Exists(target));
    }

    /// <summary>Un errore previsto torna come JSON leggibile, non come guasto del
    /// protocollo: così l'agente può correggersi da solo.</summary>
    [Fact]
    public void PredictableError_ComesBackAsReadableJson()
    {
        using var session = new Session();

        var result = session.CallTool("righe", """{"file":"C:\\non\\esiste.pdf"}""");

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("non esiste", result.GetProperty("errore").GetString()!);
    }

    [Fact]
    public void Replace_DoesNotOverwriteByItself_EvenWhenAskedByAnAgent()
    {
        var pdf = SamplePdf();
        var target = Path.Combine(_dir, "occupato.pdf");
        File.WriteAllText(target, "roba da non perdere");
        using var session = new Session();

        var result = session.CallTool("sostituisci", $$"""{"file":{{Json(pdf)}},"destinazione":{{Json(target)}},"testoEsatto":"64","testoNuovo":"99"}""");

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("sovrascrivi", result.GetProperty("errore").GetString()!);
        Assert.Equal("roba da non perdere", File.ReadAllText(target));
    }
}
