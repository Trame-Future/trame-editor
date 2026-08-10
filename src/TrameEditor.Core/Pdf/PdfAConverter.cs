using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using iText.Forms;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Pdfa;
using Tesseract;
using Encoder = System.Drawing.Imaging.Encoder;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using Path = System.IO.Path;
using Rectangle = iText.Kernel.Geom.Rectangle;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Conversione di un PDF in <b>PDF/A-2</b>, il formato dell'archiviazione a lungo
/// termine. Due percorsi, entrambi dichiarati all'utente:
/// <list type="bullet">
/// <item><b>Fedele</b>: il documento è riscritto conservando il testo vettoriale.
/// Possibile solo se ogni font è incorporato (o incorporabile senza cambiare la
/// resa) e non ci sono ostacoli insormontabili.</item>
/// <item><b>Raster</b>: le pagine diventano immagini con sopra un layer di testo
/// invisibile prodotto dall'OCR. Riesce sempre, ma il testo originale è perduto.</item>
/// </list>
/// La verifica finale che facciamo è interna (rieseguiamo l'analisi sul file
/// prodotto): <b>non</b> è una validazione formale, che va fatta con veraPDF.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PdfAConverter
{
    // ----- Percorso fedele -----

    /// <summary>
    /// Riscrive il PDF come PDF/A-2 conservando il testo.
    /// </summary>
    /// <exception cref="PdfAConversionException">Il documento ha ostacoli che non
    /// sappiamo togliere senza rasterizzare: il messaggio li elenca.</exception>
    public static PdfAConversionResult ConvertFaithfully(string sourcePath, string targetPath,
        string? title = null)
    {
        var analysis = PdfAAnalyzer.Analyze(sourcePath);
        if (!analysis.CanConvertFaithfully)
        {
            throw new PdfAConversionException(
                "Conversione fedele non possibile:" + Environment.NewLine +
                string.Join(Environment.NewLine, analysis.Blocking.Select(i => $"• {i}")));
        }

        var changes = new List<string>();
        var level = analysis.BestLevel;
        var prepared = NewTempFile();
        try
        {
            PrepareSource(sourcePath, prepared, changes);

            WriteAtomic(targetPath, tempPath =>
            {
                using var source = new PdfDocument(new PdfReader(prepared));
                var writerProperties = new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7);
                using var destination = new PdfADocument(new PdfWriter(tempPath, writerProperties),
                    level == PdfALevel.A2u ? PdfAConformance.PDF_A_2U : PdfAConformance.PDF_A_2B,
                    CreateOutputIntent());

                source.CopyPagesTo(1, source.GetNumberOfPages(), destination);
                EmbedMissingFonts(destination, changes);

                destination.GetDocumentInfo().SetTitle(
                    title ?? Path.GetFileNameWithoutExtension(sourcePath));
            });
        }
        finally
        {
            DeleteQuietly(prepared);
        }

        changes.Add($"profilo colore di destinazione {SrgbColorProfile.OutputCondition} incorporato");
        return new PdfAConversionResult(PdfAConversionMethod.Fedele, level, changes,
            PdfAAnalyzer.Analyze(targetPath));
    }

    /// <summary>
    /// Toglie dal documento tutto ciò che il PDF/A non ammette e che possiamo
    /// togliere senza toccare il contenuto visibile.
    /// </summary>
    private static void PrepareSource(string sourcePath, string targetPath, List<string> changes)
    {
        var reader = new PdfReader(sourcePath);
        reader.SetUnethicalReading(true);
        using var document = new PdfDocument(reader, new PdfWriter(targetPath));
        var catalog = document.GetCatalog().GetPdfObject();

        var acroForm = PdfAcroForm.GetAcroForm(document, false);
        if (acroForm is not null)
        {
            catalog.GetAsDictionary(PdfName.AcroForm)?.Remove(PdfName.XFA);
            acroForm.SetNeedAppearances(false);
            try
            {
                acroForm.FlattenFields();
                changes.Add("modulo appiattito: i valori restano, i campi non sono più compilabili");
            }
            catch (Exception ex)
            {
                throw new PdfAConversionException(
                    $"Il modulo del documento non è appiattibile ({ex.Message}): " +
                    "prova la conversione per immagine.");
            }
            catalog.Remove(PdfName.AcroForm);
        }

        var names = catalog.GetAsDictionary(PdfName.Names);
        if (names is not null)
        {
            if (names.Remove(PdfName.JavaScript) is not null)
                changes.Add("JavaScript rimosso");
            if (names.Remove(PdfName.EmbeddedFiles) is not null)
                changes.Add("file allegati rimossi");
        }

        if (catalog.GetAsDictionary(PdfName.OpenAction) is { } openAction &&
            PdfName.JavaScript.Equals(openAction.GetAsName(PdfName.S)))
        {
            catalog.Remove(PdfName.OpenAction);
            changes.Add("script all'apertura rimosso");
        }

        if (catalog.Remove(PdfName.AA) is not null)
            changes.Add("azioni automatiche rimosse");

        for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
            CleanAnnotations(document.GetPage(pageNumber), changes);

        // I colori CMYK non sarebbero definiti sotto un profilo di destinazione sRGB.
        var colors = PdfCmykConverter.Convert(document);
        if (colors.ColorsConverted > 0 || colors.ImagesConverted > 0)
        {
            var what = colors.ImagesConverted == 0
                ? $"{colors.ColorsConverted} colori CMYK"
                : $"{colors.ColorsConverted} colori e {colors.ImagesConverted} immagini CMYK";
            changes.Add($"{what} convertiti in sRGB — {colors.SourceDescription}");
        }

        catalog.SetModified();
    }

    private static readonly PdfName[] ProhibitedAnnotationSubtypes =
    [
        PdfName.FileAttachment, PdfName.Sound, PdfName.Movie, PdfName.Screen,
        new PdfName("3D"), new PdfName("RichMedia"),
    ];

    private static readonly PdfName[] ProhibitedActions =
    [
        PdfName.Launch, PdfName.Sound, PdfName.Movie, PdfName.ResetForm,
        PdfName.ImportData, PdfName.JavaScript,
    ];

    private static void CleanAnnotations(PdfPage page, List<string> changes)
    {
        var annotations = page.GetPdfObject().GetAsArray(PdfName.Annots);
        if (annotations is null)
            return;

        var removed = 0;
        for (var i = annotations.Size() - 1; i >= 0; i--)
        {
            var annotation = annotations.GetAsDictionary(i);
            if (annotation is null)
            {
                annotations.Remove(i);
                continue;
            }

            var subtype = annotation.GetAsName(PdfName.Subtype);
            var needsAppearance = subtype is not null &&
                !PdfName.Popup.Equals(subtype) && !PdfName.Link.Equals(subtype);

            if ((subtype is not null && ProhibitedAnnotationSubtypes.Contains(subtype)) ||
                (needsAppearance && !annotation.ContainsKey(PdfName.AP)))
            {
                annotations.Remove(i);
                removed++;
                continue;
            }

            if (annotation.GetAsDictionary(PdfName.A)?.GetAsName(PdfName.S) is { } action &&
                ProhibitedActions.Contains(action))
                annotation.Remove(PdfName.A);
            annotation.Remove(PdfName.AA);

            const int hidden = 2, print = 4, noView = 32;
            var flags = annotation.GetAsNumber(PdfName.F)?.IntValue() ?? 0;
            var fixedFlags = (flags | print) & ~hidden & ~noView;
            if (fixedFlags != flags)
                annotation.Put(PdfName.F, new PdfNumber(fixedFlags));

            // L'opacità diversa da 1 non è ammessa: la riportiamo al valore di default.
            if (annotation.GetAsNumber(PdfName.CA) is { } opacity &&
                Math.Abs(opacity.DoubleValue() - 1.0) > 0.0001)
                annotation.Remove(PdfName.CA);

            annotation.SetModified();
        }

        if (annotations.IsEmpty())
            page.GetPdfObject().Remove(PdfName.Annots);
        page.GetPdfObject().SetModified();

        if (removed > 0)
            changes.Add($"{removed} annotazioni non ammesse rimosse (pagina {page.GetDocument().GetPageNumber(page)})");
    }

    private static void EmbedMissingFonts(PdfDocument document, List<string> changes)
    {
        // Un font può comparire su più pagine: la compatibilità va verificata
        // sull'insieme di TUTTI i caratteri che gli sono affidati nel documento,
        // non su quelli di una pagina sola.
        var charactersByFont = new Dictionary<PdfDictionary, HashSet<int>>();
        for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
        {
            var page = document.GetPage(pageNumber);
            var scan = PdfPageScan.Run(page);
            foreach (var font in PdfAAnalyzer.CollectFontDictionaries(page))
            {
                var used = scan.CharactersUsedBy(font);
                if (used.Count == 0 || PdfAFontPolicy.IsEmbedded(font))
                    continue;
                if (!charactersByFont.TryGetValue(font, out var all))
                    charactersByFont[font] = all = [];
                all.UnionWith(used);
            }
        }

        foreach (var (font, charactersUsed) in charactersByFont)
        {
            var name = PdfAFontPolicy.DisplayName(font);
            var substitute = PdfAFontPolicy.FindSubstitute(font, charactersUsed)
                ?? throw new PdfAConversionException(
                    $"Font «{name}» non incorporato e senza equivalente utilizzabile sul computer.");

            if (!PdfAFontEmbedder.TryEmbed(document, font, substitute, charactersUsed, out var reason))
                throw new PdfAConversionException($"Font «{name}» non incorporabile: {reason}.");

            changes.Add($"font «{name}» incorporato da {Path.GetFileName(substitute)}");
        }
    }

    // ----- Percorso raster -----

    /// <summary>
    /// Rasterizza ogni pagina e ricostruisce un PDF/A-2 di immagini, con un layer
    /// di testo invisibile prodotto dall'OCR quando disponibile. Riesce sempre: è
    /// il percorso per i documenti che non si possono convertire fedelmente.
    /// </summary>
    /// <param name="renderPagePng">Rende la pagina (1-based) come PNG.</param>
    /// <param name="tessdataPath">Cartella dei dati Tesseract; null ⇒ nessun layer di testo.</param>
    public static PdfAConversionResult ConvertByRasterizing(string sourcePath, string targetPath,
        Func<int, byte[]> renderPagePng, double renderScale, string? tessdataPath = null,
        string? title = null, string languages = "ita+eng", long jpegQuality = 85)
    {
        var changes = new List<string>
        {
            "pagine convertite in immagine: la resa è fissata, il testo originale non è più selezionabile",
        };

        var sizes = ReadPageSizes(sourcePath);
        var hasOcr = tessdataPath is not null && Directory.Exists(tessdataPath);
        var words = 0;

        WriteAtomic(targetPath, tempPath =>
        {
            var writerProperties = new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7);
            using var destination = new PdfADocument(new PdfWriter(tempPath, writerProperties),
                PdfAConformance.PDF_A_2U, CreateOutputIntent());

            TesseractEngine? engine = null;
            iText.Kernel.Font.PdfFont? ocrFont = null;
            try
            {
                if (hasOcr)
                {
                    engine = new TesseractEngine(tessdataPath, languages, EngineMode.Default);
                    ocrFont = CreateEmbeddedOcrFont();
                }

                for (var pageNumber = 1; pageNumber <= sizes.Count; pageNumber++)
                {
                    var png = renderPagePng(pageNumber);
                    var (width, height) = sizes[pageNumber - 1];
                    var page = destination.AddNewPage(new PageSize(width, height));
                    var image = ImageDataFactory.Create(ToJpeg(png, jpegQuality));
                    var canvas = new PdfCanvas(page);
                    canvas.AddImageFittedIntoRectangle(image, new Rectangle(0, 0, width, height),
                        asInline: false);

                    if (engine is not null && ocrFont is not null)
                        words += Ocr.PdfOcrService.DrawInvisibleWords(engine, png, canvas, ocrFont,
                            height, renderScale);
                }
            }
            finally
            {
                engine?.Dispose();
            }

            destination.GetDocumentInfo().SetTitle(
                title ?? Path.GetFileNameWithoutExtension(sourcePath));
        });

        changes.Add(hasOcr
            ? $"layer di testo invisibile da OCR: {words} parole riconosciute, il documento resta ricercabile"
            : "nessun OCR disponibile: il documento non sarà ricercabile");
        changes.Add($"profilo colore di destinazione {SrgbColorProfile.OutputCondition} incorporato");

        return new PdfAConversionResult(PdfAConversionMethod.Raster, PdfALevel.A2u, changes,
            PdfAAnalyzer.Analyze(targetPath));
    }

    private static List<(float Width, float Height)> ReadPageSizes(string sourcePath)
    {
        var reader = new PdfReader(sourcePath);
        reader.SetUnethicalReading(true);
        using var document = new PdfDocument(reader);
        var sizes = new List<(float, float)>();
        for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
        {
            var box = document.GetPage(pageNumber).GetPageSizeWithRotation();
            sizes.Add((box.GetWidth(), box.GetHeight()));
        }
        return sizes;
    }

    /// <summary>Il font del layer OCR deve essere incorporato: in PDF/A non
    /// esistono font "di sistema" impliciti, nemmeno per il testo invisibile.</summary>
    private static iText.Kernel.Font.PdfFont CreateEmbeddedOcrFont()
    {
        var candidates = new[] { "arial.ttf", "segoeui.ttf", "times.ttf", "cour.ttf" }
            .Select(name => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", name));
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new PdfAConversionException(
                "Nessun font di sistema disponibile per il layer di testo dell'OCR.");
        return iText.Kernel.Font.PdfFontFactory.CreateFont(path, iText.IO.Font.PdfEncodings.IDENTITY_H,
            iText.Kernel.Font.PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
    }

    private static byte[] ToJpeg(byte[] png, long quality)
    {
        using var source = Image.FromStream(new MemoryStream(png));
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        using var output = new MemoryStream();
        bitmap.Save(output, codec, parameters);
        return output.ToArray();
    }

    // ----- Comune -----

    private static PdfOutputIntent CreateOutputIntent() =>
        new("Custom", string.Empty, "http://www.color.org", SrgbColorProfile.OutputCondition,
            new MemoryStream(SrgbColorProfile.Load()));

    private static string NewTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrameEditor");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private static void WriteAtomic(string targetPath, Action<string> writeTo)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
        try
        {
            writeTo(tempPath);
            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }
}
