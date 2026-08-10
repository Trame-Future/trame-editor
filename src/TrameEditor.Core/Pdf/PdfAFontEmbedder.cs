using iText.IO.Font;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Incorpora nel PDF il programma di un font che il documento si limitava a
/// nominare (es. Helvetica, Arial), prendendolo dai font installati sul computer.
/// <para>
/// Il dizionario del font resta quello originale — codifica, primo/ultimo codice e
/// tabella delle larghezze non vengono toccati: aggiungiamo solo il programma
/// mancante. Prima però <b>verifichiamo</b> che le larghezze dichiarate nel PDF
/// coincidano con quelle del font che stiamo incorporando: se non coincidono la
/// resa cambierebbe, e allora rinunciamo invece di produrre un archivio diverso
/// dall'originale.
/// </para>
/// </summary>
internal static class PdfAFontEmbedder
{
    /// <summary>Scarto massimo ammesso fra larghezza dichiarata e larghezza reale
    /// del glifo (millesimi di em): tollera i soli arrotondamenti.</summary>
    private const int WidthTolerance = 2;

    /// <summary>Il font di sistema è già stato letto una volta? Un TTF pesa
    /// qualche megabyte e in un documento lo stesso font torna su ogni pagina.</summary>
    private static readonly Dictionary<string, FontProgram> ProgramCache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// Verifica, senza modificare nulla, che questo font si possa incorporare con
    /// il font di sistema indicato mantenendo la stessa resa. È la stessa risposta
    /// che l'analisi mostra all'utente <b>prima</b> della conversione.
    /// </summary>
    internal static bool CanEmbed(PdfDictionary font, string systemFontPath,
        IReadOnlyCollection<int> charactersUsed, out string reason)
    {
        // I collection TrueType (.ttc) contengono più font: senza sapere quale
        // indice serve non rischiamo.
        if (Path.GetExtension(systemFontPath).Equals(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            reason = "il font di sistema è una raccolta (.ttc): non incorporabile senza ambiguità";
            return false;
        }

        if (LoadProgram(systemFontPath) is not { } program)
        {
            reason = "font di sistema non leggibile";
            return false;
        }

        return WidthsMatch(font, program, charactersUsed, out reason);
    }

    private static FontProgram? LoadProgram(string path)
    {
        lock (CacheLock)
        {
            if (ProgramCache.TryGetValue(path, out var cached))
                return cached;
            try
            {
                var program = FontProgramFactory.CreateFont(path);
                ProgramCache[path] = program;
                return program;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    internal static bool TryEmbed(PdfDocument document, PdfDictionary font,
        string systemFontPath, IReadOnlyCollection<int> charactersUsed, out string reason)
    {
        if (!CanEmbed(font, systemFontPath, charactersUsed, out reason))
            return false;

        var program = LoadProgram(systemFontPath)!;
        byte[] programBytes;
        bool isSubset;
        try
        {
            (programBytes, isSubset) = BuildProgramBytes(program, systemFontPath, charactersUsed);
        }
        catch (Exception ex)
        {
            reason = $"font di sistema non leggibile ({ex.GetType().Name})";
            return false;
        }

        EnsureWidths(font, program);
        var descriptor = BuildDescriptor(document, font, program, programBytes);

        var postScriptName = program.GetFontNames().GetFontName();
        if (isSubset)
            postScriptName = $"{SubsetPrefix(charactersUsed)}+{postScriptName}";
        font.Put(PdfName.Subtype, PdfName.TrueType);
        font.Put(PdfName.BaseFont, new PdfName(postScriptName));
        descriptor.Put(PdfName.FontName, new PdfName(postScriptName));
        font.Put(PdfName.FontDescriptor, descriptor);
        font.SetModified();

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Sui caratteri effettivamente scritti nel documento, le larghezze del font
    /// che incorporiamo devono coincidere con quelle che il lettore usava prima —
    /// dichiarate nel PDF o, per i 14 font standard, quelle del font standard
    /// stesso. Altrimenti il testo si sposterebbe.
    /// </summary>
    private static bool WidthsMatch(PdfDictionary font, FontProgram program,
        IReadOnlyCollection<int> charactersUsed, out string mismatch)
    {
        var widths = font.GetAsArray(PdfName.Widths);
        var firstChar = font.GetAsNumber(PdfName.FirstChar)?.IntValue();
        if (widths is null || firstChar is null)
        {
            // Nessuna larghezza dichiarata: è il caso dei 14 font standard, dove il
            // lettore usa le metriche incorporate nel proprio visualizzatore. Non
            // diamo per scontato che il font di sistema le rispetti: le confrontiamo.
            return StandardMetricsAgree(font, program, charactersUsed, out mismatch);
        }

        foreach (var unicode in charactersUsed)
        {
            var code = UnicodeToWinAnsi(unicode);
            var index = code is null ? -1 : code.Value - firstChar.Value;
            if (index < 0 || index >= widths.Size())
                continue;

            var declared = widths.GetAsNumber(index)?.IntValue();
            if (declared is null or 0)
                continue;

            var glyph = program.GetGlyph(unicode);
            if (glyph is null)
            {
                mismatch = $"il font installato non ha il carattere «{char.ConvertFromUtf32(unicode)}», " +
                    "usato nel documento";
                return false;
            }

            if (Math.Abs(glyph.GetWidth() - declared.Value) > WidthTolerance)
            {
                mismatch = $"la larghezza del carattere «{char.ConvertFromUtf32(unicode)}» dichiarata " +
                    "nel PDF non coincide con quella del font installato: il testo si sposterebbe";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }

    /// <summary>
    /// Quando il PDF non dichiara le larghezze si affida alle metriche di uno dei
    /// 14 font standard. Confrontiamo quelle metriche con il font che stiamo per
    /// incorporare: se coincidono (è il caso di Helvetica↔Arial, Times↔Times New
    /// Roman, Courier↔Courier New) la resa non cambia, altrimenti rinunciamo.
    /// </summary>
    private static bool StandardMetricsAgree(PdfDictionary font, FontProgram substitute,
        IReadOnlyCollection<int> charactersUsed, out string mismatch)
    {
        var baseFont = font.GetAsName(PdfName.BaseFont)?.GetValue();
        FontProgram reference;
        try
        {
            reference = FontProgramFactory.CreateFont(baseFont);
        }
        catch (Exception)
        {
            mismatch = $"il PDF non dichiara le larghezze del font «{baseFont}» e non esiste " +
                "una metrica di riferimento con cui confrontarlo";
            return false;
        }

        foreach (var unicode in charactersUsed)
        {
            var expected = reference.GetGlyph(unicode);
            if (expected is null || expected.GetWidth() == 0)
                continue;

            var actual = substitute.GetGlyph(unicode);
            if (actual is null || Math.Abs(actual.GetWidth() - expected.GetWidth()) > WidthTolerance)
            {
                mismatch = $"il carattere «{char.ConvertFromUtf32(unicode)}» ha larghezze diverse " +
                    $"in «{baseFont}» e nel font installato: il testo si sposterebbe";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }

    /// <summary>Un font incorporato deve dichiarare le proprie larghezze: se il
    /// PDF non le aveva (font standard) le scriviamo noi dal programma.</summary>
    private static void EnsureWidths(PdfDictionary font, FontProgram program)
    {
        if (font.ContainsKey(PdfName.Widths) && font.ContainsKey(PdfName.FirstChar))
            return;

        const int first = 32, last = 255;
        var widths = new PdfArray();
        for (var code = first; code <= last; code++)
        {
            var unicode = WinAnsiToUnicode(code);
            var glyph = unicode is null ? null : program.GetGlyph(unicode.Value);
            widths.Add(new PdfNumber(glyph?.GetWidth() ?? 0));
        }

        font.Put(PdfName.FirstChar, new PdfNumber(first));
        font.Put(PdfName.LastChar, new PdfNumber(last));
        font.Put(PdfName.Widths, widths);
    }

    private static PdfDictionary BuildDescriptor(PdfDocument document, PdfDictionary font,
        FontProgram program, byte[] programBytes)
    {
        var descriptor = font.GetAsDictionary(PdfName.FontDescriptor) ?? new PdfDictionary();
        var metrics = program.GetFontMetrics();
        var bbox = metrics.GetBbox();

        descriptor.Put(PdfName.Type, PdfName.FontDescriptor);
        descriptor.Put(PdfName.Flags, new PdfNumber(ComputeFlags(program)));
        descriptor.Put(PdfName.FontBBox, new PdfArray(new[] { bbox[0], bbox[1], bbox[2], bbox[3] }));
        descriptor.Put(PdfName.ItalicAngle, new PdfNumber(metrics.GetItalicAngle()));
        descriptor.Put(PdfName.Ascent, new PdfNumber(metrics.GetAscender()));
        descriptor.Put(PdfName.Descent, new PdfNumber(metrics.GetDescender()));
        descriptor.Put(PdfName.CapHeight, new PdfNumber(
            metrics.GetCapHeight() != 0 ? metrics.GetCapHeight() : metrics.GetAscender()));
        descriptor.Put(PdfName.StemV, new PdfNumber(metrics.GetStemV() != 0 ? metrics.GetStemV() : 80));
        descriptor.Put(PdfName.MissingWidth, new PdfNumber(0));

        var fontFile = new PdfStream(programBytes);
        fontFile.Put(PdfName.Length1, new PdfNumber(programBytes.Length));
        fontFile.MakeIndirect(document);
        descriptor.Put(PdfName.FontFile2, fontFile);
        // Un solo programma per descrittore: se ne esistevano altri vanno tolti.
        descriptor.Remove(PdfName.FontFile);
        descriptor.Remove(PdfName.FontFile3);

        descriptor.MakeIndirect(document);
        return descriptor;
    }

    private static int ComputeFlags(FontProgram program)
    {
        const int fixedPitch = 1, serif = 2, nonSymbolic = 32, italic = 64;
        var metrics = program.GetFontMetrics();
        // Nonsymbolic è obbligatorio per i font con codifica WinAnsi.
        var flags = nonSymbolic;
        if (metrics.IsFixedPitch())
            flags |= fixedPitch;
        if (metrics.GetItalicAngle() != 0)
            flags |= italic;

        var name = program.GetFontNames().GetFontName().ToLowerInvariant();
        if (name.Contains("times") || name.Contains("serif") || name.Contains("georgia") ||
            name.Contains("cambria") || name.Contains("garamond"))
            flags |= serif;
        return flags;
    }

    /// <summary>
    /// Incorporare un font intero significa aggiungere qualche megabyte al file:
    /// visto che sappiamo esattamente quali caratteri il documento usa, ne
    /// incorporiamo il solo sottoinsieme. Il sottoinsieme viene <b>riletto e
    /// verificato</b> (stessi glifi, stesse larghezze) e, se qualcosa non torna,
    /// si ripiega sul font intero: un archivio pesante è meglio di uno rotto.
    /// </summary>
    private static (byte[] Bytes, bool IsSubset) BuildProgramBytes(FontProgram program,
        string systemFontPath, IReadOnlyCollection<int> charactersUsed)
    {
        var full = File.ReadAllBytes(systemFontPath);
        if (program is not TrueTypeFont trueType)
            return (full, false);

        try
        {
            var glyphs = new HashSet<int> { 0 }; // .notdef è obbligatorio
            var expectedAdvances = new Dictionary<int, int>();
            foreach (var unicode in charactersUsed)
            {
                if (program.GetGlyph(unicode) is not { } glyph)
                    continue;
                glyphs.Add(glyph.GetCode());
                expectedAdvances[glyph.GetCode()] = glyph.GetWidth();
            }

            var subset = trueType.GetSubset(glyphs, true);
            if (subset is null || subset.Length == 0 || subset.Length >= full.Length)
                return (full, false);
            if (!TrueTypeSubsetCheck.KeepsMetrics(subset, expectedAdvances))
                return (full, false);
            return (subset, true);
        }
        catch (Exception)
        {
            return (full, false);
        }
    }

    /// <summary>Le sei lettere che per convenzione precedono il nome di un font
    /// ridotto a sottoinsieme ("ABCDEF+Arial"). Deterministiche: stessi caratteri,
    /// stesso prefisso.</summary>
    private static string SubsetPrefix(IReadOnlyCollection<int> charactersUsed)
    {
        var hash = 17;
        foreach (var unicode in charactersUsed.Order())
            hash = unchecked(hash * 31 + unicode);
        hash &= 0x7FFFFFFF;

        var letters = new char[6];
        for (var i = 0; i < letters.Length; i++)
        {
            letters[i] = (char)('A' + hash % 26);
            hash /= 26;
        }
        return new string(letters);
    }

    /// <summary>Tabella inversa WinAnsi, costruita una volta sola.</summary>
    private static readonly Lazy<Dictionary<int, int>> WinAnsiReverse = new(() =>
    {
        var map = new Dictionary<int, int>();
        for (var code = 0; code <= 255; code++)
        {
            if (WinAnsiToUnicode(code) is { } unicode)
                map.TryAdd(unicode, code);
        }
        return map;
    });

    private static int? UnicodeToWinAnsi(int unicode) =>
        WinAnsiReverse.Value.TryGetValue(unicode, out var code) ? code : null;

    private static int? WinAnsiToUnicode(int code)
    {
        if (code is < 0 or > 255)
            return null;
        var text = PdfEncodings.ConvertToString([(byte)code], PdfEncodings.WINANSI);
        if (text.Length == 0)
            return null;
        var unicode = char.ConvertToUtf32(text, 0);
        return unicode is 0 or 0xFFFD ? null : unicode;
    }
}
