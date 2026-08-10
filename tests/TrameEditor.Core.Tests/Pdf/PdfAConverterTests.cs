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

    // ----- Colori CMYK -----

    private static string PageContent(string path, int pageNumber = 1)
    {
        using var document = new PdfDocument(new PdfReader(path));
        var contents = document.GetPage(pageNumber).GetPdfObject().Get(PdfName.Contents);
        var bytes = contents switch
        {
            PdfStream stream => stream.GetBytes(),
            PdfArray array => Enumerable.Range(0, array.Size())
                .SelectMany(i => array.GetAsStream(i)!.GetBytes()).ToArray(),
            _ => [],
        };
        return Encoding.ASCII.GetString(bytes);
    }

    private string CreateDocumentWithCmykColor()
    {
        var path = Path.Combine(_dir, "cmyk.pdf");
        using var document = new PdfDocument(new PdfWriter(path));
        var page = document.AddNewPage(PageSize.A4);
        var canvas = new PdfCanvas(page);
        // Rosso di quadricromia: niente ciano, tutto magenta e giallo.
        canvas.SetFillColor(new iText.Kernel.Colors.DeviceCmyk(0, 100, 100, 0))
            .Rectangle(50, 600, 200, 100).Fill();
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        canvas.BeginText().SetFontAndSize(font, 14).MoveText(60, 760)
            .ShowText("Documento in quadricromia").EndText();
        return path;
    }

    [Fact]
    public void Analyze_ColoriCmyk_SonoConvertibili()
    {
        var report = PdfAAnalyzer.Analyze(CreateDocumentWithCmykColor());

        Assert.True(report.CanConvertFaithfully, string.Join("; ", report.Blocking));
        Assert.Contains(report.Issues, i =>
            i.Severity == PdfAIssueSeverity.Corretto && i.Description.Contains("CMYK"));
    }

    [Fact]
    public void ConvertFaithfully_ConverteICoIoriCmykInRgb()
    {
        var source = CreateDocumentWithCmykColor();
        var target = Path.Combine(_dir, "archivio-cmyk.pdf");

        Assert.Matches(@"\bk\b", PageContent(source)); // l'originale usa l'operatore CMYK

        var result = PdfAConverter.ConvertFaithfully(source, target);

        var converted = PageContent(target);
        Assert.DoesNotMatch(@"(?m)^[\d\.\s]+k\s*$", converted);
        Assert.Matches(@"[\d\.]+ [\d\.]+ [\d\.]+ rg", converted);
        Assert.Contains(result.Changes, c => c.Contains("CMYK") && c.Contains("sRGB"));

        // E nel file prodotto non deve restare traccia di CMYK.
        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));
        Assert.DoesNotContain(result.Verification.Issues, i => i.Description.Contains("CMYK"));
    }

    [Fact]
    public void ConvertFaithfully_ConverteAncheIColoriTinta()
    {
        var path = Path.Combine(_dir, "tinta.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage(PageSize.A4);

            // Tinta piatta "Rosso" definita su un alternativo CMYK.
            var tint = new PdfDictionary();
            tint.Put(PdfName.FunctionType, new PdfNumber(2));
            tint.Put(PdfName.Domain, new PdfArray(new double[] { 0, 1 }));
            tint.Put(PdfName.C0, new PdfArray(new double[] { 0, 0, 0, 0 }));
            tint.Put(PdfName.C1, new PdfArray(new double[] { 0, 1, 1, 0 }));
            tint.Put(PdfName.N, new PdfNumber(1));

            var separation = new PdfArray();
            separation.Add(PdfName.Separation);
            separation.Add(new PdfName("Rosso"));
            separation.Add(PdfName.DeviceCMYK);
            separation.Add(tint.MakeIndirect(document));

            var colorSpaces = new PdfDictionary();
            colorSpaces.Put(new PdfName("CS0"), separation.MakeIndirect(document));
            page.GetResources().GetPdfObject().Put(PdfName.ColorSpace, colorSpaces);

            var canvas = new PdfCanvas(page);
            canvas.GetContentStream().GetOutputStream()
                .WriteString("/CS0 cs\n1 scn\n50 600 200 100 re\nf\n");
        }
        var target = Path.Combine(_dir, "archivio-tinta.pdf");

        var report = PdfAAnalyzer.Analyze(path);
        Assert.True(report.CanConvertFaithfully, string.Join("; ", report.Blocking));

        PdfAConverter.ConvertFaithfully(path, target);

        var converted = PageContent(target);
        Assert.Contains("/DeviceRGB cs", converted);
        Assert.Matches(@"[\d\.]+ [\d\.]+ [\d\.]+ sc", converted);

        // Lo spazio colore CMYK non è più fra le risorse.
        using var document2 = new PdfDocument(new PdfReader(target));
        var remaining = document2.GetPage(1).GetResources().GetPdfObject()
            .GetAsDictionary(PdfName.ColorSpace);
        Assert.True(remaining is null || remaining.Size() == 0);
    }

    [Fact]
    public void Analyze_ImmagineJpegInCmyk_RestaBloccante()
    {
        var path = Path.Combine(_dir, "jpegCmyk.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage(PageSize.A4);
            var image = new PdfStream([0xFF, 0xD8, 0xFF, 0xE0]);
            image.Put(PdfName.Type, PdfName.XObject);
            image.Put(PdfName.Subtype, PdfName.Image);
            image.Put(PdfName.Filter, PdfName.DCTDecode);
            image.Put(PdfName.ColorSpace, PdfName.DeviceCMYK);
            image.Put(PdfName.Width, new PdfNumber(1));
            image.Put(PdfName.Height, new PdfNumber(1));
            image.Put(PdfName.BitsPerComponent, new PdfNumber(8));

            var xobjects = new PdfDictionary();
            xobjects.Put(new PdfName("Im0"), image.MakeIndirect(document));
            page.GetResources().GetPdfObject().Put(PdfName.XObject, xobjects);
        }

        var report = PdfAAnalyzer.Analyze(path);

        Assert.False(report.CanConvertFaithfully);
        Assert.Contains(report.Blocking, i => i.Description.Contains("JPEG"));
    }

    [Fact]
    public void CmykToRgb_UsaIProfiliDiSistema_EConverteISoliti()
    {
        using var converter = CmykToRgb.ForDeviceCmyk();

        Assert.True(converter.UsesIccProfiles,
            "su Windows il profilo CMYK di sistema deve esserci: " + converter.SourceDescription);

        var white = converter.Convert(0, 0, 0, 0);
        var red = converter.Convert(0, 1, 1, 0);
        var cyan = converter.Convert(1, 0, 0, 0);

        Assert.True(white.R > 0.9 && white.G > 0.9 && white.B > 0.9, $"bianco: {white}");
        Assert.True(red.R > red.G && red.R > red.B, $"rosso: {red}");
        // Il ciano di quadricromia non è (0,1,1): se lo fosse, non staremmo usando il profilo.
        Assert.True(cyan.B > cyan.G && cyan.G < 0.9, $"ciano: {cyan}");
    }

    /// <summary>
    /// Il testo nero di un documento è quasi sempre scritto col solo canale K.
    /// Un profilo di stampa descrive il nero *stampato*, che è un grigio molto
    /// scuro: applicarlo alla lettera renderebbe grigio il testo dell'archivio.
    /// </summary>
    [Fact]
    public void CmykToRgb_IlNeroDiSoloK_RestaNeroPuro()
    {
        using var converter = CmykToRgb.ForDeviceCmyk();

        Assert.Equal((0f, 0f, 0f), converter.Convert(0, 0, 0, 1));
        Assert.Equal((0.5f, 0.5f, 0.5f), converter.Convert(0, 0, 0, 0.5f));
        Assert.Equal((1f, 1f, 1f), converter.Convert(0, 0, 0, 0));

        // Il "nero ricco" (con anche C, M, Y) non è nero puro e passa dal profilo.
        var rich = converter.Convert(0.6f, 0.4f, 0.4f, 1);
        Assert.True(rich.R < 0.2 && rich.G < 0.2 && rich.B < 0.2, $"nero ricco: {rich}");
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
