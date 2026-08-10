using System.Text;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Signatures;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Signatures;

/// <summary>
/// Aprire una busta firmata <c>.p7m</c>: tirare fuori il documento vero e dire
/// chi l'ha firmato. Il punto che conta è che una busta manomessa <b>non</b>
/// passi per buona.
/// </summary>
public class P7mReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-p7m-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteP7m(string name, byte[] content, SignatureTestFixtures.Identity identity)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, SignatureTestFixtures.CreateP7m(content, identity));
        return path;
    }

    [Fact]
    public void Read_EstraeIlDocumentoEIlFirmatario()
    {
        var identity = SignatureTestFixtures.CreateIdentity("Mario Rossi");
        var originale = SignatureTestFixtures.TextBytes("Contratto di fornitura del 3 marzo.");
        var path = WriteP7m("contratto.txt.p7m", originale, identity);

        var content = P7mReader.Read(path);

        Assert.Equal(originale, content.Data);
        Assert.Equal("contratto.txt", content.SuggestedFileName);

        var firmatario = Assert.Single(content.Signers);
        Assert.Equal("Mario Rossi", firmatario.DisplayName);
        Assert.True(firmatario.IntegrityVerified);
        Assert.Null(firmatario.Problem);
        Assert.True(firmatario.CertificateValidAtSigning);
    }

    [Fact]
    public void Read_BustaConUnPdf_LoRiconosce()
    {
        var pdfPath = Path.Combine(_dir, "documento.pdf");
        MarkdownPdfExporter.ExportPlainText("Documento di prova", "documento", pdfPath);
        var identity = SignatureTestFixtures.CreateIdentity("Anna Bianchi");
        var path = WriteP7m("documento.pdf.p7m", File.ReadAllBytes(pdfPath), identity);

        var content = P7mReader.Read(path);

        Assert.True(content.IsPdf);
        Assert.Equal("documento.pdf", content.SuggestedFileName);
    }

    /// <summary>
    /// Il caso che davvero importa: se qualcuno cambia il contenuto dopo la
    /// firma, la verifica deve accorgersene e dirlo.
    /// </summary>
    [Fact]
    public void Read_ContenutoAlteratoDopoLaFirma_LoSegnala()
    {
        var identity = SignatureTestFixtures.CreateIdentity("Mario Rossi");
        var busta = SignatureTestFixtures.CreateP7m(
            SignatureTestFixtures.TextBytes("Importo: 1.000 euro"), identity);

        // Si sostituisce "1.000" con "9.000" dentro la busta, lasciando la firma
        // com'era. La sostituzione avviene sui byte: passare per una stringa
        // rovinerebbe la struttura binaria della busta.
        var manomessa = (byte[])busta.Clone();
        var posizione = IndexOf(manomessa, SignatureTestFixtures.TextBytes("1.000 euro"));
        Assert.True(posizione >= 0, "contenuto non trovato dentro la busta");
        manomessa[posizione] = (byte)'9';
        Assert.NotEqual(busta, manomessa);

        var path = Path.Combine(_dir, "manomesso.txt.p7m");
        File.WriteAllBytes(path, manomessa);

        var content = P7mReader.Read(path);
        var firmatario = Assert.Single(content.Signers);

        Assert.False(firmatario.IntegrityVerified);
        Assert.NotNull(firmatario.Problem);
        Assert.Contains("alterato", firmatario.Problem);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length && found; j++)
                found = haystack[i + j] == needle[j];
            if (found)
                return i;
        }
        return -1;
    }

    [Fact]
    public void Read_BustaCodificataInBase64_VieneComunqueLetta()
    {
        var identity = SignatureTestFixtures.CreateIdentity("Studio Verdi");
        var originale = SignatureTestFixtures.TextBytes("Fattura numero 42");
        var base64 = Convert.ToBase64String(SignatureTestFixtures.CreateP7m(originale, identity));
        var path = Path.Combine(_dir, "fattura.xml.p7m");
        File.WriteAllText(path, base64);

        var content = P7mReader.Read(path);

        Assert.Equal(originale, content.Data);
        Assert.Equal("Studio Verdi", content.Signers[0].DisplayName);
    }

    [Fact]
    public void Read_FileCheNonEUnaBusta_SpiegaIlProblema()
    {
        var path = Path.Combine(_dir, "finto.p7m");
        File.WriteAllText(path, "questo non e' un p7m");

        var errore = Assert.Throws<SignatureReadException>(() => P7mReader.Read(path));

        Assert.Contains("busta firmata", errore.Message);
    }

    [Fact]
    public void CertificatoScadutoAllaFirma_ViendeDichiarato()
    {
        var scaduto = SignatureTestFixtures.CreateIdentity("Ditta Scaduta",
            notBefore: DateTime.UtcNow.AddYears(-5), notAfter: DateTime.UtcNow.AddYears(-2));
        var path = WriteP7m("vecchio.txt.p7m", SignatureTestFixtures.TextBytes("vecchio"), scaduto);

        var firmatario = P7mReader.Read(path).Signers[0];

        // La firma è integra: è il certificato a non essere più valido.
        Assert.True(firmatario.IntegrityVerified);
        Assert.False(firmatario.CertificateValidAtSigning);
    }
}
