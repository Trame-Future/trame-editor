using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Le righe di testo di una pagina, con la posizione e se si possono modificare.
/// È il comando da cui si parte: l'indice che restituisce è quello che
/// <c>sostituisci</c> si aspetta.
/// </summary>
public static class LinesCommand
{
    public static object Run(CommandLine line)
    {
        var path = Paths.ExistingFile(line.At(0, "file.pdf"));
        var onlyEditable = line.Has("solo-modificabili");

        using var inspector = new PdfTextInspector(path);
        var pages = line.Has("tutte")
            ? Enumerable.Range(1, inspector.PageCount).ToList()
            : [Page(line, inspector.PageCount)];

        var rows = new List<object>();
        foreach (var page in pages)
        {
            var index = 0;
            foreach (var text in inspector.GetLines(page))
            {
                var current = index++;
                if (onlyEditable && !text.IsEditable)
                    continue;
                rows.Add(Describe(current, text));
            }
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "righe",
            ["file"] = path,
            ["pagine"] = inspector.PageCount,
            ["righe"] = rows,
        };
    }

    /// <summary>L'indice resta quello dell'elenco completo della pagina anche con
    /// --solo-modificabili: se cambiasse, il numero da passare a "sostituisci"
    /// dipenderebbe da come si è chiesto l'elenco.</summary>
    internal static object Describe(int index, PdfTextLine line) => new Dictionary<string, object?>
    {
        ["indice"] = index,
        ["pagina"] = line.PageNumber,
        ["testo"] = line.Text,
        ["modificabile"] = line.IsEditable,
        ["motivo"] = line.NotEditableReason,
        ["sinistra"] = Math.Round(line.Left, 2),
        ["base"] = Math.Round(line.BaselineY, 2),
        ["larghezza"] = Math.Round(line.Width, 2),
        ["altezza"] = Math.Round(line.Height, 2),
        ["font"] = line.FontName,
        ["corpoPt"] = Math.Round(line.FontSizePt, 2),
    };

    internal static int Page(CommandLine line, int pageCount)
    {
        var page = line.IntOr("pagina", 1);
        if (page < 1 || page > pageCount)
            throw new UsageException($"La pagina {page} non esiste: il documento ne ha {pageCount}.");
        return page;
    }
}
