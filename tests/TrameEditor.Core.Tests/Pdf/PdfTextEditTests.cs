using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Test end-to-end della M3: genera un PDF reale (PDFsharp, font Arial subset
/// incorporato), estrae le righe con PdfTextInspector, sostituisce con
/// PdfTextReplacer e riestrae per verificare.
/// </summary>
public class PdfTextEditTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-textedit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static PdfTextEditTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new("Arial#");
    }

    /// <summary>PDF di una pagina con due righe ("Hello World" e "Seconda riga di prova").</summary>
    private string CreateSamplePdf()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 16);
        gfx.DrawString("Hello World", font, XBrushes.Black, new XPoint(50, 100));
        gfx.DrawString("Seconda riga di prova", font, XBrushes.Black, new XPoint(50, 140));
        document.Save(path);
        return path;
    }

    private static PdfTextLine LineContaining(string path, string text)
    {
        using var inspector = new PdfTextInspector(path);
        return inspector.GetLines(1).Single(l => l.Text.Contains(text));
    }

    [Fact]
    public void Inspector_FindsBothLines_WithPlausibleGeometry()
    {
        var path = CreateSamplePdf();
        using var inspector = new PdfTextInspector(path);

        var lines = inspector.GetLines(1);

        Assert.Contains(lines, l => l.Text == "Hello World");
        Assert.Contains(lines, l => l.Text == "Seconda riga di prova");
        var hello = lines.Single(l => l.Text == "Hello World");
        Assert.True(hello.IsEditable);
        Assert.True(hello.FontSizePt is > 10 and < 25, $"corpo font implausibile: {hello.FontSizePt}");
        var (width, height) = inspector.GetPageSize(1);
        Assert.True(hello.Left > 0 && hello.Left < width);
        Assert.True(hello.BaselineY > 0 && hello.BaselineY < height);
    }

    [Fact]
    public void PlanFor_ReturnsAStrategyWithDescription()
    {
        var path = CreateSamplePdf();
        var line = LineContaining(path, "Hello World");

        var plan = PdfTextReplacer.PlanFor(path, line, "Ciao Mondo");

        Assert.False(string.IsNullOrWhiteSpace(plan.Description));
        // Su Windows Arial è installato: mai costretti al sostituto standard.
        Assert.NotEqual(PdfFontStrategy.Substitute, plan.Strategy);
    }

    [Fact]
    public void Replace_SwapsLineText_AndKeepsTheOtherLine()
    {
        var path = CreateSamplePdf();
        var target = Path.Combine(_dir, "modificato.pdf");
        var line = LineContaining(path, "Hello World");
        var plan = PdfTextReplacer.PlanFor(path, line, "Testo nuovo!");

        PdfTextReplacer.Replace(path, target, line, "Testo nuovo!", plan);

        using var inspector = new PdfTextInspector(target);
        var texts = inspector.GetLines(1).Select(l => l.Text).ToList();
        Assert.Contains("Testo nuovo!", texts);
        Assert.Contains("Seconda riga di prova", texts);
        Assert.DoesNotContain(texts, t => t.Contains("Hello"));
    }

    [Fact]
    public void Replace_NewTextKeepsPositionOfOriginal()
    {
        var path = CreateSamplePdf();
        var target = Path.Combine(_dir, "posizione.pdf");
        var line = LineContaining(path, "Hello World");
        var plan = PdfTextReplacer.PlanFor(path, line, "Sostituito");

        PdfTextReplacer.Replace(path, target, line, "Sostituito", plan);

        var replaced = LineContaining(target, "Sostituito");
        Assert.True(Math.Abs(replaced.BaselineY - line.BaselineY) < 2.0,
            $"baseline spostata: {line.BaselineY} → {replaced.BaselineY}");
        Assert.True(Math.Abs(replaced.Left - line.Left) < 2.0,
            $"riga spostata in orizzontale: {line.Left} → {replaced.Left}");
    }

    [Fact]
    public void Replace_WithEmptyText_DeletesTheLine()
    {
        var path = CreateSamplePdf();
        var target = Path.Combine(_dir, "cancellato.pdf");
        var line = LineContaining(path, "Hello World");
        var plan = PdfTextReplacer.PlanFor(path, line, "");

        PdfTextReplacer.Replace(path, target, line, "", plan);

        using var inspector = new PdfTextInspector(target);
        var texts = inspector.GetLines(1).Select(l => l.Text).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Hello"));
        Assert.Contains("Seconda riga di prova", texts);
    }

    /// <summary>
    /// Riproduce il bug segnalato dal beta test (CV generato da Word): il content
    /// stream della pagina termina con una trasformazione attiva (q/cm senza Q).
    /// Il testo sostitutivo, disegnato in coda, la ereditava e finiva scalato e
    /// fuori posto ("la riga sparisce").
    /// </summary>
    [Fact]
    public void Replace_PageWithLeftoverTransform_KeepsPositionOfOriginal()
    {
        var path = Path.Combine(_dir, "trasformazione.pdf");
        using (var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(path)))
        {
            var page = document.AddNewPage(new iText.Kernel.Geom.PageSize(595, 842));
            var font = iText.Kernel.Font.PdfFontFactory.CreateFont(
                iText.IO.Font.Constants.StandardFonts.HELVETICA);
            var fontName = page.GetResources().AddFont(document, font);
            // cm attivo e mai richiuso: baseline in coordinate dispositivo = (95, 475)
            var content = $"q 0.75 0 0 0.75 20 100 cm\nBT /{fontName.GetValue()} 16 Tf 100 500 Td (Telefono 123456) Tj ET\n";
            page.NewContentStreamAfter().SetData(System.Text.Encoding.ASCII.GetBytes(content));
        }

        var line = LineContaining(path, "Telefono");
        var target = Path.Combine(_dir, "trasformazione-out.pdf");
        var plan = PdfTextReplacer.PlanFor(path, line, "Cellulare 999");

        PdfTextReplacer.Replace(path, target, line, "Cellulare 999", plan);

        var replaced = LineContaining(target, "Cellulare 999");
        Assert.True(Math.Abs(replaced.BaselineY - line.BaselineY) < 2.0,
            $"baseline spostata: {line.BaselineY} → {replaced.BaselineY}");
        Assert.True(Math.Abs(replaced.Left - line.Left) < 2.0,
            $"riga spostata in orizzontale: {line.Left} → {replaced.Left}");
        Assert.True(Math.Abs(replaced.FontSizePt - line.FontSizePt) < 1.5,
            $"corpo font cambiato: {line.FontSizePt} → {replaced.FontSizePt}");
    }

    [Fact]
    public void Replace_LineNotInPageStream_ThrowsHonestError()
    {
        var path = CreateSamplePdf();
        var target = Path.Combine(_dir, "fallito.pdf");
        var ghost = new PdfTextLine(1, "fantasma", 400, 700, 50, 10, 400, 700,
            "Inesistente", 12, 0, 0, 0, true, null);

        var ex = Assert.Throws<PdfTextEditException>(() =>
            PdfTextReplacer.Replace(path, target, ghost, "x",
                new PdfFontPlan(PdfFontStrategy.Substitute, "test", null, "Helvetica")));
        Assert.Contains("non applicabile", ex.Message);
    }
}
