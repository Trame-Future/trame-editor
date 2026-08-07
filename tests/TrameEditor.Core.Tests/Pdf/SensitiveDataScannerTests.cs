using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

public class SensitiveDataScannerTests
{
    private static PdfTextLine LineOf(string text) =>
        new(1, text, 50, 700, 400, 12, 50, 700, "Arial", 11, 0, 0, 0, true, null);

    private static IReadOnlyList<SensitiveMatch> Scan(string text) =>
        SensitiveDataScanner.ScanLine(LineOf(text));

    [Fact]
    public void Finds_CodiceFiscale()
    {
        var matches = Scan("Codice fiscale: RSSMRA80A01H501U del richiedente");
        var match = Assert.Single(matches);
        Assert.Equal(SensitiveKind.CodiceFiscale, match.Kind);
        Assert.Equal("RSSMRA80A01H501U", match.Value);
    }

    [Fact]
    public void Finds_Iban_AlsoWithSpaces()
    {
        var compact = Assert.Single(Scan("IBAN: IT60X0542811101000000123456"));
        Assert.Equal(SensitiveKind.Iban, compact.Kind);

        var spaced = Assert.Single(Scan("Accredito su IT 60 X054 2811 1010 0000 0123 456 intestato"));
        Assert.Equal(SensitiveKind.Iban, spaced.Kind);
        Assert.Contains("IT 60", spaced.Value);
    }

    [Fact]
    public void Finds_Email_Telefono_Targa()
    {
        var matches = Scan("Contatti: mario.rossi@example.com, cell. 353 375 5498, auto CX847MN");
        Assert.Equal(3, matches.Count);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Email && m.Value == "mario.rossi@example.com");
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Telefono);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Targa && m.Value == "CX847MN");
    }

    [Fact]
    public void Ignores_Dates_Caps_And_PlainWords()
    {
        Assert.Empty(Scan("Pignataro Maggiore, 07/08/2026 — CAP 81052, protocollo n. 1234"));
    }

    [Fact]
    public void CodiceFiscale_IsNotAlsoReportedAsTarga()
    {
        // dentro un CF ci sono sequenze simili a targhe: non devono sovrapporsi
        var matches = Scan("CF RSSMRA80A01H501U");
        Assert.Single(matches);
        Assert.Equal(SensitiveKind.CodiceFiscale, matches[0].Kind);
    }

    [Fact]
    public void AlreadyMaskedValues_AreNotDetectedAgain()
    {
        Assert.Empty(Scan("email XXXXX@XXXXXXX.XXX già anonimizzata"));
    }

    [Fact]
    public void MaskLine_MasksOnlyMatchedRanges_PreservingSpaces()
    {
        var line = LineOf("Codice fiscale: RSSMRA80A01H501U e telefono 353 375 5498");
        var matches = SensitiveDataScanner.ScanLine(line);

        var masked = SensitiveDataScanner.MaskLine(line.Text, matches);

        Assert.StartsWith("Codice fiscale: XXXXXXXXXXXXXXXX", masked);
        Assert.Contains("telefono XXX XXX XXXX", masked);
        Assert.Equal(line.Text.Length, masked.Length);
        Assert.DoesNotContain("RSSMRA", masked);
        Assert.DoesNotContain("5498", masked);
    }
}
