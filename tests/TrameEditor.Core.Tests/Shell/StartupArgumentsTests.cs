using TrameEditor.Core.Shell;
using Xunit;

namespace TrameEditor.Core.Tests.Shell;

public class StartupArgumentsTests
{
    [Fact]
    public void Senza_argomenti_si_apre_e_basta()
    {
        var request = StartupArguments.Parse([]);

        Assert.Equal(StartupVerb.Open, request.Verb);
        Assert.Empty(request.Paths);
        Assert.Null(request.FirstPath);
    }

    [Fact]
    public void Un_percorso_solo_viene_aperto()
    {
        var request = StartupArguments.Parse([@"C:\documenti\contratto.pdf"]);

        Assert.Equal(StartupVerb.Open, request.Verb);
        Assert.Equal(@"C:\documenti\contratto.pdf", request.FirstPath);
    }

    [Theory]
    [InlineData("--pdfa", StartupVerb.ConvertToPdfA)]
    [InlineData("--anonimizza", StartupVerb.Redact)]
    [InlineData("--cerca", StartupVerb.SearchFolder)]
    [InlineData("--estrai-firmati", StartupVerb.ExtractSigned)]
    [InlineData("--apri", StartupVerb.Open)]
    public void Le_opzioni_scelgono_l_azione(string option, StartupVerb atteso)
    {
        var request = StartupArguments.Parse([option, @"C:\cartella\file.pdf"]);

        Assert.Equal(atteso, request.Verb);
        Assert.Equal(@"C:\cartella\file.pdf", request.FirstPath);
    }

    [Fact]
    public void L_ordine_non_conta()
    {
        var request = StartupArguments.Parse([@"C:\a.pdf", "--pdfa"]);

        Assert.Equal(StartupVerb.ConvertToPdfA, request.Verb);
        Assert.Equal(@"C:\a.pdf", request.FirstPath);
    }

    [Fact]
    public void Un_opzione_sconosciuta_non_impedisce_di_aprire_i_file()
    {
        var request = StartupArguments.Parse(["--boh", @"C:\a.pdf"]);

        Assert.Equal(StartupVerb.Open, request.Verb);
        Assert.Equal(@"C:\a.pdf", request.FirstPath);
    }

    [Fact]
    public void I_percorsi_arrivano_senza_virgolette_e_senza_vuoti()
    {
        var request = StartupArguments.Parse(["  ", "\"C:\\due parole\\a.pdf\"", @"C:\b.pdf"]);

        Assert.Equal([@"C:\due parole\a.pdf", @"C:\b.pdf"], request.Paths);
    }

    [Fact]
    public void SwitchFor_e_l_inverso_di_Parse()
    {
        foreach (var verb in Enum.GetValues<StartupVerb>())
        {
            var option = StartupArguments.SwitchFor(verb);
            var argomenti = option.Length == 0 ? new[] { @"C:\a.pdf" } : [option, @"C:\a.pdf"];

            Assert.Equal(verb, StartupArguments.Parse(argomenti).Verb);
        }
    }
}
