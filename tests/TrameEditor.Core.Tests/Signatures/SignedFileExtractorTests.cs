using System.Text;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Signatures;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Signatures;

/// <summary>
/// Estrazione in serie dai file firmati: una cartella piena di <c>.p7m</c>
/// diventa una cartella di documenti apribili con qualunque lettore.
/// </summary>
public class SignedFileExtractorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-estrai-").FullName;
    private readonly string _in;
    private readonly string _out;

    public SignedFileExtractorTests()
    {
        _in = Directory.CreateDirectory(Path.Combine(_dir, "firmati")).FullName;
        _out = Path.Combine(_dir, "estratti");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteP7m(string name, byte[] content)
    {
        var identity = SignatureTestFixtures.CreateIdentity("Trame Future srls");
        var path = Path.Combine(_in, name);
        File.WriteAllBytes(path, SignatureTestFixtures.CreateP7m(content, identity));
        return path;
    }

    private byte[] SamplePdf()
    {
        var pdf = Path.Combine(_dir, "originale.pdf");
        MarkdownPdfExporter.ExportPlainText("CONTRATTO DI FORNITURA\n\nImporto: 1.000 euro", "c", pdf);
        return File.ReadAllBytes(pdf);
    }

    private const string InvoiceXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <p:FatturaElettronica xmlns:p="http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2">
          <FatturaElettronicaHeader>
            <DatiTrasmissione><FormatoTrasmissione>FPR12</FormatoTrasmissione>
              <CodiceDestinatario>ABCDEF1</CodiceDestinatario></DatiTrasmissione>
            <CedentePrestatore><DatiAnagrafici>
              <IdFiscaleIVA><IdPaese>IT</IdPaese><IdCodice>04886130618</IdCodice></IdFiscaleIVA>
              <Anagrafica><Denominazione>Trame Future srls</Denominazione></Anagrafica>
              </DatiAnagrafici></CedentePrestatore>
            <CessionarioCommittente><DatiAnagrafici>
              <Anagrafica><Denominazione>Studio Rossi</Denominazione></Anagrafica>
              </DatiAnagrafici></CessionarioCommittente>
          </FatturaElettronicaHeader>
          <FatturaElettronicaBody>
            <DatiGenerali><DatiGeneraliDocumento>
              <TipoDocumento>TD01</TipoDocumento><Divisa>EUR</Divisa>
              <Data>2026-03-03</Data><Numero>2026/17</Numero>
              <ImportoTotaleDocumento>1220.00</ImportoTotaleDocumento>
            </DatiGeneraliDocumento></DatiGenerali>
            <DatiBeniServizi>
              <DettaglioLinee><NumeroLinea>1</NumeroLinea><Descrizione>Consulenza</Descrizione>
                <PrezzoTotale>1000.00</PrezzoTotale><AliquotaIVA>22.00</AliquotaIVA></DettaglioLinee>
              <DatiRiepilogo><AliquotaIVA>22.00</AliquotaIVA>
                <ImponibileImporto>1000.00</ImponibileImporto><Imposta>220.00</Imposta></DatiRiepilogo>
            </DatiBeniServizi>
          </FatturaElettronicaBody>
        </p:FatturaElettronica>
        """;

    [Fact]
    public void Extract_BustaConPdf_SalvaIlDocumentoApribile()
    {
        var p7m = WriteP7m("contratto.pdf.p7m", SamplePdf());

        var result = SignedFileExtractor.Extract(p7m, _out);

        Assert.True(result.Success, result.Outcome);
        var estratto = Assert.Single(result.OutputPaths);
        Assert.Equal("contratto.pdf", Path.GetFileName(estratto));
        Assert.Contains("firmato da Trame Future srls", result.Outcome);

        // Il file estratto è un PDF vero, leggibile.
        using var inspector = new PdfTextInspector(estratto);
        Assert.Contains("CONTRATTO DI FORNITURA",
            string.Join(" ", inspector.GetLines(1).Select(l => l.Text)));
    }

    /// <summary>
    /// Il caso più frequente: dentro la busta c'è una fattura XML. Estrarre solo
    /// l'XML lascerebbe l'utente con un file illeggibile come prima, quindi si
    /// produce anche la trascrizione in PDF.
    /// </summary>
    [Fact]
    public void Extract_BustaConFattura_ProduceAncheIlPdfLeggibile()
    {
        var p7m = WriteP7m("IT04886130618_00042.xml.p7m", Encoding.UTF8.GetBytes(InvoiceXml));

        var result = SignedFileExtractor.Extract(p7m, _out);

        Assert.True(result.Success, result.Outcome);
        Assert.Equal(2, result.OutputPaths.Count);
        Assert.Contains(result.OutputPaths, p => p.EndsWith("IT04886130618_00042.xml"));

        var leggibile = result.OutputPaths.Single(p => p.EndsWith("- leggibile.pdf"));
        using var inspector = new PdfTextInspector(leggibile);
        var testo = string.Join(" ", inspector.GetLines(1).Select(l => l.Text));
        Assert.Contains("2026/17", testo);
        Assert.Contains("Trame Future srls", testo);
    }

    [Fact]
    public void Extract_SenzaTraduzioneDelleFatture_SoloLXml()
    {
        var p7m = WriteP7m("fattura.xml.p7m", Encoding.UTF8.GetBytes(InvoiceXml));

        var result = SignedFileExtractor.Extract(p7m, _out, renderInvoices: false);

        Assert.Single(result.OutputPaths);
        Assert.EndsWith(".xml", result.OutputPaths[0]);
    }

    [Fact]
    public void Extract_NonSovrascriveIFileGiaPresenti()
    {
        var p7m = WriteP7m("contratto.pdf.p7m", SamplePdf());

        var primo = SignedFileExtractor.Extract(p7m, _out);
        var secondo = SignedFileExtractor.Extract(p7m, _out);

        Assert.NotEqual(primo.OutputPaths[0], secondo.OutputPaths[0]);
        Assert.Contains("(2)", Path.GetFileName(secondo.OutputPaths[0]));
        Assert.True(File.Exists(primo.OutputPaths[0]), "il primo file è rimasto dov'era");
    }

    [Fact]
    public void Extract_FileNonValido_NonInterrompeIlLavoro()
    {
        var finto = Path.Combine(_in, "rotto.p7m");
        File.WriteAllText(finto, "questo non e' un p7m");

        var result = SignedFileExtractor.Extract(finto, _out);

        Assert.False(result.Success);
        Assert.Empty(result.OutputPaths);
        Assert.Contains("busta firmata", result.Outcome);
    }

    [Fact]
    public void FindSignedFiles_ElencaSoloIP7mDellaCartella()
    {
        WriteP7m("a.pdf.p7m", SamplePdf());
        WriteP7m("b.pdf.p7m", SamplePdf());
        File.WriteAllText(Path.Combine(_in, "altro.txt"), "niente");

        var trovati = SignedFileExtractor.FindSignedFiles(_in);

        Assert.Equal(2, trovati.Count);
        Assert.All(trovati, f => Assert.EndsWith(".p7m", f));
    }
}
