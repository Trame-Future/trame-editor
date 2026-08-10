using iText.Kernel.Exceptions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Esame preventivo di un PDF rispetto ai vincoli PDF/A-2: dice <b>prima</b> di
/// convertire che cosa non va, che cosa sistemeremo noi e che cosa impedisce del
/// tutto la conversione fedele. Lo stesso esame viene rieseguito sul file
/// prodotto come verifica interna.
/// </summary>
public static class PdfAAnalyzer
{
    public static PdfAAnalysisReport Analyze(string path)
    {
        var issues = new List<PdfAIssue>();
        var nonEmbedded = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var withoutUnicode = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var reader = new PdfReader(path);
        reader.SetUnethicalReading(true);
        PdfDocument document;
        try
        {
            document = new PdfDocument(reader);
        }
        catch (BadPasswordException)
        {
            CloseQuietly(reader);
            return new PdfAAnalysisReport(
                [new PdfAIssue(PdfAIssueSeverity.Bloccante,
                    "Il PDF è protetto da password: aprilo con la sua password prima di convertirlo.")],
                [], [], 0);
        }

        using (document)
        {
            if (reader.IsEncrypted())
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    "Il documento è cifrato: il PDF/A non ammette cifratura, la copia prodotta non sarà protetta."));

            if (document.GetPdfVersion().CompareTo(PdfVersion.PDF_1_7) > 0)
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    $"Versione PDF {document.GetPdfVersion()}: sarà riportata a 1.7, richiesta da PDF/A-2."));

            InspectCatalog(document, issues);

            var pageCount = document.GetNumberOfPages();
            for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
            {
                var page = document.GetPage(pageNumber);

                PdfPageScan? scan = null;
                try
                {
                    scan = PdfPageScan.Run(page);
                }
                catch (Exception ex)
                {
                    issues.Add(new PdfAIssue(PdfAIssueSeverity.Bloccante,
                        $"Contenuto della pagina non analizzabile ({ex.GetType().Name}): non possiamo " +
                        "garantire una conversione fedele.", $"pagina {pageNumber}"));
                }

                InspectFonts(page, pageNumber, scan, issues, nonEmbedded, withoutUnicode);
                InspectAnnotations(page, pageNumber, issues);
                InspectColorsAndFilters(page, pageNumber, scan, issues);
            }

            return new PdfAAnalysisReport(issues, [.. nonEmbedded], [.. withoutUnicode], pageCount);
        }
    }

    private static void CloseQuietly(PdfReader reader)
    {
        try
        {
            reader.Close();
        }
        catch
        {
            // già chiuso o mai aperto
        }
    }

    // ----- Catalogo -----

    private static void InspectCatalog(PdfDocument document, List<PdfAIssue> issues)
    {
        var catalog = document.GetCatalog().GetPdfObject();

        var names = catalog.GetAsDictionary(PdfName.Names);
        if (names?.ContainsKey(PdfName.JavaScript) == true)
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Il documento contiene JavaScript: sarà rimosso (vietato in PDF/A)."));
        if (names?.ContainsKey(PdfName.EmbeddedFiles) == true)
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Il documento contiene file allegati: saranno rimossi (PDF/A-2 non li ammette)."));

        if (catalog.GetAsDictionary(PdfName.OpenAction) is { } openAction &&
            PdfName.JavaScript.Equals(openAction.GetAsName(PdfName.S)))
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Il documento esegue uno script all'apertura: sarà rimosso."));

        if (catalog.ContainsKey(PdfName.AA))
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Il documento contiene azioni automatiche: saranno rimosse."));

        var acroForm = catalog.GetAsDictionary(PdfName.AcroForm);
        if (acroForm?.ContainsKey(PdfName.XFA) == true)
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Modulo XFA (modulo dinamico): non è ammesso in PDF/A, il modulo sarà appiattito."));
        else if (acroForm is not null)
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "Il documento è un modulo compilabile: sarà appiattito (i valori restano, i campi non saranno più modificabili)."));
    }

    // ----- Font -----

    private static void InspectFonts(PdfPage page, int pageNumber, PdfPageScan? scan,
        List<PdfAIssue> issues, SortedSet<string> nonEmbedded, SortedSet<string> withoutUnicode)
    {
        foreach (var font in CollectFontDictionaries(page))
        {
            // Senza lettura del contenuto non sappiamo quali caratteri siano usati:
            // in quel caso la sostituzione del font non è verificabile.
            var charactersUsed = scan?.CharactersUsedBy(font) ?? [];
            var usage = PdfAFontPolicy.Inspect(font, charactersUsed);

            // Un font dichiarato fra le risorse ma non usato non pone problemi.
            if (scan is not null && charactersUsed.Count == 0)
                continue;

            if (!usage.IsEmbedded)
            {
                nonEmbedded.Add(usage.Name);
                if (usage.SubstitutePath is not null)
                {
                    issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                        $"Font «{usage.Name}» non incorporato: sarà incorporato usando " +
                        $"{Path.GetFileName(usage.SubstitutePath)} dal computer.",
                        $"pagina {pageNumber}"));
                }
                else
                {
                    issues.Add(new PdfAIssue(PdfAIssueSeverity.Bloccante,
                        $"Font «{usage.Name}» non incorporato e non sostituibile in sicurezza: " +
                        "il PDF/A esige che ogni font sia incorporato.",
                        $"pagina {pageNumber}"));
                }
            }

            if (!usage.HasUnicodeMapping)
            {
                withoutUnicode.Add(usage.Name);
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Declassa,
                    $"Font «{usage.Name}» senza mappatura Unicode: il testo non è estraibile in modo " +
                    "affidabile, quindi il livello sarà PDF/A-2b invece di 2u.",
                    $"pagina {pageNumber}"));
            }
        }
    }

    /// <summary>Tutti i font della pagina, compresi quelli usati solo dentro i
    /// form XObject (che hanno risorse proprie).</summary>
    internal static IEnumerable<PdfDictionary> CollectFontDictionaries(PdfPage page)
    {
        var found = new List<PdfDictionary>();
        var seen = new HashSet<PdfObject>();
        foreach (var resources in CollectResourceDictionaries(page))
        {
            if (resources.GetAsDictionary(PdfName.Font) is not { } fonts)
                continue;
            foreach (var key in fonts.KeySet())
            {
                if (fonts.GetAsDictionary(key) is { } font && seen.Add(font))
                    found.Add(font);
            }
        }
        return found;
    }

    /// <summary>Le risorse della pagina e quelle proprie dei form XObject annidati.</summary>
    internal static IEnumerable<PdfDictionary> CollectResourceDictionaries(PdfPage page)
    {
        var visited = new HashSet<PdfObject>();
        var found = new List<PdfDictionary>();
        Walk(page.GetResources().GetPdfObject(), visited, found);
        return found;
    }

    private static void Walk(PdfDictionary? resources, HashSet<PdfObject> visited, List<PdfDictionary> found)
    {
        if (resources is null || !visited.Add(resources))
            return;
        found.Add(resources);

        var xobjects = resources.GetAsDictionary(PdfName.XObject);
        if (xobjects is null)
            return;
        foreach (var key in xobjects.KeySet())
        {
            if (xobjects.GetAsStream(key) is { } stream && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype)))
                Walk(stream.GetAsDictionary(PdfName.Resources), visited, found);
        }
    }

    // ----- Annotazioni -----

    private static readonly PdfName[] ProhibitedAnnotationSubtypes =
    [
        PdfName.FileAttachment, PdfName.Sound, PdfName.Movie, PdfName.Screen, new PdfName("3D"),
        new PdfName("RichMedia"),
    ];

    private static readonly PdfName[] ProhibitedActions =
    [
        PdfName.Launch, PdfName.Sound, PdfName.Movie, PdfName.ResetForm,
        PdfName.ImportData, PdfName.JavaScript,
    ];

    private static void InspectAnnotations(PdfPage page, int pageNumber, List<PdfAIssue> issues)
    {
        var annotations = page.GetPdfObject().GetAsArray(PdfName.Annots);
        if (annotations is null)
            return;

        for (var i = 0; i < annotations.Size(); i++)
        {
            var annotation = annotations.GetAsDictionary(i);
            if (annotation is null)
                continue;
            var subtype = annotation.GetAsName(PdfName.Subtype);

            if (subtype is not null && ProhibitedAnnotationSubtypes.Contains(subtype))
            {
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    $"Annotazione di tipo {subtype.GetValue()} non ammessa in PDF/A: sarà rimossa.",
                    $"pagina {pageNumber}"));
                continue;
            }

            if (annotation.GetAsDictionary(PdfName.A)?.GetAsName(PdfName.S) is { } action &&
                ProhibitedActions.Contains(action))
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    $"Annotazione con azione {action.GetValue()} non ammessa: l'azione sarà rimossa.",
                    $"pagina {pageNumber}"));

            var flags = annotation.GetAsNumber(PdfName.F)?.IntValue() ?? 0;
            const int hidden = 2, print = 4, noView = 32;
            if ((flags & print) == 0 || (flags & hidden) != 0 || (flags & noView) != 0)
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    "Annotazione con contrassegni non conformi (deve essere stampabile e visibile): saranno corretti.",
                    $"pagina {pageNumber}"));

            var needsAppearance = subtype is not null &&
                !PdfName.Popup.Equals(subtype) && !PdfName.Link.Equals(subtype);
            if (needsAppearance && !annotation.ContainsKey(PdfName.AP))
                issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                    "Annotazione senza aspetto grafico definito: sarà rimossa (il PDF/A esige un aspetto fisso).",
                    $"pagina {pageNumber}"));
        }
    }

    // ----- Colori e filtri -----

    private static void InspectColorsAndFilters(PdfPage page, int pageNumber, PdfPageScan? scan,
        List<PdfAIssue> issues)
    {
        if (UsesLzw(page))
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Bloccante,
                "Il contenuto usa la compressione LZW, vietata in PDF/A.", $"pagina {pageNumber}"));

        var blockers = FindCmykBlockers(page, scan);
        foreach (var blocker in blockers)
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Bloccante, blocker, $"pagina {pageNumber}"));

        if (blockers.Count == 0 && (scan?.UsesCmyk == true || ResourcesUseCmyk(page)))
            issues.Add(new PdfAIssue(PdfAIssueSeverity.Corretto,
                "La pagina usa colori CMYK: saranno convertiti in sRGB, il profilo di destinazione " +
                "del PDF/A. La conversione è colorimetrica ma la sorgente CMYK è un'assunzione " +
                "standard, quindi qualche colore può cambiare leggermente.",
                $"pagina {pageNumber}"));
    }

    /// <summary>I casi di CMYK che non sappiamo tradurre: meglio dirlo che
    /// consegnare un archivio con i colori sbagliati.</summary>
    private static List<string> FindCmykBlockers(PdfPage page, PdfPageScan? scan)
    {
        var blockers = new List<string>();

        if (scan?.UsesCmykInlineImage == true)
            blockers.Add("La pagina contiene un'immagine CMYK scritta dentro il flusso di contenuto: " +
                "non è convertibile in sRGB.");

        foreach (var resources in CollectResourceDictionaries(page))
        {
            var xobjects = resources.GetAsDictionary(PdfName.XObject);
            if (xobjects is not null && xobjects.KeySet().Any(key =>
                    xobjects.GetAsStream(key) is { } stream &&
                    PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)) &&
                    IsCmyk(ResolveColorSpace(stream.Get(PdfName.ColorSpace), resources)) &&
                    IsCompressedPhoto(stream)))
            {
                blockers.Add("La pagina contiene un'immagine JPEG in CMYK: non è decodificabile in " +
                    "modo affidabile, quindi non la convertiamo in sRGB.");
            }

            var shadings = resources.GetAsDictionary(PdfName.Shading);
            if (shadings is not null && shadings.KeySet().Any(key =>
                    IsCmyk(ResolveColorSpace(
                        shadings.GetAsDictionary(key)?.Get(PdfName.ColorSpace) ??
                        shadings.GetAsStream(key)?.Get(PdfName.ColorSpace), resources))))
            {
                blockers.Add("La pagina contiene una sfumatura definita in CMYK: la conversione in " +
                    "sRGB richiederebbe di ricostruirla, e non lo facciamo.");
            }
        }

        return [.. blockers.Distinct()];
    }

    /// <summary>Uno spazio colore può essere scritto per esteso o richiamato per
    /// nome dalle risorse.</summary>
    private static PdfObject? ResolveColorSpace(PdfObject? colorSpace, PdfDictionary resources)
    {
        if (colorSpace is not PdfName name)
            return colorSpace;
        return resources.GetAsDictionary(PdfName.ColorSpace)?.Get(name) ?? colorSpace;
    }

    private static bool IsCompressedPhoto(PdfStream image)
    {
        var filter = image.Get(PdfName.Filter);
        return filter switch
        {
            PdfName name => PdfName.DCTDecode.Equals(name) || PdfName.JPXDecode.Equals(name),
            PdfArray array => Enumerable.Range(0, array.Size()).Any(i =>
                PdfName.DCTDecode.Equals(array.GetAsName(i)) ||
                PdfName.JPXDecode.Equals(array.GetAsName(i))),
            _ => false,
        };
    }

    private static bool UsesLzw(PdfPage page)
    {
        var contents = page.GetPdfObject().Get(PdfName.Contents);
        var streams = contents switch
        {
            PdfStream stream => [stream],
            PdfArray array => Enumerable.Range(0, array.Size())
                .Select(array.GetAsStream).Where(s => s is not null).Cast<PdfStream>().ToArray(),
            _ => Array.Empty<PdfStream>(),
        };
        return streams.Any(HasLzwFilter);
    }

    private static bool HasLzwFilter(PdfStream stream)
    {
        var filter = stream.Get(PdfName.Filter);
        return filter switch
        {
            PdfName name => PdfName.LZWDecode.Equals(name),
            PdfArray array => Enumerable.Range(0, array.Size())
                .Any(i => PdfName.LZWDecode.Equals(array.GetAsName(i))),
            _ => false,
        };
    }

    private static bool ResourcesUseCmyk(PdfPage page)
    {
        foreach (var resources in CollectResourceDictionaries(page))
        {
            var colorSpaces = resources.GetAsDictionary(PdfName.ColorSpace);
            if (colorSpaces is not null && colorSpaces.KeySet().Any(key => IsCmyk(colorSpaces.Get(key))))
                return true;

            var xobjects = resources.GetAsDictionary(PdfName.XObject);
            if (xobjects is not null && xobjects.KeySet().Any(key =>
                    xobjects.GetAsStream(key) is { } stream &&
                    PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)) &&
                    IsCmyk(ResolveColorSpace(stream.Get(PdfName.ColorSpace), resources))))
                return true;
        }
        return false;
    }

    private static bool IsCmyk(PdfObject? colorSpace)
    {
        switch (colorSpace)
        {
            case PdfName name:
                return PdfName.DeviceCMYK.Equals(name);
            case PdfArray array when array.Size() >= 2:
                var family = array.GetAsName(0);
                if (PdfName.ICCBased.Equals(family))
                    return array.GetAsStream(1)?.GetAsNumber(PdfName.N)?.IntValue() == 4;
                if (PdfName.Separation.Equals(family) || PdfName.DeviceN.Equals(family))
                    return array.Size() >= 3 && IsCmyk(array.Get(2));
                if (PdfName.Indexed.Equals(family))
                    return IsCmyk(array.Get(1));
                return false;
            default:
                return false;
        }
    }

}
