using TrameEditor.Cli;

namespace TrameEditor.Cli.Tests;

/// <summary>La lettura degli argomenti: nessun disco di mezzo, quindi si può provare tutta.</summary>
public class CommandLineTests
{
    [Fact]
    public void Reads_VerbPositionalsAndOptions()
    {
        var line = CommandLine.Parse(["righe", "documento.pdf", "--pagina", "3"]);

        Assert.Equal("righe", line.Verb);
        Assert.Equal("documento.pdf", line.At(0, "file"));
        Assert.Equal(3, line.IntOr("pagina", 1));
    }

    [Fact]
    public void Accepts_BothFormsOfOption()
    {
        Assert.Equal("3", CommandLine.Parse(["x", "--pagina", "3"]).Value("pagina"));
        Assert.Equal("3", CommandLine.Parse(["x", "--pagina=3"]).Value("pagina"));
    }

    [Fact]
    public void TreatsOptionWithoutValue_AsSwitch()
    {
        var line = CommandLine.Parse(["anonimizza", "a.pdf", "b.pdf", "--metadati", "--tipi", "cf"]);

        Assert.True(line.Has("metadati"));
        Assert.Null(line.Value("metadati"));
        Assert.Equal("cf", line.Value("tipi"));
    }

    [Fact]
    public void Verb_IsCaseInsensitive()
    {
        Assert.Equal("righe", CommandLine.Parse(["RIGHE", "a.pdf"]).Verb);
    }

    [Fact]
    public void EmptyCommandLine_HasNoVerb()
    {
        Assert.Equal(string.Empty, CommandLine.Parse([]).Verb);
    }

    /// <summary>Il testo da scrivere può cominciare con due trattini o essere vuoto: sono
    /// valori, non opzioni. Con --nuovo="" si svuota una riga, che è un uso legittimo.</summary>
    [Fact]
    public void Value_CanBeEmpty_WithEqualsForm()
    {
        var line = CommandLine.Parse(["sostituisci", "a.pdf", "b.pdf", "--nuovo="]);

        Assert.Equal(string.Empty, line.Value("nuovo"));
    }

    [Fact]
    public void MissingRequiredOption_SaysWhichOne()
    {
        var line = CommandLine.Parse(["sostituisci", "a.pdf", "b.pdf"]);

        var error = Assert.Throws<UsageException>(() => line.Required("nuovo"));
        Assert.Contains("--nuovo", error.Message);
    }

    [Fact]
    public void MissingPositional_SaysWhichOne()
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(["righe"]).At(0, "file.pdf"));
        Assert.Contains("file.pdf", error.Message);
    }

    [Fact]
    public void NonNumericOption_IsRejectedWithTheValue()
    {
        var line = CommandLine.Parse(["righe", "a.pdf", "--pagina", "prima"]);

        var error = Assert.Throws<UsageException>(() => line.IntOr("pagina", 1));
        Assert.Contains("prima", error.Message);
    }
}
