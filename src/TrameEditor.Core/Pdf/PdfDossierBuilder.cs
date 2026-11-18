using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Navigation;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>Un documento dentro il fascicolo.</summary>
public sealed record DossierEntry(string Title, int FirstPage, int PageCount);

/// <summary>Esito della costruzione del fascicolo.</summary>
public sealed record DossierResult(
    IReadOnlyList<DossierEntry> Entries,
    int TotalPages,
    bool HasIndexPage,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Unisce più PDF in un fascicolo consultabile: segnalibri (uno per documento) e, se
/// richiesta, una pagina di indice con i titoli cliccabili.
/// <para>
/// È la differenza fra un malloppo e un documento: unire e basta lo fa già "Unisci PDF",
/// ma su venti allegati nessuno ritrova più niente senza un indice.
/// </para>
/// <para>
/// I numeri di pagina nell'indice tengono conto dell'indice stesso: l'indice è la prima
/// pagina, quindi tutto il resto scala di uno. Scriverli senza quella correzione darebbe
/// rimandi sbagliati di una pagina, che è il modo più silenzioso di rendere inutile un indice.
/// </para>
/// </summary>
public static class PdfDossierBuilder
{
    public static DossierResult Build(IReadOnlyList<string> sourcePaths, string targetPath,
        bool withIndexPage = true, string indexTitle = "Indice")
    {
        if (sourcePaths.Count < 2)
            throw new ArgumentException("Per fare un fascicolo servono almeno due PDF.", nameof(sourcePaths));

        var warnings = new List<string>();
        var full = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

        // Prima si contano le pagine di ciascun documento: l'indice va scritto sapendo già
        // dove comincia ognuno, e l'indice stesso occupa una pagina.
        var entries = new List<DossierEntry>();
        var offset = withIndexPage ? 1 : 0;
        var next = offset + 1;

        foreach (var source in sourcePaths)
        {
            int pages;
            try
            {
                using var reader = new PdfDocument(new PdfReader(source));
                pages = reader.GetNumberOfPages();
            }
            catch (Exception ex)
            {
                warnings.Add($"«{Path.GetFileName(source)}» non è stato incluso: {ex.Message}");
                continue;
            }

            entries.Add(new DossierEntry(Path.GetFileNameWithoutExtension(source), next, pages));
            next += pages;
        }

        if (entries.Count < 2)
            throw new InvalidOperationException(
                "Dopo aver scartato i file illeggibili restano meno di due documenti: " +
                "non c'è un fascicolo da fare.");

        try
        {
            using (var writer = new PdfWriter(temporary))
            using (var dossier = new PdfDocument(writer))
            {
                if (withIndexPage)
                    WriteIndexPage(dossier, entries, indexTitle);

                foreach (var source in sourcePaths)
                {
                    if (!entries.Any(e => e.Title == Path.GetFileNameWithoutExtension(source)))
                        continue; // scartato prima perché illeggibile: già dichiarato negli avvisi
                    using var reader = new PdfDocument(new PdfReader(source));
                    reader.CopyPagesTo(1, reader.GetNumberOfPages(), dossier);
                }

                AddBookmarks(dossier, entries);
                if (withIndexPage)
                    LinkIndexEntries(dossier, entries);
            }

            if (File.Exists(full))
                File.Replace(temporary, full, destinationBackupFileName: null);
            else
                File.Move(temporary, full);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        var total = entries.Sum(e => e.PageCount) + offset;
        return new DossierResult(entries, total, withIndexPage, warnings);
    }

    /// <summary>La pagina d'indice: titolo, poi una riga per documento con il numero di
    /// pagina. I rimandi cliccabili si aggiungono dopo, quando le pagine esistono.</summary>
    private static void WriteIndexPage(PdfDocument dossier, List<DossierEntry> entries, string title)
    {
        var page = dossier.AddNewPage(PageSize.A4);
        var canvas = new PdfCanvas(page);
        var bold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
        var regular = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

        canvas.BeginText().SetFontAndSize(bold, 18).MoveText(60, 780).ShowText(title).EndText();

        var y = 740.0;
        foreach (var entry in entries)
        {
            if (y < 60)
            {
                // Più documenti di quanti ne stiano in una pagina: il resto non si perde, ma
                // l'indice non va a capo su una seconda pagina perché cambierebbe tutti i
                // numeri già calcolati. Si dichiara invece di sbagliare i rimandi.
                canvas.BeginText().SetFontAndSize(regular, 10).MoveText(60, 45)
                    .ShowText("(l'elenco continua: usa i segnalibri per i documenti successivi)").EndText();
                break;
            }

            canvas.BeginText().SetFontAndSize(regular, 11).MoveText(60, y)
                .ShowText(Shorten(entry.Title, 70)).EndText();
            canvas.BeginText().SetFontAndSize(regular, 11).MoveText(480, y)
                .ShowText($"pag. {entry.FirstPage}").EndText();

            canvas.SetStrokeColor(new DeviceRgb(200, 200, 200)).SetLineWidth(0.4f)
                .MoveTo(60, y - 4).LineTo(535, y - 4).Stroke();
            y -= 22;
        }
    }

    private static void AddBookmarks(PdfDocument dossier, List<DossierEntry> entries)
    {
        var root = dossier.GetOutlines(false);
        foreach (var entry in entries)
        {
            if (entry.FirstPage > dossier.GetNumberOfPages())
                continue;
            var outline = root.AddOutline(entry.Title);
            outline.AddDestination(PdfExplicitDestination.CreateFit(dossier.GetPage(entry.FirstPage)));
        }
    }

    /// <summary>Rende cliccabili le righe dell'indice.</summary>
    private static void LinkIndexEntries(PdfDocument dossier, List<DossierEntry> entries)
    {
        var first = dossier.GetPage(1);
        var y = 740.0;
        foreach (var entry in entries)
        {
            if (y < 60 || entry.FirstPage > dossier.GetNumberOfPages())
                break;

            var area = new Rectangle(55, (float)y - 6, 485, 20);
            var link = new iText.Kernel.Pdf.Annot.PdfLinkAnnotation(area)
                .SetAction(PdfAction.CreateGoTo(
                    PdfExplicitDestination.CreateFit(dossier.GetPage(entry.FirstPage))));
            link.SetBorder(new PdfArray(new float[] { 0, 0, 0 }));
            first.AddAnnotation(link);
            y -= 22;
        }
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
