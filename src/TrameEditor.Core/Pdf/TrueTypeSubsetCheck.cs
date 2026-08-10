using System.Buffers.Binary;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Controlla che un font ridotto a sottoinsieme sia ancora utilizzabile: le
/// tabelle indispensabili ci sono e le larghezze dei glifi che ci servono non
/// sono cambiate.
/// <para>
/// Serve un controllo diretto sui byte perché un sottoinsieme, pur valido dentro
/// un PDF, non è un font autonomo completo (manca per esempio la tabella dei
/// nomi) e i lettori di font generici lo rifiutano. Quello che conta per la resa
/// però è verificabile: la tabella <c>hmtx</c> con le larghezze e la presenza dei
/// glifi.
/// </para>
/// </summary>
internal static class TrueTypeSubsetCheck
{
    private static readonly string[] RequiredTables =
        ["head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap"];

    /// <param name="advancesByGlyph">Larghezze attese (millesimi di em) per i
    /// glifi effettivamente usati, indicizzate per numero di glifo.</param>
    internal static bool KeepsMetrics(byte[] font, IReadOnlyDictionary<int, int> advancesByGlyph)
    {
        try
        {
            var tables = ReadTableDirectory(font);
            if (tables is null || RequiredTables.Any(name => !tables.ContainsKey(name)))
                return false;

            var unitsPerEm = ReadUInt16(font, tables["head"] + 18);
            if (unitsPerEm == 0)
                return false;

            var metricsCount = ReadUInt16(font, tables["hhea"] + 34);
            if (metricsCount == 0)
                return false;

            var hmtx = tables["hmtx"];
            foreach (var (glyph, expected) in advancesByGlyph)
            {
                var index = Math.Min(glyph, metricsCount - 1);
                var offset = hmtx + index * 4;
                if (offset + 2 > font.Length)
                    return false;

                var advance = ReadUInt16(font, offset) * 1000 / unitsPerEm;
                if (Math.Abs(advance - expected) > 1)
                    return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Dictionary<string, int>? ReadTableDirectory(byte[] font)
    {
        if (font.Length < 12)
            return null;

        var version = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(0, 4));
        // 0x00010000 = TrueType, 'true' = TrueType (Mac), 'OTTO' = CFF
        if (version is not 0x00010000 and not 0x74727565 and not 0x4F54544F)
            return null;

        var tableCount = ReadUInt16(font, 4);
        var tables = new Dictionary<string, int>(tableCount, StringComparer.Ordinal);
        for (var i = 0; i < tableCount; i++)
        {
            var entry = 12 + i * 16;
            if (entry + 16 > font.Length)
                return null;
            var tag = System.Text.Encoding.ASCII.GetString(font, entry, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8, 4));
            if (offset < 0 || offset >= font.Length)
                return null;
            tables[tag] = offset;
        }
        return tables;
    }

    private static int ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
}
