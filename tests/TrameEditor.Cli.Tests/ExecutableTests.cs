using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TrameEditor.Cli.Tests;

/// <summary>
/// Il programma eseguito davvero, non le sue classi: quello che esce da stdout e il numero
/// con cui termina. Serve perché qui è finito un difetto che i test sulle classi non
/// vedevano — un'eccezione di iText che sfuggiva al filtro delle eccezioni previste e
/// faceva morire il programma stampando la traccia di stack.
/// Successo dando in pasto a "firme" il LICENSE.txt della cartella di installazione.
/// </summary>
public class ExecutableTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-exe-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "trameeditor-cli.exe");
        Assert.True(File.Exists(exe), $"trameeditor-cli.exe non è accanto ai test: {exe}");

        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in args)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "il programma non è terminato entro un minuto");
        return (process.ExitCode, output, error);
    }

    private static JsonElement Parse(string stdout) => JsonDocument.Parse(stdout).RootElement.Clone();

    [Fact]
    public void AFileThatIsNotAPdf_EndsWithReadableJson_NotAStackTrace()
    {
        var notAPdf = Path.Combine(_dir, "non-e-un-pdf.txt");
        File.WriteAllText(notAPdf, "Questo è testo, non un PDF.");

        var (exitCode, stdout, stderr) = Run("righe", notAPdf);

        Assert.Equal(2, exitCode);
        var result = Parse(stdout);
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("non è un PDF", result.GetProperty("errore").GetString()!);
        // La traccia di stack non deve comparire da nessuna parte: chi legge è un programma.
        Assert.DoesNotContain("Unhandled exception", stderr);
        Assert.DoesNotContain("at iText", stderr);
    }

    [Fact]
    public void NoCommand_ExplainsItself_AndKeepsJsonSeparateFromHelp()
    {
        var (exitCode, stdout, stderr) = Run();

        Assert.Equal(1, exitCode);
        Assert.False(Parse(stdout).GetProperty("ok").GetBoolean());
        // L'aiuto va su stderr: stdout deve restare JSON puro, o chi lo legge si perde.
        Assert.Contains("righe <file.pdf>", stderr);
    }

    /// <summary>Gli accenti devono arrivare interi: la console di Windows sta su una codepage
    /// antica e senza forzare UTF-8 «già» diventa un byte che rompe la lettura del JSON.</summary>
    [Fact]
    public void Accents_SurviveTheConsole()
    {
        var (_, stdout, _) = Run("righe", Path.Combine(_dir, "manca.pdf"));

        Assert.Contains("Il file non esiste", Parse(stdout).GetProperty("errore").GetString()!);
        Assert.DoesNotContain("�", stdout); // il carattere di sostituzione: segno di codifica rotta
    }

    [Theory]
    [InlineData("PDF header not found", "non è un PDF")]
    [InlineData("Could not find the version header comment", "non è un PDF")]
    [InlineData("Bad user password", "protetto da password")]
    [InlineData("Trailer not found", "danneggiato")]
    public void KnownLibraryErrors_AreTranslated(string original, string expected)
    {
        Assert.Contains(expected, Messages.Explain(new InvalidOperationException(original)));
    }

    [Fact]
    public void UnknownError_KeepsTheOriginalMessage_AndSaysItsType()
    {
        var explained = Messages.Explain(new InvalidOperationException("qualcosa di mai visto"));

        // Meglio un messaggio oscuro ma vero che uno inventato.
        Assert.Contains("qualcosa di mai visto", explained);
        Assert.Contains("InvalidOperationException", explained);
    }
}
