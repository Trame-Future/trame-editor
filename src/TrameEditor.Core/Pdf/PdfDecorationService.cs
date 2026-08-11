using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

public enum PageNumberPosition
{
    BassoCentro,
    BassoDestra,
    BassoSinistra,
    AltoCentro,
    AltoDestra,
}

/// <summary>Numerazione delle pagine. <c>{n}</c> è il numero, <c>{tot}</c> il totale.</summary>
public sealed record PageNumbering(
    string Format = "{n} / {tot}",
    PageNumberPosition Position = PageNumberPosition.BassoCentro,
    int StartAt = 1,
    bool SkipFirstPage = false,
    float FontSize = 9);

/// <summary>Scritta in diagonale sopra la pagina (COPIA, RISERVATO, BOZZA…).</summary>
public sealed record Watermark(
    string Text,
    float Opacity = 0.15f,
    float AngleDegrees = 45,
    float FontSize = 60);

/// <summary>Testo fisso in cima e in fondo a ogni pagina.</summary>
public sealed record HeaderFooter(string? Header = null, string? Footer = null, float FontSize = 9);

public sealed record DecorationResult(int PagesDecorated, IReadOnlyList<string> Applied);

/// <summary>
/// Aggiunge a un PDF quello che serve prima di consegnarlo: numeri di pagina,
/// una filigrana, un'intestazione o un piè di pagina.
/// <para>
/// Tutto viene <b>sovrapposto</b> al contenuto esistente con lo stesso canvas
/// protetto che usiamo per timbri e annotazioni: il contenuto originale non
/// viene toccato, e le decorazioni non ereditano lo stato grafico della pagina.
/// </para>
/// </summary>
public static class PdfDecorationService
{
    private const float Margin = 28f;

    public static DecorationResult Apply(string sourcePath, string targetPath,
        PageNumbering? numbering = null, Watermark? watermark = null, HeaderFooter? headerFooter = null)
    {
        if (numbering is null && watermark is null &&
            (headerFooter is null || (headerFooter.Header is null && headerFooter.Footer is null)))
            throw new ArgumentException("Nessuna decorazione richiesta.", nameof(numbering));

        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        var decorated = 0;
        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                var font = LoadFont();
                var total = document.GetNumberOfPages();
                for (var pageNumber = 1; pageNumber <= total; pageNumber++)
                {
                    var page = document.GetPage(pageNumber);
                    var canvas = PdfOverlayCanvas.Create(document, page);
                    var box = page.GetPageSizeWithRotation();
                    var touched = false;

                    if (watermark is not null)
                    {
                        DrawWatermark(canvas, font, watermark, box);
                        touched = true;
                    }
                    if (headerFooter is not null)
                        touched |= DrawHeaderFooter(canvas, font, headerFooter, box);
                    if (numbering is not null && !(numbering.SkipFirstPage && pageNumber == 1))
                    {
                        DrawPageNumber(canvas, font, numbering, box, pageNumber, total);
                        touched = true;
                    }

                    if (touched)
                        decorated++;
                }
            }

            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        var applied = new List<string>();
        if (numbering is not null)
            applied.Add("numeri di pagina");
        if (watermark is not null)
            applied.Add($"filigrana \"{watermark.Text}\"");
        if (headerFooter?.Header is not null)
            applied.Add("intestazione");
        if (headerFooter?.Footer is not null)
            applied.Add("piè di pagina");

        return new DecorationResult(decorated, applied);
    }

    /// <summary>Un font di sistema incorporato: così il file resta convertibile
    /// in PDF/A, dove i font impliciti non esistono.</summary>
    private static PdfFont LoadFont()
    {
        var candidates = new[] { "arial.ttf", "segoeui.ttf", "times.ttf" }
            .Select(name => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", name));
        var path = candidates.FirstOrDefault(File.Exists);
        return path is null
            ? PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA)
            : PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H,
                PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
    }

    private static void DrawWatermark(PdfCanvas canvas, PdfFont font, Watermark watermark,
        iText.Kernel.Geom.Rectangle box)
    {
        canvas.SaveState();
        canvas.SetExtGState(new PdfExtGState()
            .SetFillOpacity(Math.Clamp(watermark.Opacity, 0.02f, 1f)));
        canvas.SetFillColor(new DeviceGray(0.35f));

        var radians = watermark.AngleDegrees * Math.PI / 180;
        var width = font.GetWidth(watermark.Text, watermark.FontSize);
        var centerX = box.GetWidth() / 2;
        var centerY = box.GetHeight() / 2;
        var offsetX = (float)(centerX - width / 2 * Math.Cos(radians));
        var offsetY = (float)(centerY - width / 2 * Math.Sin(radians));

        canvas.BeginText()
            .SetFontAndSize(font, watermark.FontSize)
            .SetTextMatrix((float)Math.Cos(radians), (float)Math.Sin(radians),
                (float)-Math.Sin(radians), (float)Math.Cos(radians), offsetX, offsetY)
            .ShowText(watermark.Text)
            .EndText();
        canvas.RestoreState();
    }

    private static bool DrawHeaderFooter(PdfCanvas canvas, PdfFont font, HeaderFooter headerFooter,
        iText.Kernel.Geom.Rectangle box)
    {
        var touched = false;
        if (headerFooter.Header is { } header)
        {
            DrawText(canvas, font, header, headerFooter.FontSize,
                Margin, box.GetHeight() - Margin);
            touched = true;
        }
        if (headerFooter.Footer is { } footer)
        {
            DrawText(canvas, font, footer, headerFooter.FontSize, Margin, Margin - 10);
            touched = true;
        }
        return touched;
    }

    private static void DrawPageNumber(PdfCanvas canvas, PdfFont font, PageNumbering numbering,
        iText.Kernel.Geom.Rectangle box, int pageNumber, int totalPages)
    {
        var shown = pageNumber + numbering.StartAt - 1;
        var text = numbering.Format
            .Replace("{n}", shown.ToString())
            .Replace("{tot}", (totalPages + numbering.StartAt - 1).ToString());

        var width = font.GetWidth(text, numbering.FontSize);
        var (x, y) = numbering.Position switch
        {
            PageNumberPosition.BassoSinistra => (Margin, Margin),
            PageNumberPosition.BassoDestra => (box.GetWidth() - Margin - width, Margin),
            PageNumberPosition.AltoCentro => ((box.GetWidth() - width) / 2, box.GetHeight() - Margin),
            PageNumberPosition.AltoDestra => (box.GetWidth() - Margin - width, box.GetHeight() - Margin),
            _ => ((box.GetWidth() - width) / 2, Margin),
        };

        DrawText(canvas, font, text, numbering.FontSize, x, y);
    }

    private static void DrawText(PdfCanvas canvas, PdfFont font, string text, float size,
        float x, float y)
    {
        canvas.SaveState();
        canvas.SetFillColor(new DeviceGray(0.15f));
        canvas.BeginText()
            .SetFontAndSize(font, size)
            .SetTextMatrix(x, y)
            .ShowText(Sanitize(font, text))
            .EndText();
        canvas.RestoreState();
    }

    /// <summary>I caratteri che il font non contiene diventano '?': meglio un
    /// punto interrogativo visibile che un'eccezione a metà lavoro.</summary>
    private static string Sanitize(PdfFont font, string text) =>
        new([.. text.Select(c => c == ' ' || font.ContainsGlyph(c) ? c : '?')]);
}
