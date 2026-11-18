using System.Globalization;
using TrameEditor.Core.Documents;
using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Cancella quello che sta dentro uno o più rettangoli (PDF), oppure i dati personali di un
/// file di testo. È il complemento di «anonimizza», che riconosce gli schemi ma non sa
/// niente di una firma o di una fotografia.
/// </summary>
public static class AreaRedactCommand
{
    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "file"));
        var destination = Paths.Target(line.At(1, "destinazione"), line.Has("sovrascrivi"));

        return IsText(source)
            ? RedactText(source, destination, line)
            : RedactAreas(source, destination, line);
    }

    private static bool IsText(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".markdown" or ".csv";

    private static object RedactText(string source, string destination, CommandLine line)
    {
        var kinds = KindsOf(line.Value("tipi"));
        var result = TextRedactor.RedactFile(source, destination, kinds);

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "oscura",
            ["tipo"] = "testo",
            ["origine"] = source,
            ["destinazione"] = destination,
            ["righeCambiate"] = result.LinesChanged,
            ["datiTolti"] = result.ItemsRedacted,
            ["trovati"] = result.Found
                .GroupBy(f => f.Kind.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    private static object RedactAreas(string source, string destination, CommandLine line)
    {
        var areas = AreasOf(line);
        var result = PdfAreaRedactor.Apply(source, destination, areas, !line.Has("senza-rettangolo"));

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "oscura",
            ["tipo"] = "pdf",
            ["origine"] = source,
            ["destinazione"] = destination,
            ["areeApplicate"] = result.AreasApplied,
            ["righeTolte"] = result.TextLinesRemoved,
            ["immaginiTolte"] = result.ImagesRemoved,
            // Quello che non si è potuto togliere resta coperto ma presente: tacerlo
            // vorrebbe dire consegnare un documento che sembra oscurato e non lo è.
            ["completo"] = result.Complete,
            ["righeRimaste"] = result.SkippedLines.Select(l => l.Text).ToList(),
            ["avvisi"] = result.Warnings,
        };
    }

    /// <summary>Le aree: «pagina:sinistra,base,larghezza,altezza», anche più d'una.</summary>
    private static List<RedactionArea> AreasOf(CommandLine line)
    {
        if (!line.Has("area"))
            throw new UsageException(
                "Indica almeno un'area da cancellare: --area \"1:50,530,300,25\" " +
                "(pagina:sinistra,base,larghezza,altezza, in punti PDF con l'origine in basso " +
                "a sinistra). Per più aree, separale con un punto e virgola.");

        var areas = new List<RedactionArea>();
        foreach (var piece in line.Required("area").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = piece.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var page))
                throw new UsageException(
                    $"Area scritta male: «{piece}». Si scrive «pagina:sinistra,base,larghezza,altezza».");

            var numbers = parts[1].Split(',', StringSplitOptions.TrimEntries);
            if (numbers.Length != 4 ||
                !numbers.All(n => double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                throw new UsageException(
                    $"Area scritta male: «{piece}». Dopo i due punti servono quattro numeri " +
                    "separati da virgola, col punto come separatore decimale.");

            var values = numbers.Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
            areas.Add(new RedactionArea(page, values[0], values[1], values[2], values[3]));
        }
        return areas;
    }

    private static IReadOnlyCollection<SensitiveKind>? KindsOf(string? tipi)
    {
        if (string.IsNullOrWhiteSpace(tipi))
            return null;

        var map = new Dictionary<string, SensitiveKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["cf"] = SensitiveKind.CodiceFiscale,
            ["codicefiscale"] = SensitiveKind.CodiceFiscale,
            ["iban"] = SensitiveKind.Iban,
            ["email"] = SensitiveKind.Email,
            ["telefono"] = SensitiveKind.Telefono,
            ["targa"] = SensitiveKind.Targa,
        };

        var chosen = new List<SensitiveKind>();
        foreach (var raw in tipi.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!map.TryGetValue(raw, out var kind))
                throw new UsageException(
                    $"Tipo di dato sconosciuto: «{raw}». Ammessi: {string.Join(", ", map.Keys.Distinct())}.");
            chosen.Add(kind);
        }
        return chosen;
    }
}
