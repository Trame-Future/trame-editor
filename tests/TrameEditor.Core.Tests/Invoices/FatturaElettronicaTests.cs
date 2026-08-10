using System.Text;
using TrameEditor.Core.Invoices;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Invoices;

/// <summary>
/// Lettura di una fattura elettronica italiana. Le fatture vere arrivano con
/// prefissi di namespace diversi a seconda di chi le emette: la lettura non
/// deve dipendere da quelli.
/// </summary>
public class FatturaElettronicaTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-fattura-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <param name="prefix">Prefisso del namespace, come lo mettono i vari gestionali.</param>
    private static string SampleXml(string prefix = "p:", string? attachmentBase64 = null)
    {
        var p = prefix;
        var ns = prefix.Length == 0
            ? " xmlns=\"http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2\""
            : $" xmlns:{prefix.TrimEnd(':')}=\"http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2\"";

        var allegato = attachmentBase64 is null
            ? string.Empty
            : $"""
                 <Allegati>
                   <NomeAttachment>cortesia.pdf</NomeAttachment>
                   <FormatoAttachment>PDF</FormatoAttachment>
                   <DescrizioneAttachment>Copia di cortesia</DescrizioneAttachment>
                   <Attachment>{attachmentBase64}</Attachment>
                 </Allegati>
               """;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <{p}FatturaElettronica{ns} versione="FPR12">
              <FatturaElettronicaHeader>
                <DatiTrasmissione>
                  <IdTrasmittente><IdPaese>IT</IdPaese><IdCodice>04886130618</IdCodice></IdTrasmittente>
                  <ProgressivoInvio>00042</ProgressivoInvio>
                  <FormatoTrasmissione>FPR12</FormatoTrasmissione>
                  <CodiceDestinatario>ABCDEF1</CodiceDestinatario>
                </DatiTrasmissione>
                <CedentePrestatore>
                  <DatiAnagrafici>
                    <IdFiscaleIVA><IdPaese>IT</IdPaese><IdCodice>04886130618</IdCodice></IdFiscaleIVA>
                    <Anagrafica><Denominazione>Trame Future srls</Denominazione></Anagrafica>
                    <RegimeFiscale>RF01</RegimeFiscale>
                  </DatiAnagrafici>
                  <Sede>
                    <Indirizzo>Via Roma</Indirizzo><NumeroCivico>14</NumeroCivico>
                    <CAP>81052</CAP><Comune>Pignataro Maggiore</Comune>
                    <Provincia>CE</Provincia><Nazione>IT</Nazione>
                  </Sede>
                </CedentePrestatore>
                <CessionarioCommittente>
                  <DatiAnagrafici>
                    <CodiceFiscale>RSSMRA80A01H501U</CodiceFiscale>
                    <Anagrafica><Nome>Mario</Nome><Cognome>Rossi</Cognome></Anagrafica>
                  </DatiAnagrafici>
                  <Sede>
                    <Indirizzo>Via Verdi 3</Indirizzo><CAP>00100</CAP>
                    <Comune>Roma</Comune><Provincia>RM</Provincia><Nazione>IT</Nazione>
                  </Sede>
                </CessionarioCommittente>
              </FatturaElettronicaHeader>
              <FatturaElettronicaBody>
                <DatiGenerali>
                  <DatiGeneraliDocumento>
                    <TipoDocumento>TD01</TipoDocumento>
                    <Divisa>EUR</Divisa>
                    <Data>2026-03-03</Data>
                    <Numero>2026/17</Numero>
                    <ImportoTotaleDocumento>1281.00</ImportoTotaleDocumento>
                    <Causale>Servizi di sviluppo software</Causale>
                  </DatiGeneraliDocumento>
                </DatiGenerali>
                <DatiBeniServizi>
                  <DettaglioLinee>
                    <NumeroLinea>1</NumeroLinea>
                    <Descrizione>Analisi e sviluppo</Descrizione>
                    <Quantita>10.00</Quantita>
                    <UnitaMisura>ore</UnitaMisura>
                    <PrezzoUnitario>100.00</PrezzoUnitario>
                    <PrezzoTotale>1000.00</PrezzoTotale>
                    <AliquotaIVA>22.00</AliquotaIVA>
                  </DettaglioLinee>
                  <DettaglioLinee>
                    <NumeroLinea>2</NumeroLinea>
                    <Descrizione>Rimborso spese documentate</Descrizione>
                    <PrezzoUnitario>50.00</PrezzoUnitario>
                    <PrezzoTotale>50.00</PrezzoTotale>
                    <AliquotaIVA>0.00</AliquotaIVA>
                    <Natura>N1</Natura>
                  </DettaglioLinee>
                  <DatiRiepilogo>
                    <AliquotaIVA>22.00</AliquotaIVA>
                    <ImponibileImporto>1000.00</ImponibileImporto>
                    <Imposta>220.00</Imposta>
                    <EsigibilitaIVA>I</EsigibilitaIVA>
                  </DatiRiepilogo>
                  <DatiRiepilogo>
                    <AliquotaIVA>0.00</AliquotaIVA>
                    <Natura>N1</Natura>
                    <ImponibileImporto>50.00</ImponibileImporto>
                    <Imposta>0.00</Imposta>
                    <RiferimentoNormativo>Escluso art. 15 DPR 633/72</RiferimentoNormativo>
                  </DatiRiepilogo>
                </DatiBeniServizi>
                <DatiPagamento>
                  <CondizioniPagamento>TP02</CondizioniPagamento>
                  <DettaglioPagamento>
                    <ModalitaPagamento>MP05</ModalitaPagamento>
                    <DataScadenzaPagamento>2026-04-02</DataScadenzaPagamento>
                    <ImportoPagamento>1281.00</ImportoPagamento>
                    <IBAN>IT60X0542811101000000123456</IBAN>
                    <Beneficiario>Trame Future srls</Beneficiario>
                  </DettaglioPagamento>
                </DatiPagamento>
            {allegato}
              </FatturaElettronicaBody>
            </{p}FatturaElettronica>
            """;
    }

    [Theory]
    [InlineData("p:")]
    [InlineData("ns2:")]
    [InlineData("")]
    public void Parse_QualunqueSiaIlPrefissoDelNamespace(string prefix)
    {
        var invoice = FatturaElettronicaReader.Parse(SampleXml(prefix));

        Assert.Equal("Trame Future srls", invoice.Fornitore.Nome);
        Assert.Equal("IT04886130618", invoice.Fornitore.PartitaIva);
        Assert.Equal("Mario Rossi", invoice.Cliente.Nome);
        Assert.Equal("RSSMRA80A01H501U", invoice.Cliente.CodiceFiscale);
        Assert.Equal("ABCDEF1", invoice.CodiceDestinatario);

        var documento = Assert.Single(invoice.Documenti);
        Assert.Equal("TD01", documento.TipoDocumento);
        Assert.Equal("2026/17", documento.Numero);
        Assert.Equal(new DateOnly(2026, 3, 3), documento.Data);
        Assert.Equal(1281.00m, documento.ImportoTotale);
    }

    [Fact]
    public void Parse_LeggeRigheRiepilogoEPagamento()
    {
        var documento = FatturaElettronicaReader.Parse(SampleXml()).Documenti[0];

        Assert.Equal(2, documento.Righe.Count);
        Assert.Equal("Analisi e sviluppo", documento.Righe[0].Descrizione);
        Assert.Equal(10m, documento.Righe[0].Quantita);
        Assert.Equal("ore", documento.Righe[0].UnitaMisura);
        Assert.Equal(22m, documento.Righe[0].AliquotaIva);
        Assert.Equal("N1", documento.Righe[1].Natura);

        Assert.Equal(1050m, documento.TotaleImponibile);
        Assert.Equal(220m, documento.TotaleImposta);

        var pagamento = Assert.Single(documento.Pagamenti);
        Assert.Equal("MP05", pagamento.Modalita);
        Assert.Equal(new DateOnly(2026, 4, 2), pagamento.Scadenza);
        Assert.Equal("IT60X0542811101000000123456", pagamento.Iban);
    }

    [Fact]
    public void Parse_EstraeGliAllegati()
    {
        var contenuto = Encoding.UTF8.GetBytes("%PDF-1.7 copia di cortesia");
        var invoice = FatturaElettronicaReader.Parse(
            SampleXml(attachmentBase64: Convert.ToBase64String(contenuto)));

        var allegato = Assert.Single(invoice.Documenti[0].Allegati);
        Assert.Equal("cortesia.pdf", allegato.Nome);
        Assert.Equal("PDF", allegato.Formato);
        Assert.Equal(contenuto, allegato.Data);
    }

    /// <summary>Il totale non è obbligatorio: se manca si ricava dal riepilogo IVA
    /// invece di mostrare zero.</summary>
    [Fact]
    public void TotaleMancante_SiRicavaDalRiepilogo()
    {
        var xml = SampleXml().Replace(
            "<ImportoTotaleDocumento>1281.00</ImportoTotaleDocumento>", string.Empty);

        var documento = FatturaElettronicaReader.Parse(xml).Documenti[0];

        Assert.Null(documento.ImportoTotale);
        Assert.Equal(1270m, documento.TotaleCalcolato); // 1050 imponibile + 220 imposta
    }

    [Fact]
    public void Parse_XmlCheNonEUnaFattura_SpiegaIlProblema()
    {
        var errore = Assert.Throws<InvoiceReadException>(() =>
            FatturaElettronicaReader.Parse("<Documento><Testo>ciao</Testo></Documento>"));

        Assert.Contains("non è una fattura elettronica", errore.Message);
    }

    [Fact]
    public void Parse_XmlRotto_SpiegaIlProblema()
    {
        var errore = Assert.Throws<InvoiceReadException>(() =>
            FatturaElettronicaReader.Parse("<FatturaElettronica><rotto"));

        Assert.Contains("non è un XML leggibile", errore.Message);
    }

    [Fact]
    public void LooksLikeInvoice_RiconosceDalContenutoNonDallEstensione()
    {
        var fattura = Path.Combine(_dir, "IT04886130618_00042.xml");
        File.WriteAllText(fattura, SampleXml());
        var altro = Path.Combine(_dir, "note.xml");
        File.WriteAllText(altro, "<appunti><voce>spesa</voce></appunti>");

        Assert.True(FatturaElettronicaReader.LooksLikeInvoice(fattura));
        Assert.False(FatturaElettronicaReader.LooksLikeInvoice(altro));
    }

    // ----- Resa leggibile -----

    [Fact]
    public void ToMarkdown_TraduceICodiciEMostraIDatiChiave()
    {
        var invoice = FatturaElettronicaReader.Parse(SampleXml());

        var testo = FatturaRenderer.ToMarkdown(invoice, "IT04886130618_00042.xml");

        Assert.Contains("# Fattura (TD01) n. 2026/17", testo);
        Assert.Contains("Vista leggibile", testo);
        Assert.Contains("Trame Future srls", testo);
        Assert.Contains("Mario Rossi", testo);
        Assert.Contains("Analisi e sviluppo", testo);
        Assert.Contains("Bonifico (MP05)", testo);
        Assert.Contains("Regime ordinario (RF01)", testo);
        Assert.Contains("Escluse ex art. 15 (N1)", testo);
        Assert.Contains("Pagamento completo (TP02)", testo);
        Assert.Contains("IVA a esigibilità immediata (I)", testo);
        Assert.Contains("03/03/2026", testo);
        Assert.Contains("1.281,00 EUR", testo);
    }

    /// <summary>Un codice che non conosciamo si mostra com'è: inventare una
    /// descrizione su un documento fiscale sarebbe peggio che non darla.</summary>
    [Fact]
    public void ToMarkdown_CodiceSconosciuto_MostratoComeE()
    {
        var xml = SampleXml().Replace("<TipoDocumento>TD01</TipoDocumento>",
            "<TipoDocumento>TD99</TipoDocumento>");

        var testo = FatturaRenderer.ToMarkdown(FatturaElettronicaReader.Parse(xml));

        Assert.Contains("TD99", testo);
        Assert.DoesNotContain("Fattura (TD99)", testo);
    }
}
