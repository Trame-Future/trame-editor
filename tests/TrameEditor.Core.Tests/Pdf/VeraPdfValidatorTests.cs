using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// La lettura del rapporto di veraPDF. Lo strumento non è incluso nel programma
/// (si installa su richiesta), quindi qui si prova la parte che è nostra:
/// interpretare il verdetto senza sbagliarlo e senza rompersi se la forma del
/// rapporto cambia da una versione all'altra.
/// </summary>
public class VeraPdfValidatorTests
{
    [Fact]
    public void Parse_FileConforme_LoRiconosce()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <report>
              <jobs>
                <job>
                  <validationReport profileName="PDF/A-2U validation profile"
                                    statement="PDF file is compliant with Validation Profile requirements."
                                    isCompliant="true" flavour="PDFA_2_U" />
                </job>
              </jobs>
            </report>
            """;

        var report = VeraPdfValidator.Parse(xml, "2u");

        Assert.NotNull(report);
        Assert.True(report!.IsCompliant);
        Assert.Equal("PDFA_2_U", report.Flavour);
        Assert.Empty(report.Failures);
        Assert.True(report.DidRun);
    }

    [Fact]
    public void Parse_FileNonConforme_ElencaLeRegoleFallite()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <report>
              <jobs>
                <job>
                  <validationReport isCompliant="false" flavour="PDFA_2_B">
                    <details passedRules="140" failedRules="2">
                      <rule specification="ISO 19005-2:2011" clause="6.2.11.4" testNumber="1"
                            status="failed" passedChecks="0" failedChecks="3">
                        <description>All fonts used in a conforming file shall be embedded.</description>
                      </rule>
                      <rule specification="ISO 19005-2:2011" clause="6.6.2" testNumber="1"
                            status="failed" passedChecks="0" failedChecks="1">
                        <description>The document metadata stream shall be present.</description>
                      </rule>
                      <rule clause="6.1.2" testNumber="1" status="passed" passedChecks="12" />
                    </details>
                  </validationReport>
                </job>
              </jobs>
            </report>
            """;

        var report = VeraPdfValidator.Parse(xml, "2b");

        Assert.NotNull(report);
        Assert.False(report!.IsCompliant);
        Assert.Equal(2, report.Failures.Count);
        Assert.Contains(report.Failures, f => f.Contains("6.2.11.4") && f.Contains("embedded"));
        Assert.Contains(report.Failures, f => f.Contains("3 occorrenze"));
        Assert.DoesNotContain(report.Failures, f => f.Contains("6.1.2")); // le regole superate non si elencano
    }

    /// <summary>La forma del rapporto è cambiata fra le versioni di veraPDF:
    /// il verdetto va letto comunque, ovunque si trovi nell'albero.</summary>
    [Fact]
    public void Parse_FormaDiversaDelRapporto_LeggeComunqueIlVerdetto()
    {
        const string xml = """
            <ns:report xmlns:ns="http://www.verapdf.org/MachineReadableReport">
              <ns:batchSummary>
                <ns:validationReports compliant="1" nonCompliant="0" />
              </ns:batchSummary>
              <ns:jobs><ns:job><ns:validationResult isCompliant="true" /></ns:job></ns:jobs>
            </ns:report>
            """;

        var report = VeraPdfValidator.Parse(xml, "2u");

        Assert.NotNull(report);
        Assert.True(report!.IsCompliant);
        Assert.Equal("2u", report.Flavour); // non dichiarato: si tiene quello richiesto
    }

    [Fact]
    public void Parse_RapportoIllegibile_NonInventaUnVerdetto()
    {
        Assert.Null(VeraPdfValidator.Parse("questo non è XML", "2u"));
        Assert.Null(VeraPdfValidator.Parse("", "2u"));
        // XML valido ma senza verdetto: meglio nessuna risposta che una sbagliata.
        Assert.Null(VeraPdfValidator.Parse("<report><jobs /></report>", "2u"));
    }

    [Fact]
    public void Validate_EseguibileInesistente_LoDiceInvecediBocciareIlFile()
    {
        var report = VeraPdfValidator.Validate(
            Path.Combine(Path.GetTempPath(), "verapdf-che-non-esiste.bat"),
            Path.Combine(Path.GetTempPath(), "qualsiasi.pdf"),
            PdfALevel.A2u);

        Assert.False(report.DidRun);
        Assert.NotNull(report.Error);
        // Il file non è stato bocciato: semplicemente non è stato validato.
        Assert.False(report.IsCompliant);
    }

    [Fact]
    public void FindExecutable_SenzaInstallazione_NonTrovaNulla()
    {
        var found = VeraPdfValidator.FindExecutable("percorso\\inesistente\\verapdf.bat");

        // Se veraPDF non è installato su questa macchina deve dire di no,
        // e se lo è deve restituire un file che esiste davvero.
        Assert.True(found is null || File.Exists(found));
    }
}
