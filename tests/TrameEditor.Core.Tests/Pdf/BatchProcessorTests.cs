using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class BatchProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-batch-").FullName;
    private readonly string _outDir;

    public BatchProcessorTests()
    {
        _outDir = Path.Combine(_dir, "out");
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateSensitivePdf(string name)
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.Export(
            "# Documento\n\nCodice fiscale RSSMRA80A01H501U\n\nemail mario@example.com\n\nTesto normale.",
            name, path);
        return path;
    }

    [Fact]
    public void ProcessFile_RedactCompressProtect_FullChain()
    {
        var source = CreateSensitivePdf("doc.pdf");
        var recipe = new BatchRecipe(RunOcr: false, Redact: true, Compress: true, ProtectPassword: "batch123");

        var result = BatchProcessor.ProcessFile(source, _outDir, recipe);

        Assert.True(result.Success, result.Outcome);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Contains("anonimizzati", result.Outcome);
        Assert.Contains("protetto", result.Outcome);

        // il risultato è protetto; decifrandolo, i dati sensibili non ci sono più
        Assert.True(PdfCryptoService.IsPasswordProtected(result.OutputPath!));
        var decrypted = Path.Combine(_dir, "check.pdf");
        PdfCryptoService.Decrypt(result.OutputPath!, decrypted, "batch123");
        Assert.Empty(PdfRedactionService.Scan(decrypted)
            .Where(m => m.Kind is SensitiveKind.CodiceFiscale or SensitiveKind.Email));

        // l'originale è intatto
        Assert.NotEmpty(PdfRedactionService.Scan(source));
    }

    [Fact]
    public void ProcessFile_ProtectedInput_IsSkippedHonestly()
    {
        var source = CreateSensitivePdf("chiuso.pdf");
        var locked = Path.Combine(_dir, "lucchettato.pdf");
        PdfCryptoService.Encrypt(source, locked, "x");

        var result = BatchProcessor.ProcessFile(locked, _outDir,
            new BatchRecipe(false, true, false, null));

        Assert.False(result.Success);
        Assert.Contains("protetto da password", result.Outcome);
    }

    [Fact]
    public void ProcessFile_OutputNameIsUnique()
    {
        var source = CreateSensitivePdf("stesso.pdf");
        var recipe = new BatchRecipe(false, false, true, null);

        var first = BatchProcessor.ProcessFile(source, _outDir, recipe);
        var second = BatchProcessor.ProcessFile(source, _outDir, recipe);

        Assert.True(first.Success && second.Success);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
    }

    [Fact]
    public void ProcessFile_WithNoSteps_ReportsIt()
    {
        var source = CreateSensitivePdf("niente.pdf");
        var result = BatchProcessor.ProcessFile(source, _outDir, new BatchRecipe(false, false, false, null));
        Assert.False(result.Success);
        Assert.Contains("nessun passo", result.Outcome);
    }
}
