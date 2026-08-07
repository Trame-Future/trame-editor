using TrameEditor.Core.Profile;

namespace TrameEditor.Core.Tests.Profile;

public class FormAutoFillerTests
{
    private static readonly Dictionary<string, string> Profile = new()
    {
        [ProfileKeys.Nome] = "Pietro",
        [ProfileKeys.Cognome] = "Ricciardi",
        [ProfileKeys.CodiceFiscale] = "RCCPTR80A01H501U",
        [ProfileKeys.Email] = "pietro@example.com",
        [ProfileKeys.Citta] = "Pignataro Maggiore",
    };

    private static string? ValueFor(string fieldName) =>
        FormAutoFiller.Match([fieldName], Profile).SingleOrDefault()?.Value;

    [Fact]
    public void Cognome_DoesNotMatchNome()
    {
        Assert.Equal("Ricciardi", ValueFor("Cognome"));
        Assert.Equal("Pietro", ValueFor("Nome"));
        Assert.Equal("Ricciardi", ValueFor("txt_Cognome_1"));
    }

    [Fact]
    public void FieldNamesWithDecorations_AreNormalized()
    {
        Assert.Equal("RCCPTR80A01H501U", ValueFor("txtCodice_Fiscale_2"));
        Assert.Equal("pietro@example.com", ValueFor("E-Mail (contatto)"));
        Assert.Equal("Pignataro Maggiore", ValueFor("Comune di residenza"));
    }

    [Fact]
    public void NomeCompleto_IsComposedWhenMissing()
    {
        Assert.Equal("Pietro Ricciardi", ValueFor("Nominativo del richiedente"));
        Assert.Equal("Pietro Ricciardi", ValueFor("nome_e_cognome"));
    }

    [Fact]
    public void RecognizedFieldWithEmptyProfileValue_ProducesNoProposal_AndNoFallback()
    {
        // "cognome" riconosciuto ma vuoto nel profilo: non deve ricadere su "nome"
        var profile = new Dictionary<string, string> { [ProfileKeys.Nome] = "Pietro" };
        Assert.Empty(FormAutoFiller.Match(["Cognome"], profile));
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        Assert.Empty(FormAutoFiller.Match(["campo_generico_x", "note", "firma"], Profile));
    }
}
