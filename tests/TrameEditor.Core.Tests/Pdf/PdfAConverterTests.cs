using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using TrameEditor.Core.Pdf;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using Path = System.IO.Path;
using Rectangle = iText.Kernel.Geom.Rectangle;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfAConverterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-pdfa-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ----- Documenti di prova -----

    /// <summary>Documento normale: font standard <b>non incorporato</b>, il caso
    /// più comune (è anche quello che produce il nostro export Markdown→PDF).</summary>
    private string CreateDocumentWithStandardFont(string name = "documento.pdf")
    {
        var path = Path.Combine(_dir, name);
        using var document = new PdfDocument(new PdfWriter(path));
        var page = document.AddNewPage(PageSize.A4);
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        new PdfCanvas(page)
            .BeginText().SetFontAndSize(font, 14).MoveText(60, 760)
            .ShowText("Contratto di prova numero 12345").EndText();
        return path;
    }

    private string CreateDocumentWithSymbolFont()
    {
        var path = Path.Combine(_dir, "simboli.pdf");
        using var document = new PdfDocument(new PdfWriter(path));
        var page = document.AddNewPage(PageSize.A4);
        var font = PdfFontFactory.CreateFont(StandardFonts.SYMBOL);
        new PdfCanvas(page)
            .BeginText().SetFontAndSize(font, 14).MoveText(60, 760).ShowText("abg").EndText();
        return path;
    }

    private static string ExtractText(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var text = new StringBuilder();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
                text.AppendLine(line.Text);
        }
        return text.ToString();
    }

    // ----- Analisi -----

    [Fact]
    public void Analyze_FontStandardNonIncorporato_ESegnalatoComeCorreggibile()
    {
        var path = CreateDocumentWithStandardFont();

        var report = PdfAAnalyzer.Analyze(path);

        Assert.Contains("Helvetica", report.NonEmbeddedFonts);
        Assert.True(report.CanConvertFaithfully,
            "Helvetica ha un equivalente di sistema: deve restare convertibile");
        Assert.Contains(report.Issues, i =>
            i.Severity == PdfAIssueSeverity.Corretto && i.Description.Contains("non incorporato"));
    }

    [Fact]
    public void Analyze_FontSenzaEquivalente_BloccaLaConversioneFedele()
    {
        var path = CreateDocumentWithSymbolFont();

        var report = PdfAAnalyzer.Analyze(path);

        Assert.False(report.CanConvertFaithfully);
        Assert.Contains(report.Blocking, i => i.Description.Contains("Symbol"));
    }

    /// <summary>
    /// Helvetica e Arial coincidono su tutto l'alfabeto ma non su quattro simboli
    /// (¯ ± · ÷). Se il documento li usa, sostituire il font sposterebbe il testo:
    /// va rifiutato. Se non li usa, la sostituzione è legittima — verificarlo su
    /// tutto il set invece che sui caratteri usati rifiuterebbe documenti sani.
    /// </summary>
    [Fact]
    public void Analyze_CaratteriConMetricheDiverse_BloccaLaSostituzioneDelFont()
    {
        var path = Path.Combine(_dir, "conSimboli.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage(PageSize.A4);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            new PdfCanvas(page).BeginText().SetFontAndSize(font, 14).MoveText(60, 760)
                .ShowText("tolleranza ± 3 mm").EndText();
        }

        var report = PdfAAnalyzer.Analyze(path);

        Assert.False(report.CanConvertFaithfully);
        Assert.Contains(report.Blocking, i => i.Description.Contains("Helvetica"));

        // Lo stesso testo senza quel simbolo resta convertibile.
        Assert.True(PdfAAnalyzer.Analyze(CreateDocumentWithStandardFont("senzaSimboli.pdf"))
            .CanConvertFaithfully);
    }

    [Fact]
    public void Analyze_FontDichiaratoMaNonUsato_NonBlocca()
    {
        var path = Path.Combine(_dir, "fontInutilizzato.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage(PageSize.A4);
            // Symbol finisce fra le risorse della pagina ma non scrive nulla.
            var unused = PdfFontFactory.CreateFont(StandardFonts.SYMBOL);
            var canvas = new PdfCanvas(page);
            canvas.BeginText().SetFontAndSize(unused, 12).EndText();
            canvas.BeginText().SetFontAndSize(PdfFontFactory.CreateFont(StandardFonts.HELVETICA), 12)
                .MoveText(60, 760).ShowText("Solo testo normale").EndText();
        }

        var report = PdfAAnalyzer.Analyze(path);

        Assert.True(report.CanConvertFaithfully,
            "un font mai usato non deve impedire l'archiviazione: " +
            string.Join("; ", report.Blocking));
    }

    [Fact]
    public void Analyze_PdfProtettoDaPassword_EBloccante()
    {
        var source = CreateDocumentWithStandardFont();
        var protectedPath = Path.Combine(_dir, "protetto.pdf");
        PdfCryptoService.Encrypt(source, protectedPath, "prova123");

        var report = PdfAAnalyzer.Analyze(protectedPath);

        Assert.False(report.CanConvertFaithfully);
        Assert.Contains(report.Blocking, i => i.Description.Contains("password"));
    }

    // ----- Conversione fedele -----

    [Fact]
    public void ConvertFaithfully_IncorporaIFontEConservaIlTesto()
    {
        var source = CreateDocumentWithStandardFont();
        var target = Path.Combine(_dir, "archivio.pdf");
        var testoOriginale = ExtractText(source);

        var result = PdfAConverter.ConvertFaithfully(source, target);

        Assert.Equal(PdfAConversionMethod.Fedele, result.Method);
        // Il testo deve restare quello di prima: un archivio diverso dall'originale
        // non sarebbe un archivio.
        Assert.Contains("Contratto di prova numero 12345", ExtractText(target));
        Assert.Equal(testoOriginale.Trim(), ExtractText(target).Trim());
        // E nessun font può più risultare non incorporato.
        Assert.Empty(result.Verification.NonEmbeddedFonts);
        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));
        Assert.Contains(result.Changes, c => c.Contains("incorporato"));
    }

    [Fact]
    public void ConvertFaithfully_ScriveOutputIntentEMetadatiPdfA()
    {
        var source = CreateDocumentWithStandardFont();
        var target = Path.Combine(_dir, "archivio.pdf");

        var result = PdfAConverter.ConvertFaithfully(source, target);

        using var document = new PdfDocument(new PdfReader(target));
        var catalog = document.GetCatalog().GetPdfObject();

        var outputIntents = catalog.GetAsArray(PdfName.OutputIntents);
        Assert.NotNull(outputIntents);
        var intent = outputIntents.GetAsDictionary(0);
        Assert.NotNull(intent!.GetAsStream(PdfName.DestOutputProfile));

        var xmp = Encoding.UTF8.GetString(catalog.GetAsStream(PdfName.Metadata)!.GetBytes());
        Assert.Contains("pdfaid:part=\"2\"", xmp);
        Assert.Contains($"pdfaid:conformance=\"{(result.Level == PdfALevel.A2u ? "U" : "B")}\"", xmp);
        Assert.Equal(PdfVersion.PDF_1_7, document.GetPdfVersion());
    }

    [Fact]
    public void ConvertFaithfully_RimuoveJavaScriptEAllegati()
    {
        var path = Path.Combine(_dir, "conScript.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage(PageSize.A4);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            new PdfCanvas(page).BeginText().SetFontAndSize(font, 12)
                .MoveText(60, 760).ShowText("Documento con script").EndText();
            AddJavaScript(document, "app.alert('ciao');");
            document.AddFileAttachment("nota.txt", iText.Kernel.Pdf.Filespec.PdfFileSpec
                .CreateEmbeddedFileSpec(document, Encoding.UTF8.GetBytes("dati"), "nota",
                    "nota.txt", null, null));
        }
        var target = Path.Combine(_dir, "archivio.pdf");

        var result = PdfAConverter.ConvertFaithfully(path, target);

        using var converted = new PdfDocument(new PdfReader(target));
        var names = converted.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names);
        Assert.True(names is null || !names.ContainsKey(PdfName.JavaScript));
        Assert.True(names is null || !names.ContainsKey(PdfName.EmbeddedFiles));
        Assert.Contains(result.Changes, c => c.Contains("JavaScript"));
        Assert.Contains(result.Changes, c => c.Contains("allegati"));
    }

    /// <summary>Inserisce uno script a livello di documento (catalogo → Names → JavaScript).</summary>
    private static void AddJavaScript(PdfDocument document, string script)
    {
        var action = new PdfDictionary();
        action.Put(PdfName.S, PdfName.JavaScript);
        action.Put(PdfName.JS, new PdfString(script));

        var entries = new PdfArray();
        entries.Add(new PdfString("script1"));
        entries.Add(action.MakeIndirect(document));

        var javaScriptNode = new PdfDictionary();
        javaScriptNode.Put(PdfName.Names, entries);

        var names = document.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names)
            ?? new PdfDictionary();
        names.Put(PdfName.JavaScript, javaScriptNode.MakeIndirect(document));
        document.GetCatalog().GetPdfObject().Put(PdfName.Names, names.MakeIndirect(document));
    }

    [Fact]
    public void ConvertFaithfully_FontNonSostituibile_RifiutaSpiegandoPerche()
    {
        var source = CreateDocumentWithSymbolFont();
        var target = Path.Combine(_dir, "archivio.pdf");

        var error = Assert.Throws<PdfAConversionException>(
            () => PdfAConverter.ConvertFaithfully(source, target));

        Assert.Contains("Symbol", error.Message);
        Assert.False(File.Exists(target), "un rifiuto non deve lasciare file a metà");
    }

    // ----- Conversione per immagine -----

    /// <summary>"Scansione" sintetica: una pagina immagine senza layer di testo.</summary>
    private (string PdfPath, byte[] PagePng, double Scale) CreateScannedPdf()
    {
        const int width = 1240, height = 1754; // A4 a ~150 dpi
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Arial", 40);
            graphics.DrawString("VERBALE DI ASSEMBLEA", font, Brushes.Black, 100, 300);
            graphics.DrawString("protocollo 98765", font, Brushes.Black, 100, 450);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var png = stream.ToArray();

        var pdfPath = Path.Combine(_dir, "scansione.pdf");
        using (var document = new PdfDocument(new PdfWriter(pdfPath)))
        {
            var page = document.AddNewPage(new PageSize(595, 842));
            new PdfCanvas(page).AddImageFittedIntoRectangle(ImageDataFactory.Create(png),
                new Rectangle(0, 0, 595, 842), asInline: false);
        }
        return (pdfPath, png, width / 595.0);
    }

    /// <summary>Trova la cartella tessdata del repo risalendo da bin/.</summary>
    private static string TessdataPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tessdata");
            if (File.Exists(Path.Combine(candidate, "ita.traineddata")))
                return candidate;
            current = current.Parent;
        }
        throw new InvalidOperationException("cartella tessdata non trovata");
    }

    [Fact]
    public void ConvertByRasterizing_ProduceUnPdfAConTestoRicercabile()
    {
        var (source, png, scale) = CreateScannedPdf();
        var target = Path.Combine(_dir, "archivio-raster.pdf");

        var result = PdfAConverter.ConvertByRasterizing(source, target, _ => png, scale, TessdataPath());

        Assert.Equal(PdfAConversionMethod.Raster, result.Method);
        Assert.Empty(result.Verification.NonEmbeddedFonts); // anche il font OCR è incorporato
        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));

        // Il testo dell'OCR deve essere davvero estraibile dal file prodotto.
        var text = ExtractText(target);
        Assert.Contains("ASSEMBLEA", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("98765", text);

        // La pagina conserva le dimensioni fisiche dell'originale.
        using var document = new PdfDocument(new PdfReader(target));
        Assert.Equal(595, document.GetPage(1).GetPageSize().GetWidth(), 1);
        Assert.NotNull(document.GetCatalog().GetPdfObject().GetAsArray(PdfName.OutputIntents));
    }

    [Fact]
    public void ConvertByRasterizing_SenzaOcr_LoDichiara()
    {
        var (source, png, scale) = CreateScannedPdf();
        var target = Path.Combine(_dir, "archivio-raster.pdf");

        var result = PdfAConverter.ConvertByRasterizing(source, target, _ => png, scale, tessdataPath: null);

        Assert.Contains(result.Changes, c => c.Contains("nessun OCR"));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void SrgbColorProfile_EDisponibileSuWindows()
    {
        Assert.True(SrgbColorProfile.IsAvailable,
            "senza il profilo sRGB di sistema la conversione PDF/A non è possibile");
        Assert.NotEmpty(SrgbColorProfile.Load());
    }
}
