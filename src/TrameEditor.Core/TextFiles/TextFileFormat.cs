using System.Text;

namespace TrameEditor.Core.TextFiles;

public enum TextEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Ansi,
}

/// <summary>
/// Formato rilevato di un file di testo. Viene preservato al salvataggio:
/// un file aperto ANSI/CRLF viene risalvato ANSI/CRLF.
/// </summary>
public sealed record TextFileFormat(TextEncodingKind EncodingKind, bool HasBom, LineEnding LineEnding)
{
    /// <summary>Default per i nuovi documenti: UTF-8 senza BOM, CRLF (convenzione Windows).</summary>
    public static TextFileFormat Default { get; } = new(TextEncodingKind.Utf8, HasBom: false, LineEnding.Crlf);

    public string EncodingDisplayName => EncodingKind switch
    {
        TextEncodingKind.Utf8 => HasBom ? "UTF-8 con BOM" : "UTF-8",
        TextEncodingKind.Utf16Le => "UTF-16 LE",
        TextEncodingKind.Utf16Be => "UTF-16 BE",
        TextEncodingKind.Ansi => "ANSI (Windows-1252)",
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>Encoding da usare in scrittura (il BOM è emesso dal preambolo quando previsto).</summary>
    public Encoding CreateEncoding() => EncodingKind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: HasBom),
        TextEncodingKind.Utf16Le => new UnicodeEncoding(bigEndian: false, byteOrderMark: HasBom),
        TextEncodingKind.Utf16Be => new UnicodeEncoding(bigEndian: true, byteOrderMark: HasBom),
        TextEncodingKind.Ansi => CodePages.Windows1252,
        _ => throw new ArgumentOutOfRangeException(),
    };
}

internal static class CodePages
{
    // Il provider va registrato prima di GetEncoding: niente inizializzatore di
    // campo, che verrebbe eseguito prima del corpo del costruttore statico.
    internal static Encoding Windows1252 { get; } = CreateWindows1252();

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
