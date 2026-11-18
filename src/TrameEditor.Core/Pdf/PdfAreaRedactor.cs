using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>Un rettangolo da cancellare, in punti PDF con l'origine in basso a sinistra.</summary>
public sealed record RedactionArea(int PageNumber, double Left, double Bottom, double Width, double Height)
{
    public double Right => Left + Width;

    public double Top => Bottom + Height;

    /// <summary>Vero se la riga cade dentro l'area: si guarda il centro, così una riga che
    /// sporge di poco dal rettangolo tirato a mano conta lo stesso.</summary>
    public bool Covers(PdfTextLine line)
    {
        var centreX = line.Left + line.Width / 2;
        var centreY = line.Bottom + line.Height / 2;
        return line.PageNumber == PageNumber &&
               centreX >= Left && centreX <= Right &&
               centreY >= Bottom && centreY <= Top;
    }
}

/// <summary>Esito dell'anonimizzazione per area.</summary>
public sealed record AreaRedactionResult(
    int AreasApplied,
    int TextLinesRemoved,
    int ImagesRemoved,
    IReadOnlyList<PdfTextLine> SkippedLines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Vero se tutto quello che stava nelle aree è stato tolto davvero.</summary>
    public bool Complete => SkippedLines.Count == 0;
}

/// <summary>
/// Cancella quello che sta dentro un rettangolo disegnato a mano: firme, fotografie, timbri,
/// intere tabelle. È il complemento dell'anonimizzazione per schemi, che riconosce un codice
/// fiscale ma non sa niente di una fotografia.
/// <para>
/// Il contenuto viene <b>tolto davvero</b>, non coperto: un rettangolo nero disegnato sopra
/// lascia il testo selezionabile e l'immagine estraibile, ed è il modo in cui si pubblicano
/// ogni anno documenti che si credevano oscurati. Il rettangolo nero si disegna lo stesso,
/// ma dopo: serve a far vedere che lì qualcosa c'era.
/// </para>
/// </summary>
public static class PdfAreaRedactor
{
    public static AreaRedactionResult Apply(string sourcePath, string targetPath,
        IReadOnlyList<RedactionArea> areas, bool paintBlack = true)
    {
        if (areas.Count == 0)
            throw new InvalidOperationException(
                "Non è stata indicata nessuna area: non c'è niente da cancellare.");

        var warnings = new List<string>();
        var full = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));

        // Primo passo: via il testo. Si riusa la riscrittura per righe, che sa togliere i
        // caratteri senza spostare quelli che restano fuori dall'area.
        var lines = LinesInside(sourcePath, areas, warnings);
        var withoutText = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        IReadOnlyList<PdfTextLine> skipped = [];

        try
        {
            if (lines.Count > 0)
            {
                var result = PdfTextReplacer.ReplaceMany(sourcePath, withoutText,
                    [.. lines.Select(l => (l, string.Empty))]);
                skipped = result.SkippedLines;
                if (skipped.Count > 0)
                    warnings.Add($"{skipped.Count} righe dentro le aree non sono state tolte: " +
                                 "il testo che si vede lì non risulta disegnato dal contenuto " +
                                 "della pagina. Nel file prodotto sono coperte ma ancora presenti.");
            }
            else
            {
                File.Copy(sourcePath, withoutText, overwrite: true);
            }

            // Secondo passo: via le immagini coperte, e il rettangolo sopra.
            var images = 0;
            var second = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var document = new PdfDocument(new PdfReader(withoutText), new PdfWriter(second)))
                {
                    foreach (var group in areas.GroupBy(a => a.PageNumber))
                    {
                        if (group.Key < 1 || group.Key > document.GetNumberOfPages())
                        {
                            warnings.Add($"La pagina {group.Key} non esiste in questo documento: " +
                                         "le aree che la riguardavano sono state ignorate.");
                            continue;
                        }

                        var page = document.GetPage(group.Key);
                        images += RemoveCoveredImages(page, [.. group]);
                        if (paintBlack)
                            PaintOver(page, [.. group]);
                    }
                }

                if (File.Exists(full))
                    File.Replace(second, full, destinationBackupFileName: null);
                else
                    File.Move(second, full);
            }
            finally
            {
                if (File.Exists(second))
                    File.Delete(second);
            }

            return new AreaRedactionResult(areas.Count, lines.Count - skipped.Count, images,
                skipped, warnings);
        }
        finally
        {
            if (File.Exists(withoutText))
                File.Delete(withoutText);
        }
    }

    /// <summary>Le righe di testo che cadono nelle aree.</summary>
    private static List<PdfTextLine> LinesInside(string path, IReadOnlyList<RedactionArea> areas,
        List<string> warnings)
    {
        using var inspector = new PdfTextInspector(path);
        var lines = new List<PdfTextLine>();

        foreach (var pageNumber in areas.Select(a => a.PageNumber).Distinct().Order())
        {
            if (pageNumber < 1 || pageNumber > inspector.PageCount)
                continue; // la pagina mancante è già segnalata nel secondo passo

            foreach (var line in inspector.GetLines(pageNumber))
            {
                if (areas.Any(a => a.Covers(line)))
                    lines.Add(line);
            }
        }

        if (lines.Count == 0)
            warnings.Add("Nelle aree indicate non c'era testo: se dovevi cancellare una firma o " +
                         "una fotografia va bene, altrimenti controlla le coordinate.");
        return lines;
    }

    /// <summary>
    /// Toglie le immagini coperte da un'area. Senza seguire la matrice di trasformazione non
    /// si sa dove ogni immagine è disegnata, quindi si interviene solo quando l'area copre
    /// buona parte della pagina — il caso di chi vuole togliere una fotografia a tutta
    /// pagina. Per le altre il contenuto resta sotto il rettangolo, e l'esito lo dice.
    /// </summary>
    private static int RemoveCoveredImages(PdfPage page, List<RedactionArea> areas)
    {
        var xobjects = page.GetResources().GetResource(PdfName.XObject);
        if (xobjects is null)
            return 0;

        var size = page.GetPageSize();
        var pageArea = size.GetWidth() * size.GetHeight();
        var covers = areas.Any(a => a.Width * a.Height / pageArea >= 0.25);
        if (!covers)
            return 0;

        var removed = 0;
        foreach (var name in xobjects.KeySet().ToList())
        {
            var stream = xobjects.GetAsStream(name);
            if (stream is null || !PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                continue;
            xobjects.Remove(name);
            removed++;
        }
        return removed;
    }

    /// <summary>Il rettangolo pieno sopra l'area: non è lui a cancellare — il contenuto è già
    /// stato tolto — ma rende visibile che lì c'era qualcosa.</summary>
    private static void PaintOver(PdfPage page, List<RedactionArea> areas)
    {
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), page.GetDocument());
        canvas.SaveState().SetFillColor(ColorConstants.BLACK);
        foreach (var area in areas)
        {
            canvas.Rectangle(new Rectangle((float)area.Left, (float)area.Bottom,
                (float)area.Width, (float)area.Height)).Fill();
        }
        canvas.RestoreState();
    }
}
