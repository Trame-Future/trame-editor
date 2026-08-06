using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

public sealed record PdfCompressResult(long BeforeBytes, long AfterBytes, int ImagesRecompressed);

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
