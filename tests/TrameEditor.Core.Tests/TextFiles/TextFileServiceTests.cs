using System.Text;
using TrameEditor.Core.TextFiles;

namespace TrameEditor.Core.Tests.TextFiles;

public class TextFileServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Load_Utf8Bom_StripsBomAndDetectsFormat()
    {
        var path = PathFor("bom.txt");
        File.WriteAllText(path, "prima\r\nseconda", new UTF8Encoding(true));

        var loaded = TextFileService.Load(path);

        Assert.Equal("prima\r\nseconda", loaded.Content);
        Assert.Equal(new TextFileFormat(TextEncodingKind.Utf8, true, LineEnding.Crlf), loaded.Format);
    }

    [Fact]
    public void Load_Ansi_DecodesAccentedCharacters()
    {
        var path = PathFor("ansi.txt");
        File.WriteAllBytes(path, [0x63, 0x69, 0x74, 0x74, 0xE0, 0x0A, 0x70, 0x69, 0xF9]); // "città\npiù"

        var loaded = TextFileService.Load(path);

        Assert.Equal("città\npiù", loaded.Content);
        Assert.Equal(new TextFileFormat(TextEncodingKind.Ansi, false, LineEnding.Lf), loaded.Format);
    }

    [Fact]
    public void Load_Utf16Le_Roundtrip()
    {
        var path = PathFor("utf16.txt");
        File.WriteAllText(path, "héllo\r\nwörld", Encoding.Unicode);

        var loaded = TextFileService.Load(path);
        Assert.Equal("héllo\r\nwörld", loaded.Content);
        Assert.Equal(TextEncodingKind.Utf16Le, loaded.Format.EncodingKind);

        TextFileService.Save(path, "nuovo çontenuto", loaded.Format);
        Assert.Equal("nuovo çontenuto", TextFileService.Load(path).Content);
        Assert.Equal([0xFF, 0xFE], File.ReadAllBytes(path).Take(2));
    }

    [Fact]
    public void Save_PreservesBomAndEncoding()
    {
        var path = PathFor("preserve.txt");
        var format = new TextFileFormat(TextEncodingKind.Utf8, HasBom: true, LineEnding.Crlf);

        TextFileService.Save(path, "testo però", format);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("testo però", TextFileService.Load(path).Content);
    }

    [Fact]
    public void Save_NormalizesMixedLineEndings()
    {
        var path = PathFor("mixed.txt");
        var format = new TextFileFormat(TextEncodingKind.Utf8, false, LineEnding.Crlf);

        TextFileService.Save(path, "a\nb\r\nc\rd", format);

        Assert.Equal("a\r\nb\r\nc\r\nd", File.ReadAllText(path));
    }

    [Fact]
    public void Save_OverExistingFile_ReplacesContent()
    {
        var path = PathFor("replace.txt");
        File.WriteAllText(path, "vecchio contenuto molto più lungo del nuovo");

        TextFileService.Save(path, "nuovo", TextFileFormat.Default);

        Assert.Equal("nuovo", File.ReadAllText(path));
    }

    [Fact]
    public void Save_LeavesNoTempFiles()
    {
        var path = PathFor("clean.txt");
        TextFileService.Save(path, "contenuto", TextFileFormat.Default);
        TextFileService.Save(path, "contenuto 2", TextFileFormat.Default);

        Assert.Equal(["clean.txt"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("a\r\nb", LineEnding.Crlf)]
    [InlineData("a\nb", LineEnding.Lf)]
    [InlineData("a\rb", LineEnding.Cr)]
    [InlineData("senza a capo", LineEnding.Crlf)]
    public void DetectLineEnding_Cases(string content, LineEnding expected)
    {
        Assert.Equal(expected, TextFileService.DetectLineEnding(content));
    }
}
