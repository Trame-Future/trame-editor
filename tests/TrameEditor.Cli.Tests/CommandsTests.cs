using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Cli.Commands;

namespace TrameEditor.Cli.Tests;

/// <summary>
/// I comandi su un PDF vero, generato qui: è il giro che farebbe un agente — prima
/// <c>righe</c> per vedere cosa c'è, poi <c>sostituisci</c> sull'indice che ha letto.
/// La riga di prova è a colonne, la forma dei documenti dei gestionali.
/// </summary>
public class CommandsTests : IDisposable
{
    private const string Row = "MELINDA 75/80                64                10";

    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static CommandsTests()
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

    private string SamplePdf()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        gfx.DrawString(Row, font, XBrushes.Black, new XPoint(50, 100));
        gfx.DrawString("Mario Rossi - RSSMRA80A01H501U", font, XBrushes.Black, new XPoint(50, 130));
        document.Save(path);
        return path;
    }

    private string Target(string name) => Path.Combine(_dir, name);

    private static IDictionary<string, object?> Run(params string[] args)
    {
        var line = CommandLine.Parse(args);
        object payload = line.Verb switch
        {
            "righe" => LinesCommand.Run(line),
            "sostituisci" => ReplaceCommand.Run(line),
            "anonimizza" => RedactCommand.Run(line),
            _ => throw new InvalidOperationException($"comando non previsto nel test: {line.Verb}"),
        };
        return (IDictionary<string, object?>)payload;
    }

    private static List<IDictionary<string, object?>> Rows(IDictionary<string, object?> result) =>
        ((IEnumerable<object>)result["righe"]!).Cast<IDictionary<string, object?>>().ToList();

    [Fact]
    public void Lines_ListsTheColumnsWithStableIndexes()
    {
        var pdf = SamplePdf();

        var rows = Rows(Run("righe", pdf));

        var texts = rows.Select(r => (string)r["testo"]!).ToList();
        Assert.Contains("MELINDA 75/80", texts);
        Assert.Contains("64", texts);
        Assert.Contains("10", texts);
        // L'indice è la chiave con cui l'agente tornerà a chiedere la modifica.
        Assert.Equal(Enumerable.Range(0, rows.Count), rows.Select(r => (int)r["indice"]!));
        Assert.All(rows, r => Assert.True((bool)r["modificabile"]!));
    }

    [Fact]
    public void Replace_ByText_ChangesOnlyThatColumn()
    {
        var pdf = SamplePdf();
        var target = Target("uscita.pdf");

        var result = Run("sostituisci", pdf, target, "--testo", "64", "--nuovo", "99");

        Assert.True((bool)result["ok"]!);
        Assert.Equal("64", result["testoPrecedente"]);
        var after = Rows(Run("righe", target)).Select(r => (string)r["testo"]!).ToList();
        Assert.Contains("99", after);
        Assert.Contains("MELINDA 75/80", after);
        Assert.Contains("10", after);
    }

    [Fact]
    public void Replace_ByIndex_UsesTheNumberFromLines()
    {
        var pdf = SamplePdf();
        var rows = Rows(Run("righe", pdf));
        var index = rows.First(r => (string)r["testo"]! == "10")["indice"]!.ToString()!;

        var result = Run("sostituisci", pdf, Target("uscita.pdf"), "--riga", index, "--nuovo", "25");

        Assert.Equal("10", result["testoPrecedente"]);
        Assert.Equal("25", result["testoNuovo"]);
    }

    /// <summary>Quale carattere è stato usato deve stare nell'esito: se il font originale
    /// non avesse i glifi richiesti il risultato si vedrebbe diverso, e un programma non
    /// legge le finestre di avviso.</summary>
    [Fact]
    public void Replace_DeclaresWhichFontWasUsed()
    {
        var pdf = SamplePdf();

        var result = Run("sostituisci", pdf, Target("uscita.pdf"), "--testo", "64", "--nuovo", "99");

        var font = (IDictionary<string, object?>)result["carattere"]!;
        Assert.False(string.IsNullOrWhiteSpace((string)font["descrizione"]!));
        Assert.NotNull(font["strategia"]);
        Assert.IsType<bool>(font["originaleRiusato"]);
    }

    [Fact]
    public void Replace_RefusesToOverwrite_UnlessAsked()
    {
        var pdf = SamplePdf();
        var target = Target("uscita.pdf");
        File.WriteAllText(target, "contenuto da non perdere");

        var error = Assert.Throws<UsageException>(() =>
            Run("sostituisci", pdf, target, "--testo", "64", "--nuovo", "99"));

        Assert.Contains("--sovrascrivi", error.Message);
        Assert.Equal("contenuto da non perdere", File.ReadAllText(target));

        Run("sostituisci", pdf, target, "--testo", "64", "--nuovo", "99", "--sovrascrivi");
        Assert.NotEqual("contenuto da non perdere", File.ReadAllText(target));
    }

    [Fact]
    public void Replace_WithAmbiguousText_StopsAndListsTheIndexes()
    {
        var pdf = Path.Combine(_dir, "doppio.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString("64", font, XBrushes.Black, new XPoint(50, 100));
            gfx.DrawString("64", font, XBrushes.Black, new XPoint(50, 130));
            document.Save(pdf);
        }

        var error = Assert.Throws<UsageException>(() =>
            Run("sostituisci", pdf, Target("uscita.pdf"), "--testo", "64", "--nuovo", "99"));

        Assert.Contains("2 volte", error.Message);
        Assert.Contains("--riga", error.Message);
    }

    [Fact]
    public void Lines_RejectsAPageThatDoesNotExist()
    {
        var error = Assert.Throws<UsageException>(() => Run("righe", SamplePdf(), "--pagina", "7"));

        Assert.Contains("non esiste", error.Message);
    }

    [Fact]
    public void Redact_RemovesTheDataAndSaysWhatWasLeftBehind()
    {
        var pdf = SamplePdf();
        var target = Target("pulito.pdf");

        var result = Run("anonimizza", pdf, target, "--tipi", "cf");

        Assert.True((bool)result["ok"]!);
        Assert.True((int)result["rimossi"]! >= 1);
        // "completo" dice se è rimasto fuori qualcosa: è l'informazione che rende
        // fidato l'esito di uno strumento di anonimizzazione.
        Assert.True((bool)result["completo"]!);
        var after = Rows(Run("righe", target)).Select(r => (string)r["testo"]!);
        Assert.DoesNotContain(after, t => t.Contains("RSSMRA80A01H501U"));
    }

    [Fact]
    public void Redact_RejectsAnUnknownKind()
    {
        var error = Assert.Throws<UsageException>(() =>
            Run("anonimizza", SamplePdf(), Target("x.pdf"), "--tipi", "passaporto"));

        Assert.Contains("passaporto", error.Message);
    }

    /// <summary>L'uscita deve essere JSON valido con gli accenti veri: chi legge è un
    /// programma, e "già" o un byte rotto gli rovinano la lettura.</summary>
    [Fact]
    public void Output_IsValidJson_WithRealAccents()
    {
        var json = Output.Serialize(Run("righe", SamplePdf()));

        using var parsed = JsonDocument.Parse(json);
        Assert.True(parsed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("à", Output.Error("Il file esiste già: prova."));
        Assert.DoesNotContain("\\u00e0", Output.Error("Il file esiste già: prova."));
    }
}
