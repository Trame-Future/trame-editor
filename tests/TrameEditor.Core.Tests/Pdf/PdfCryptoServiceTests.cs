using iText.Kernel.Exceptions;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfCryptoServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-crypto-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreatePdf()
    {
        var path = Path.Combine(_dir, "src.pdf");
        MarkdownPdfExporter.Export("# Documento riservato\n\ncontenuto segreto", "doc", path);
        return path;
    }

    [Fact]
    public void EncryptThenDecrypt_Roundtrip()
    {
        var plain = CreatePdf();
        var locked = Path.Combine(_dir, "locked.pdf");
        var unlocked = Path.Combine(_dir, "unlocked.pdf");

        PdfCryptoService.Encrypt(plain, locked, "segreta123");

        Assert.False(PdfCryptoService.IsPasswordProtected(plain));
        Assert.True(PdfCryptoService.IsPasswordProtected(locked));

        PdfCryptoService.Decrypt(locked, unlocked, "segreta123");
        Assert.False(PdfCryptoService.IsPasswordProtected(unlocked));
        using var inspector = new PdfTextInspector(unlocked);
        Assert.Contains(inspector.GetLines(1), l => l.Text.Contains("riservato"));
    }

    [Fact]
    public void Decrypt_WrongPassword_Throws()
    {
        var plain = CreatePdf();
        var locked = Path.Combine(_dir, "locked2.pdf");
        PdfCryptoService.Encrypt(plain, locked, "giusta");

        Assert.Throws<BadPasswordException>(() =>
            PdfCryptoService.Decrypt(locked, Path.Combine(_dir, "out.pdf"), "sbagliata"));
    }
}
