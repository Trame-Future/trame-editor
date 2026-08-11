using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

public sealed record PdfCompressResult(long BeforeBytes, long AfterBytes, int ImagesRecompressed);

/// <summary>Esito della compressione con un peso da rispettare.</summary>
public sealed record PdfTargetCompressResult(
    long BeforeBytes,
    long AfterBytes,
    long TargetBytes,
    int MaxDimension,
    long JpegQuality,
    bool TargetReached)
{
    /// <summary>Che cosa è stato sacrificato per rientrare, detto in chiaro.</summary>
    public string Sacrifices => TargetReached
        ? $"immagini ridotte a {MaxDimension} pixel e qualità {JpegQuality}"
        : $"nemmeno alla qualità più bassa ({MaxDimension} pixel, qualità {JpegQuality}) " +
          "il file rientra nel limite: quello che vedi è il meglio ottenibile";
}

/// <summary>
/// Riduce il peso di un PDF: ricomprime le immagini JPEG (ridimensionandole se
/// oltre <c>maxDimension</c> pixel) e riscrive il file in modalità full
/// compression. Le immagini con trasparenza o con filtri non JPEG sono lasciate
/// intatte per non degradare il documento.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PdfCompressor
{
    public static PdfCompressResult Compress(string sourcePath, string targetPath,
        int maxDimension = 1600, long jpegQuality = 75)
    {
        var beforeBytes = new FileInfo(sourcePath).Length;
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        var recompressed = 0;
        try
        {
            var writerProperties = new WriterProperties().SetFullCompressionMode(true);
            using (var document = new PdfDocument(new PdfReader(sourcePath),
                new PdfWriter(tempPath, writerProperties)))
            {
                for (var i = 1; i <= document.GetNumberOfPdfObjects(); i++)
                {
                    if (document.GetPdfObject(i) is PdfStream stream && TryRecompressImage(stream, maxDimension, jpegQuality))
                        recompressed++;
                }
            }

            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return new PdfCompressResult(beforeBytes, new FileInfo(fullTarget).Length, recompressed);
    }

    /// <summary>
    /// Comprime finché il file non sta sotto il peso richiesto — il caso classico
    /// è "deve entrare nella PEC". Si prova per gradi, dal meno invasivo al più
    /// invasivo, e ci si ferma appena il limite è rispettato: non si degrada il
    /// documento più del necessario.
    /// <para>
    /// Se nemmeno il grado più aggressivo basta, si consegna comunque il file più
    /// piccolo ottenuto e <b>lo si dichiara</b>, invece di far credere che il
    /// limite sia stato rispettato.
    /// </para>
    /// </summary>
    public static PdfTargetCompressResult CompressToTarget(string sourcePath, string targetPath,
        long targetBytes)
    {
        if (targetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "Il peso richiesto deve essere positivo.");

        var beforeBytes = new FileInfo(sourcePath).Length;

        // Dal più conservativo al più drastico: dimensione massima e qualità JPEG.
        (int MaxDimension, long Quality)[] steps =
        [
            (2200, 85), (1600, 75), (1200, 65), (900, 55), (700, 45), (500, 35),
        ];

        PdfTargetCompressResult? best = null;
        foreach (var (maxDimension, quality) in steps)
        {
            var result = Compress(sourcePath, targetPath, maxDimension, quality);
            var attempt = new PdfTargetCompressResult(beforeBytes, result.AfterBytes, targetBytes,
                maxDimension, quality, result.AfterBytes <= targetBytes);

            if (attempt.TargetReached)
                return attempt;
            best = attempt;
        }

        return best!;
    }

    private static bool TryRecompressImage(PdfStream stream, int maxDimension, long jpegQuality)
    {
        if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
            return false;
        if (stream.ContainsKey(PdfName.SMask) || stream.ContainsKey(PdfName.Mask))
            return false; // trasparenza: non toccare

        var filter = stream.Get(PdfName.Filter);
        var isJpeg = PdfName.DCTDecode.Equals(filter) ||
            (filter is PdfArray filters && filters.Size() == 1 && PdfName.DCTDecode.Equals(filters.GetAsName(0)));
        if (!isJpeg)
            return false;

        try
        {
            var original = stream.GetBytes(false);
            using var source = Image.FromStream(new MemoryStream(original));
            var scale = Math.Min(1.0, (double)maxDimension / Math.Max(source.Width, source.Height));
            using var resized = new Bitmap(source,
                Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));

            var codec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
            using var output = new MemoryStream();
            resized.Save(output, codec, parameters);
            var data = output.ToArray();

            if (data.Length >= original.Length * 0.9)
                return false; // il risparmio non giustifica la riscrittura

            stream.Clear();
            stream.SetData(data);
            stream.Put(PdfName.Type, PdfName.XObject);
            stream.Put(PdfName.Subtype, PdfName.Image);
            stream.Put(PdfName.Filter, PdfName.DCTDecode);
            stream.Put(PdfName.Width, new PdfNumber(resized.Width));
            stream.Put(PdfName.Height, new PdfNumber(resized.Height));
            stream.Put(PdfName.BitsPerComponent, new PdfNumber(8));
            stream.Put(PdfName.ColorSpace, PdfName.DeviceRGB);
            stream.Remove(PdfName.DecodeParms);
            stream.Remove(PdfName.Decode);
            return true;
        }
        catch
        {
            return false; // immagine non decodificabile: lasciata intatta
        }
    }
}
