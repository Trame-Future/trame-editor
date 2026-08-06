using System.Text;

namespace TrameEditor.Core.TextFiles;

public static class TextEncodingDetector
{
    /// <summary>
    /// Rileva l'encoding dai byte del file: prima i BOM (UTF-8, UTF-16 LE/BE),
    /// poi decodifica UTF-8 stretta; se fallisce il file è trattato come ANSI
    /// (Windows-1252), che accetta qualunque sequenza di byte.
    /// </summary>
    public static (TextEncodingKind Kind, bool HasBom, int BomLength) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (TextEncodingKind.Utf8, true, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (TextEncodingKind.Utf16Le, true, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (TextEncodingKind.Utf16Be, true, 2);

        return IsValidUtf8(bytes)
            ? (TextEncodingKind.Utf8, false, 0)
            : (TextEncodingKind.Ansi, false, 0);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
