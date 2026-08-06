namespace TrameEditor.Core.TextFiles;

public enum LineEnding
{
    Crlf,
    Lf,
    Cr,
}

public static class LineEndingExtensions
{
    public static string ToLiteral(this LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Crlf => "\r\n",
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => throw new ArgumentOutOfRangeException(nameof(lineEnding)),
    };

    public static string DisplayName(this LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Crlf => "CRLF",
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => throw new ArgumentOutOfRangeException(nameof(lineEnding)),
    };
}
