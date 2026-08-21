using TrameEditor.Core.Pdf;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Anonimizzazione: i dati personali vengono tolti dal contenuto, non coperti con un
/// rettangolo. Quello che <b>non</b> si è riusciti a togliere finisce nell'esito, riga per
/// riga: chi legge è un programma, e un avviso non riportato qui per lui non esiste.
/// </summary>
public static class RedactCommand
{
    private static readonly Dictionary<string, SensitiveKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cf"] = SensitiveKind.CodiceFiscale,
        ["codicefiscale"] = SensitiveKind.CodiceFiscale,
        ["iban"] = SensitiveKind.Iban,
        ["email"] = SensitiveKind.Email,
        ["telefono"] = SensitiveKind.Telefono,
        ["targa"] = SensitiveKind.Targa,
    };

    public static object Run(CommandLine line)
    {
        var source = Paths.ExistingFile(line.At(0, "origine.pdf"));
        var target = Paths.Target(line.At(1, "destinazione.pdf"), line.Has("sovrascrivi"));
        var wanted = Selected(line.Value("tipi"));

        var found = PdfRedactionService.Scan(source);
        var selected = found.Where(match => wanted.Contains(match.Kind)).ToList();
        var result = PdfRedactionService.Apply(source, target, selected, line.Has("metadati"));

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "anonimizza",
            ["origine"] = source,
            ["destinazione"] = target,
            ["tipiCercati"] = wanted.Select(k => k.ToString()).ToList(),
            ["trovati"] = found.GroupBy(m => m.Kind)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ["rimossi"] = result.ItemsRedacted,
            ["righeCambiate"] = result.LinesChanged,
            ["metadatiRipuliti"] = result.MetadataStripped,
            // Righe che contenevano un dato ma che non si è potuto riscrivere: il file
            // prodotto le contiene ancora, e tacerlo sarebbe il peggio che può fare
            // uno strumento di anonimizzazione.
            ["righeSaltate"] = result.SkippedLines
                .Select(l => new Dictionary<string, object?>
                {
                    ["pagina"] = l.PageNumber,
                    ["testo"] = l.Text,
                    ["motivo"] = l.NotEditableReason ?? "il testo non è stato trovato nel contenuto della pagina",
                })
                .ToList(),
            ["completo"] = result.SkippedLines.Count == 0,
        };
    }

    private static HashSet<SensitiveKind> Selected(string? tipi)
    {
        if (string.IsNullOrWhiteSpace(tipi))
            return [.. Enum.GetValues<SensitiveKind>()];

        var chosen = new HashSet<SensitiveKind>();
        foreach (var raw in tipi.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Kinds.TryGetValue(raw, out var kind))
                throw new UsageException(
                    $"Tipo di dato sconosciuto: «{raw}». Ammessi: {string.Join(", ", Kinds.Keys.Distinct())}.");
            chosen.Add(kind);
        }
        return chosen;
    }
}
