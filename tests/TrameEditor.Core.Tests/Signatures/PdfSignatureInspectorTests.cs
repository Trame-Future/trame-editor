using iText.Bouncycastle.Crypto;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Crypto;
using iText.Kernel.Pdf;
using iText.Signatures;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Signatures;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Signatures;

/// <summary>
/// Verifica delle firme dentro un PDF. I PDF firmati per le prove vengono creati
/// qui, così il test non dipende da file esterni né da certificati che scadono.
/// </summary>
public class PdfSignatureInspectorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-pdfsign-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateUnsignedPdf(string name = "documento.pdf")
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.ExportPlainText(
            "CONTRATTO DI FORNITURA\n\nImporto: 1.000 euro\nDurata: 12 mesi\n", "documento", path);
        return path;
    }

    private string Sign(string sourcePdf, string commonName, string targetName,
        string? reason = null, string? location = null)
    {
        var identity = SignatureTestFixtures.CreateIdentity(commonName);
        var target = Path.Combine(_dir, targetName);

        using (var output = File.Create(target))
        {
            // Append mode: firmare riscrivendo il file invaliderebbe le firme
            // gia' presenti. E' cosi' che si firma un PDF, anche la prima volta.
            var signer = new PdfSigner(new PdfReader(sourcePdf), output,
                new StampingProperties().UseAppendMode());
            var properties = signer.GetSignerProperties();
            if (reason is not null)
                properties.SetReason(reason);
            if (location is not null)
                properties.SetLocation(location);

            IExternalSignature signature = new PrivateKeySignature(
                new PrivateKeyBC(identity.PrivateKey), DigestAlgorithms.SHA256);
            IX509Certificate[] chain = [new X509CertificateBC(identity.Certificate)];
            signer.SignDetached(signature, chain, null, null, null, 0, PdfSigner.CryptoStandard.CMS);
        }

        return target;
    }

    [Fact]
    public void Inspect_PdfNonFirmato_NessunaFirma()
    {
        Assert.Empty(PdfSignatureInspector.Inspect(CreateUnsignedPdf()));
        Assert.False(PdfSignatureInspector.HasSignatures(CreateUnsignedPdf("altro.pdf")));
    }

    [Fact]
    public void Inspect_PdfFirmato_RiportaFirmatarioEIntegrita()
    {
        var firmato = Sign(CreateUnsignedPdf(), "Mario Rossi", "firmato.pdf",
            reason: "Approvazione", location: "Pignataro Maggiore");

        var firme = PdfSignatureInspector.Inspect(firmato);

        var firma = Assert.Single(firme);
        Assert.Equal("Mario Rossi", firma.Signer.DisplayName);
        Assert.True(firma.Signer.IntegrityVerified);
        Assert.Null(firma.Signer.Problem);
        Assert.True(firma.CoversWholeDocument);
        Assert.True(firma.Signer.CertificateValidAtSigning);
        Assert.NotNull(firma.Signer.SignedAt);
        Assert.Equal("Approvazione", firma.Reason);
        Assert.Equal("Pignataro Maggiore", firma.Location);
        Assert.Contains("RSA", firma.Algorithm, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Il caso che conta: qualcuno modifica il PDF dopo la firma. Deve risultare
    /// che la firma non copre più tutto il documento.
    /// </summary>
    [Fact]
    public void Inspect_ModificatoDopoLaFirma_NonCopreTuttoIlDocumento()
    {
        var firmato = Sign(CreateUnsignedPdf(), "Mario Rossi", "firmato.pdf");
        var modificato = Path.Combine(_dir, "modificato.pdf");

        // Si aggiunge una pagina in append: la firma resta valida sulla revisione
        // firmata, ma non copre più il file così com'è ora.
        using (var document = new PdfDocument(new PdfReader(firmato),
            new PdfWriter(modificato), new StampingProperties().UseAppendMode()))
        {
            document.AddNewPage();
        }

        var firma = Assert.Single(PdfSignatureInspector.Inspect(modificato));

        Assert.False(firma.CoversWholeDocument);
        Assert.True(firma.Signer.IntegrityVerified, "la revisione firmata è comunque intatta");
    }

    [Fact]
    public void Inspect_DueFirme_LeElencaEntrambe()
    {
        var prima = Sign(CreateUnsignedPdf(), "Mario Rossi", "prima.pdf");
        var seconda = Sign(prima, "Anna Bianchi", "seconda.pdf");

        var firme = PdfSignatureInspector.Inspect(seconda);

        Assert.Equal(2, firme.Count);
        Assert.Contains(firme, f => f.Signer.DisplayName == "Mario Rossi");
        Assert.Contains(firme, f => f.Signer.DisplayName == "Anna Bianchi");
        Assert.All(firme, f => Assert.True(f.Signer.IntegrityVerified));
    }

    [Fact]
    public void Disclaimer_DiceChiaramenteCosaNonVerifichiamo()
    {
        Assert.Contains("revocato", PdfSignatureInspector.LegalDisclaimer);
        Assert.Contains("non è un accertamento di validità legale",
            PdfSignatureInspector.LegalDisclaimer);
    }
}
