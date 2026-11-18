using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Cartella sorvegliata. Il problema vero non è lavorare i file ma capire quando un file è
/// finito di arrivare: una copia da rete o uno scanner creano il file e lo riempiono dopo.
/// </summary>
public class WatchedFolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-sorveglia-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private WatchedFolderLayout Layout() => WatchedFolderLayout.Under(_dir);

    private string Drop(string name, string content = "contenuto")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Lavorazione finta che riesce sempre, per provare il giro senza toccare PDF veri.</summary>
    private static WatchedFileOutcome Succeeds(string source, string outputFolder)
    {
        var produced = Path.Combine(outputFolder, "lavorato-" + Path.GetFileName(source));
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(produced, "risultato");
        return new WatchedFileOutcome(Path.GetFileName(source), true, "fatto", produced, DateTime.UtcNow);
    }

    private static WatchedFileOutcome Fails(string source, string outputFolder) =>
        new(Path.GetFileName(source), false, "non riuscito: documento illeggibile", null, DateTime.UtcNow);

    /// <summary>Senza attesa i test sarebbero lenti: la stabilità si prova a parte.</summary>
    private WatchedFolder Folder(Func<string, string, WatchedFileOutcome> process) =>
        new(Layout(), process) { StableFor = TimeSpan.Zero };

    [Fact]
    public void ProcessesThePdfsItFinds()
    {
        Drop("uno.pdf");
        Drop("due.pdf");

        var outcomes = Folder(Succeeds).ProcessReady();

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.True(File.Exists(Path.Combine(_dir, "fatti", "uno.pdf")));
    }

    [Fact]
    public void IgnoresWhatIsNotAPdf()
    {
        Drop("documento.txt");
        Drop("foglio.xlsx");

        Assert.Empty(Folder(Succeeds).ProcessReady());
    }

    [Fact]
    public void AFailedFileGoesToItsOwnFolder()
    {
        Drop("rotto.pdf");

        var outcomes = Folder(Fails).ProcessReady();

        Assert.False(outcomes[0].Success);
        Assert.True(File.Exists(Path.Combine(_dir, "non riusciti", "rotto.pdf")));
        Assert.False(File.Exists(Path.Combine(_dir, "fatti", "rotto.pdf")));
    }

    /// <summary>Un errore imprevisto nella lavorazione non deve fermare la sorveglianza né
    /// perdere il file: diventa un esito negativo come gli altri.</summary>
    [Fact]
    public void AnUnexpectedErrorBecomesAnOutcome_NotACrash()
    {
        Drop("esplosivo.pdf");

        var outcomes = Folder((_, _) => throw new InvalidOperationException("scoppiato"))
            .ProcessReady();

        Assert.False(outcomes[0].Success);
        Assert.Contains("scoppiato", outcomes[0].Outcome);
        Assert.True(File.Exists(Path.Combine(_dir, "non riusciti", "esplosivo.pdf")));
    }

    /// <summary>Lo stesso nome può tornare domani: la copia di ieri non va persa.</summary>
    [Fact]
    public void TheSameNameTwice_DoesNotOverwrite()
    {
        Drop("ricorrente.pdf", "primo");
        Folder(Succeeds).ProcessReady();
        Drop("ricorrente.pdf", "secondo");

        Folder(Succeeds).ProcessReady();

        Assert.True(File.Exists(Path.Combine(_dir, "fatti", "ricorrente.pdf")));
        Assert.True(File.Exists(Path.Combine(_dir, "fatti", "ricorrente (2).pdf")));
        Assert.Equal("primo", File.ReadAllText(Path.Combine(_dir, "fatti", "ricorrente.pdf")));
    }

    /// <summary>Un file che sta ancora crescendo non è pronto: una copia da rete crea il file
    /// e lo riempie dopo.</summary>
    [Fact]
    public void AFileStillGrowing_IsNotReady()
    {
        var path = Drop("in-arrivo.pdf", "parziale");
        var sizes = new Queue<long>([10, 20]); // seconda misura più grande della prima

        var ready = new WatchedFolder(Layout(), Succeeds) { StableFor = TimeSpan.Zero }
            .ReadyFiles(_ => sizes.Dequeue());

        Assert.Empty(ready);
        Assert.True(File.Exists(path)); // resta lì, si riguarda al giro dopo
    }

    [Fact]
    public void AnEmptyFile_IsNotReady()
    {
        Drop("vuoto.pdf", string.Empty);

        Assert.Empty(Folder(Succeeds).ReadyFiles());
    }

    /// <summary>Un file ancora aperto da chi lo scrive non si tocca.</summary>
    [Fact]
    public void AFileHeldOpenByTheWriter_IsNotReady()
    {
        var path = Drop("in-scrittura.pdf");
        using var held = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);

        Assert.Empty(Folder(Succeeds).ReadyFiles());
    }

    [Fact]
    public void TheLogKeepsOneLinePerFile()
    {
        Drop("uno.pdf");
        Drop("due.pdf");

        Folder(Succeeds).ProcessReady();

        var lines = File.ReadAllLines(Path.Combine(_dir, "registro.csv"));
        Assert.Equal(3, lines.Length); // intestazione + due file
        Assert.StartsWith("Quando;File;Esito", lines[0].TrimStart('﻿'));
        // I file si lavorano in ordine alfabetico, quindi "due" prima di "uno": l'ordine è
        // voluto, perché un registro che cambia ordine a ogni giro non si confronta con
        // quello di ieri.
        Assert.Contains("due.pdf", lines[1]);
        Assert.Contains("uno.pdf", lines[2]);
    }

    [Fact]
    public void TheLogGrowsAcrossRuns()
    {
        Drop("primo.pdf");
        Folder(Succeeds).ProcessReady();
        Drop("secondo.pdf");

        Folder(Succeeds).ProcessReady();

        Assert.Equal(3, File.ReadAllLines(Path.Combine(_dir, "registro.csv")).Length);
    }

    [Fact]
    public void AnAlreadyProcessedFolder_HasNothingToDo()
    {
        Drop("uno.pdf");
        Folder(Succeeds).ProcessReady();

        Assert.Empty(Folder(Succeeds).ProcessReady());
    }

    [Fact]
    public void TheWorkingFoldersAreCreatedIfMissing()
    {
        Drop("uno.pdf");

        Folder(Succeeds).ProcessReady();

        Assert.True(Directory.Exists(Path.Combine(_dir, "fatti")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "non riusciti")));
    }
}
