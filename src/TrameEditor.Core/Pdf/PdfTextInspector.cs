using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Estrae le righe di testo di un PDF con posizioni, font e colore (PdfPig).
/// Il file è letto in memoria: nessun lock sul file originale.
/// </summary>
public sealed class PdfTextInspector : IDisposable
{
    private const double BaselineTolerance = 2.0;

    private readonly PdfDocument _document;

    public PdfTextInspector(string path)
    {
        _document = PdfDocument.Open(File.ReadAllBytes(path));
    }

    public int PageCount => _document.NumberOfPages;

    public (double Width, double Height) GetPageSize(int pageNumber)
    {
        var page = _document.GetPage(pageNumber);
        return (page.Width, page.Height);
    }

    /// <summary>Righe della pagina (1-based), dall'alto verso il basso.</summary>
    public IReadOnlyList<PdfTextLine> GetLines(int pageNumber)
    {
        var page = _document.GetPage(pageNumber);
        var pageRotated = page.Rotation.Value != 0;

        var words = page.GetWords()
            .Where(w => w.Letters.Count > 0 && !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => BaselineOf(w))
            .ToList();

        var lines = new List<PdfTextLine>();
        var current = new List<Word>();
        foreach (var word in words)
        {
            if (current.Count > 0 &&
                Math.Abs(BaselineOf(current[0]) - BaselineOf(word)) > BaselineTolerance)
            {
                AddSegments(lines, current, pageNumber, pageRotated);
                current.Clear();
            }
            current.Add(word);
        }
        if (current.Count > 0)
            AddSegments(lines, current, pageNumber, pageRotated);

        return lines;
    }

    /// <summary>Spezza una riga di baseline in segmenti quando il distacco orizzontale
    /// è ampio (colonne diverse, numeri di pagina, intestazioni tabellari).</summary>
    private static void AddSegments(List<PdfTextLine> lines, List<Word> lineWords,
        int pageNumber, bool pageRotated)
    {
        var ordered = lineWords.OrderBy(w => w.BoundingBox.Left).ToList();
        var segment = new List<Word>();
        foreach (var word in ordered)
        {
            if (segment.Count > 0)
            {
                var gapLimit = Math.Max(6.0, word.Letters[0].PointSize * 2.0);
                if (word.BoundingBox.Left - segment[^1].BoundingBox.Right > gapLimit)
                {
                    lines.Add(BuildLine(segment, pageNumber, pageRotated));
                    segment.Clear();
                }
            }
            segment.Add(word);
        }
        if (segment.Count > 0)
            lines.Add(BuildLine(segment, pageNumber, pageRotated));
    }

    private static PdfTextLine BuildLine(List<Word> words, int pageNumber, bool pageRotated)
    {
        var text = string.Join(" ", words.Select(w => w.Text));
        var left = words.Min(w => w.BoundingBox.Left);
        var bottom = words.Min(w => w.BoundingBox.Bottom);
        var right = words.Max(w => w.BoundingBox.Right);
        var top = words.Max(w => w.BoundingBox.Top);
        var first = words[0].Letters[0];

        var horizontal = words.SelectMany(w => w.Letters)
            .All(l => l.TextOrientation == TextOrientation.Horizontal);
        string? reason = pageRotated
            ? "La pagina ha una rotazione interna: modifica del testo non supportata."
            : !horizontal
                ? "Testo ruotato o verticale: modifica non supportata."
                : null;

        var (r, g, b) = first.Color.ToRGBValues();
        return new PdfTextLine(
            pageNumber,
            text,
            left,
            bottom,
            right - left,
            top - bottom,
            first.StartBaseLine.X,
            first.StartBaseLine.Y,
            first.FontName ?? string.Empty,
            first.PointSize,
            (double)r, (double)g, (double)b,
            IsEditable: reason is null,
            NotEditableReason: reason);
    }

    private static double BaselineOf(Word word) => word.Letters[0].StartBaseLine.Y;

    public void Dispose() => _document.Dispose();
}
