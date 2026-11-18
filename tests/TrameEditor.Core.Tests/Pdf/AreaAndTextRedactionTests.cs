using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Documents;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Anonimizzazione per area (il rettangolo tirato su ciò che si vede) e sui file di testo.
/// Il principio è lo stesso del resto del programma: il contenuto si toglie davvero, non si
/// copre — un rettangolo nero disegnato sopra lascia il testo selezionabile.
/// </summary>
public class AreaAndTextRedactionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-aree-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static AreaAndTextRedactionTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new("Arial#");
    }

    /// <summary>Tre righe su una pagina A4; PDFsharp disegna dall'alto, il PDF misura dal
    /// basso: una riga a y=100 sta a 742 punti dal fondo.</summary>
    private string Document()
    {
        var path = Path.Combine(_dir, "documento.pdf");
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        gfx.DrawString("Intestazione pubblica", font, XBrushes.Black, new XPoint(60, 100));
        gfx.DrawString("Dato riservato da togliere", font, XBrushes.Black, new XPoint(60, 300));
        gfx.DrawString("Chiusura pubblica", font, XBrushes.Black, new XPoint(60, 500));
        document.Save(path);
        return path;
    }

    private string Target(string name = "pulito.pdf") => Path.Combine(_dir, name);

    private static List<string> TextsOf(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var texts = new List<string>();
        for (var page = 1; page <= inspector.PageCount; page++)
            texts.AddRange(inspector.GetLines(page).Select(l => l.Text));
        return texts;
    }

    /// <summary>La riga centrale sta a circa 842-300 = 542 punti dal fondo.</summary>
    private static RedactionArea MiddleLine() => new(1, 50, 530, 300, 25);

    [Fact]
    public void TheTextInsideTheAreaIsReallyGone()
    {
        var pdf = Document();

        var result = PdfAreaRedactor.Apply(pdf, Target(), [MiddleLine()]);

        Assert.Equal(1, result.TextLinesRemoved);
        var texts = TextsOf(Target());
        // Non deve essere più estraibile: coprirlo non basterebbe.
        Assert.DoesNotContain(texts, t => t.Contains("riservato"));
    }

    [Fact]
    public void WhatIsOutsideTheAreaStays()
    {
        var pdf = Document();

        PdfAreaRedactor.Apply(pdf, Target(), [MiddleLine()]);

        var texts = TextsOf(Target());
        Assert.Contains(texts, t => t.Contains("Intestazione"));
        Assert.Contains(texts, t => t.Contains("Chiusura"));
    }

    [Fact]
    public void NoAreas_IsRefusedWithAReason()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PdfAreaRedactor.Apply(Document(), Target(), []));

        Assert.Contains("nessuna area", error.Message);
    }

    /// <summary>Un'area dove non c'è testo può essere giusta — una firma, una foto — ma va
    /// detto, perché il caso più probabile è che le coordinate siano sbagliate.</summary>
    [Fact]
    public void AnAreaWithoutText_IsReported()
    {
        var result = PdfAreaRedactor.Apply(Document(), Target(), [new RedactionArea(1, 400, 50, 100, 30)]);

        Assert.Equal(0, result.TextLinesRemoved);
        Assert.Contains(result.Warnings, w => w.Contains("non c'era testo"));
    }

    [Fact]
    public void APageThatDoesNotExist_IsReportedNotCrashed()
    {
        var result = PdfAreaRedactor.Apply(Document(), Target(), [new RedactionArea(9, 50, 50, 100, 30)]);

        Assert.Contains(result.Warnings, w => w.Contains("pagina 9"));
    }

    // ----- File di testo -----

    [Fact]
    public void FindsPersonalDataInPlainText()
    {
        var text = "Cliente: RSSMRA80A01H501U\nIBAN IT60X0542811101000000123456\nNiente qui";

        var found = TextRedactor.Scan(text);

        Assert.Contains(found, m => m.Kind == SensitiveKind.CodiceFiscale && m.LineNumber == 1);
        Assert.Contains(found, m => m.Kind == SensitiveKind.Iban && m.LineNumber == 2);
    }

    [Fact]
    public void ReplacesThemWithX_KeepingTheRest()
    {
        var text = "Cliente: RSSMRA80A01H501U - pratica 42";

        var (redacted, result) = TextRedactor.Redact(text);

        Assert.DoesNotContain("RSSMRA80A01H501U", redacted);
        Assert.Contains("pratica 42", redacted);
        Assert.Equal(1, result.LinesChanged);
    }

    [Fact]
    public void OnlyTheChosenKinds()
    {
        var text = "RSSMRA80A01H501U e mario@example.com";

        var (redacted, _) = TextRedactor.Redact(text, [SensitiveKind.Email]);

        Assert.Contains("RSSMRA80A01H501U", redacted); // non chiesto: resta
        Assert.DoesNotContain("mario@example.com", redacted);
    }

    /// <summary>Un .txt di un gestionale può essere in ANSI con fine riga di Windows:
    /// restituirlo in UTF-8 con fine riga Unix vuol dire che chi lo rilegge non lo
    /// riconosce più.</summary>
    [Fact]
    public void TheFileKeepsItsEncodingAndLineEndings()
    {
        var source = Path.Combine(_dir, "ansi.txt");
        var ansi = Encoding.GetEncoding(1252);
        File.WriteAllText(source, "Città: Caserta\r\nCF RSSMRA80A01H501U\r\n", ansi);
        var target = Path.Combine(_dir, "ansi-pulito.txt");

        TextRedactor.RedactFile(source, target);

        var bytes = File.ReadAllBytes(target);
        Assert.DoesNotContain(bytes, b => b == 0xEF); // nessun BOM UTF-8 comparso dal nulla
        var text = File.ReadAllText(target, ansi);
        Assert.Contains("Città", text);
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("RSSMRA80A01H501U", text);
    }

    [Fact]
    public void NothingToRedact_LeavesTheTextAsItWas()
    {
        var text = "Nessun dato personale qui dentro.";

        var (redacted, result) = TextRedactor.Redact(text);

        Assert.Equal(text, redacted);
        Assert.Equal(0, result.ItemsRedacted);
    }
}
