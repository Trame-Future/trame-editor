using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Documents;

/// <summary>
/// Con che cosa si indica un punto del documento: in un PDF è la pagina, in un
/// file di testo o Markdown è la riga. Serve perché le funzioni che citano una
/// posizione (l'assistente, il confronto) devono dire la verità su entrambi.
/// </summary>
public enum DocumentUnit
{
    Pagina,
    Riga,
}

public static class DocumentUnitExtensions
{
    /// <summary>Etichetta breve per le citazioni: "pag." oppure "riga".</summary>
    public static string ShortLabel(this DocumentUnit unit) =>
        unit == DocumentUnit.Pagina ? "pag." : "riga";

    public static string Label(this DocumentUnit unit) =>
        unit == DocumentUnit.Pagina ? "pagina" : "riga";
}

/// <summary>Un pezzo di documento con il punto da cui viene (pagina o riga).</summary>
public sealed record DocumentSection(int Reference, string Text);

public sealed record DocumentContent(DocumentUnit Unit, IReadOnlyList<DocumentSection> Sections)
{
    public int TotalChars => Sections.Sum(section => section.Text.Length);
}

/// <summary>
/// Legge il testo di un documento in sezioni numerate, qualunque sia il formato.
/// È il punto in cui PDF, TXT e Markdown smettono di essere cose diverse: da qui
/// in poi confronto e assistente lavorano sullo stesso materiale.
/// </summary>
public static class DocumentTextReader
{
    /// <summary>Righe per sezione nei file di testo: abbastanza da dare contesto
    /// a chi legge la citazione, abbastanza poche da essere precisi.</summary>
    private const int LinesPerSection = 20;

    public static bool IsSupported(string path) => Extension(path) is ".pdf" or ".txt" or ".md"
        or ".markdown" or ".log" or ".csv" or ".json" or ".xml";

    public static bool IsPdf(string path) => Extension(path) == ".pdf";

    private static string Extension(string path) =>
        Path.GetExtension(path).ToLowerInvariant();

    public static DocumentContent Read(string path) =>
        IsPdf(path) ? ReadPdf(path) : ReadText(File.ReadAllText(path));

    private static DocumentContent ReadPdf(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var sections = new List<DocumentSection>();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            var text = string.Join("\n", inspector.GetLines(page).Select(line => line.Text));
            sections.Add(new DocumentSection(page, text));
        }
        return new DocumentContent(DocumentUnit.Pagina, sections);
    }

    /// <summary>Testo già in memoria (l'editor aperto): sezioni di righe
    /// consecutive, numerate dalla prima riga di ciascuna.</summary>
    public static DocumentContent ReadText(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sections = new List<DocumentSection>();
        for (var start = 0; start < lines.Length; start += LinesPerSection)
        {
            var block = string.Join("\n", lines.Skip(start).Take(LinesPerSection)).Trim();
            if (block.Length > 0)
                sections.Add(new DocumentSection(start + 1, block));
        }
        return new DocumentContent(DocumentUnit.Riga, sections);
    }

    /// <summary>Righe non vuote con il loro riferimento, per il confronto.</summary>
    public static (DocumentUnit Unit, List<(string Text, int Reference)> Lines) ReadLines(string path)
    {
        if (IsPdf(path))
        {
            using var inspector = new PdfTextInspector(path);
            var pdfLines = new List<(string, int)>();
            for (var page = 1; page <= inspector.PageCount; page++)
            {
                foreach (var line in inspector.GetLines(page))
                {
                    var text = line.Text.Trim();
                    if (text.Length > 0)
                        pdfLines.Add((text, page));
                }
            }
            return (DocumentUnit.Pagina, pdfLines);
        }

        var lines = new List<(string, int)>();
        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var text = raw.Trim();
            if (text.Length > 0)
                lines.Add((text, number));
        }
        return (DocumentUnit.Riga, lines);
    }
}
