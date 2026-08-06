using System.Text.RegularExpressions;

namespace TrameEditor.Core.Pdf;

public enum SensitiveKind
{
    CodiceFiscale,
    Iban,
    Email,
    Telefono,
    Targa,
}

/// <summary>Un dato sensibile trovato in una riga: intervallo in caratteri
/// del testo originale della riga.</summary>
public sealed record SensitiveMatch(
    PdfTextLine Line,
    SensitiveKind Kind,
    string Value,
    int Start,
    int Length);

/// <summary>
/// Riconoscimento di dati personali nel testo (pattern deterministici, nessuna
/// rete): codice fiscale, IBAN italiano, email, telefoni, targhe auto.
/// I pattern "compatti" (CF, IBAN…) sono cercati anche attraverso gli spazi
/// ("IT 60 X054…"), rimappando l'esito sull'intervallo originale.
/// </summary>
public static class SensitiveDataScanner
{
    private static readonly Regex EmailRegex = new(
        @"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);

    // Pattern su testo compattato (senza spazi), in ordine di priorità:
    // in caso di sovrapposizione vince il primo trovato.
    private static readonly (SensitiveKind Kind, Regex Regex)[] CompactPatterns =
    [
        (SensitiveKind.CodiceFiscale,
            new Regex(@"[A-Z]{6}\d{2}[ABCDEHLMPRST]\d{2}[A-Z]\d{3}[A-Z]", RegexOptions.Compiled)),
        (SensitiveKind.Iban,
            new Regex(@"IT\d{2}[A-Z][A-Z0-9]{22}", RegexOptions.Compiled)),
        (SensitiveKind.Telefono,
            new Regex(@"(\+39)?3\d{8,9}", RegexOptions.Compiled)),
        (SensitiveKind.Telefono,
            new Regex(@"0\d{8,10}", RegexOptions.Compiled)),
        (SensitiveKind.Targa,
            new Regex(@"[A-Z]{2}\d{3}[A-Z]{2}", RegexOptions.Compiled)),
    ];

    public static IReadOnlyList<SensitiveMatch> ScanLine(PdfTextLine line)
    {
        var text = line.Text;
        var matches = new List<SensitiveMatch>();

        foreach (Match email in EmailRegex.Matches(text))
            TryAdd(matches, line, SensitiveKind.Email, text, email.Index, email.Length);

        // Testo compattato: solo gli spazi vengono rimossi, con mappa degli indici.
        var compactChars = new List<char>(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
                continue;
            compactChars.Add(char.ToUpperInvariant(text[i]));
            map.Add(i);
        }
        var compact = new string(compactChars.ToArray());

        foreach (var (kind, regex) in CompactPatterns)
        {
            foreach (Match match in regex.Matches(compact))
            {
                var originalStart = map[match.Index];
                var originalEnd = map[match.Index + match.Length - 1] + 1;

                // Confini valutati sul testo ORIGINALE: nel compattato la parola
                // successiva si incolla al dato ("…H501U" + "del" ⇒ falso confine).
                if (originalStart > 0 && char.IsLetterOrDigit(text[originalStart - 1]))
                    continue;
                if (originalEnd < text.Length && char.IsLetterOrDigit(text[originalEnd]))
                    continue;

                TryAdd(matches, line, kind, text, originalStart, originalEnd - originalStart);
            }
        }

        return matches.OrderBy(m => m.Start).ToList();
    }

    private static void TryAdd(List<SensitiveMatch> matches, PdfTextLine line,
        SensitiveKind kind, string text, int start, int length)
    {
        var end = start + length;
        if (matches.Any(m => start < m.Start + m.Length && m.Start < end))
            return; // sovrapposto a un dato già riconosciuto (priorità al primo)
        matches.Add(new SensitiveMatch(line, kind, text.Substring(start, length), start, length));
    }

    /// <summary>Maschera gli intervalli indicati: lettere e cifre → 'X',
    /// spazi e punteggiatura conservati (il layout resta leggibile).</summary>
    public static string MaskLine(string lineText, IEnumerable<SensitiveMatch> matchesInLine)
    {
        var chars = lineText.ToCharArray();
        foreach (var match in matchesInLine)
        {
            for (var i = match.Start; i < match.Start + match.Length && i < chars.Length; i++)
            {
                if (char.IsLetterOrDigit(chars[i]))
                    chars[i] = 'X';
            }
        }
        return new string(chars);
    }

    public static string DisplayName(this SensitiveKind kind) => kind switch
    {
        SensitiveKind.CodiceFiscale => "Codice fiscale",
        SensitiveKind.Iban => "IBAN",
        SensitiveKind.Email => "Email",
        SensitiveKind.Telefono => "Telefono",
        SensitiveKind.Targa => "Targa",
        _ => kind.ToString(),
    };
}
