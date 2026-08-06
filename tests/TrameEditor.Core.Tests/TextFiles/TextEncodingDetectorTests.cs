using System.Text;
using TrameEditor.Core.TextFiles;

namespace TrameEditor.Core.Tests.TextFiles;

public class TextEncodingDetectorTests
{
    [Fact]
    public void Detect_Utf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x63, 0x69, 0x61, 0x6F };
        Assert.Equal((TextEncodingKind.Utf8, true, 3), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_Utf16LeBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("ciao")).ToArray();
        Assert.Equal((TextEncodingKind.Utf16Le, true, 2), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_Utf16BeBom()
    {
        var bytes = Encoding.BigEndianUnicode.GetPreamble()
            .Concat(Encoding.BigEndianUnicode.GetBytes("ciao")).ToArray();
        Assert.Equal((TextEncodingKind.Utf16Be, true, 2), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_ValidUtf8WithoutBom()
    {
        var bytes = Encoding.UTF8.GetBytes("città però àèìòù");
        Assert.Equal((TextEncodingKind.Utf8, false, 0), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_PlainAsciiIsUtf8()
    {
        var bytes = Encoding.ASCII.GetBytes("hello world");
        Assert.Equal((TextEncodingKind.Utf8, false, 0), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_InvalidUtf8FallsBackToAnsi()
    {
        // "città" in Windows-1252: 0xE0 isolato non è UTF-8 valido
        var bytes = new byte[] { 0x63, 0x69, 0x74, 0x74, 0xE0 };
        Assert.Equal((TextEncodingKind.Ansi, false, 0), TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_EmptyFileIsUtf8()
    {
        Assert.Equal((TextEncodingKind.Utf8, false, 0), TextEncodingDetector.Detect([]));
    }
}
