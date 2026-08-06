using System.Text;

namespace TrameEditor.Core.TextFiles;

public sealed record LoadedTextFile(string Content, TextFileFormat Format);

public static class TextFileService
{
    public static LoadedTextFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (kind, hasBom, bomLength) = TextEncodingDetector.Detect(bytes);
        var format = new TextFileFormat(kind, hasBom, LineEnding.Crlf);
        var content = format.CreateEncoding().GetString(bytes, bomLength, bytes.Length - bomLength);
        return new LoadedTextFile(content, format with { LineEnding = DetectLineEnding(content) });
    }

    /// <summary>
    /// Salvataggio atomico: scrive su un file temporaneo nella stessa cartella e
    /// poi lo scambia con la destinazione, così un crash a metà scrittura non
    /// lascia mai il file di destinazione corrotto o troncato.
    /// </summary>
    public static void Save(string path, string content, TextFileFormat format)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Percorso senza cartella: {path}", nameof(path));

        var normalized = NormalizeLineEndings(content, format.LineEnding);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, normalized, format.CreateEncoding());
            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static LineEnding DetectLineEnding(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '\r':
                    return i + 1 < content.Length && content[i + 1] == '\n' ? LineEnding.Crlf : LineEnding.Cr;
                case '\n':
                    return LineEnding.Lf;
            }
        }
        return LineEnding.Crlf;
    }

    public static string NormalizeLineEndings(string content, LineEnding lineEnding)
    {
        var unified = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return lineEnding == LineEnding.Lf ? unified : unified.Replace("\n", lineEnding.ToLiteral());
    }
}
