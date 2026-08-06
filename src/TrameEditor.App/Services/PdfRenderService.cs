using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace TrameEditor.App.Services;

/// <summary>
/// Rendering delle pagine PDF via PDFium (Docnet). Il file è letto in memoria,
/// quindi nessun lock sul file originale. PDFium non è thread-safe: tutte le
/// operazioni passano da un semaforo.
/// </summary>
public sealed class PdfRenderService : IDisposable
{
    /// <summary>Pagine renderizzate a 2x: nitide fino al 200% di zoom senza ri-render.</summary>
    public const double FullScale = 2.0;

    /// <summary>Scala di render per l'OCR (~216 dpi): pixel per punto PDF.</summary>
    public const double OcrScale = 3.0;

    private const double ThumbScale = 0.25;

    private readonly byte[] _fileBytes;
    private readonly IDocReader _fullReader;
    private readonly IDocReader _thumbReader;
    private IDocReader? _ocrReader;

    // PDFium non è thread-safe a livello di processo: il semaforo è condiviso
    // tra tutte le istanze (viewer, miniature, OCR).
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, string> _textCache = [];

    public int PageCount { get; }

    public PdfRenderService(string path)
    {
        _fileBytes = File.ReadAllBytes(path);
        _fullReader = DocLib.Instance.GetDocReader(_fileBytes, new PageDimensions(FullScale));
        _thumbReader = DocLib.Instance.GetDocReader(_fileBytes, new PageDimensions(ThumbScale));
        PageCount = _fullReader.GetPageCount();
    }

    /// <summary>Render sincrono ad alta risoluzione per l'OCR, come PNG.
    /// Da chiamare fuori dal thread UI.</summary>
    public byte[] RenderPagePngForOcr(int pageIndex)
    {
        _gate.Wait();
        try
        {
            _ocrReader ??= DocLib.Instance.GetDocReader(_fileBytes, new PageDimensions(OcrScale));
            var bitmap = Render(_ocrReader, pageIndex);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BitmapSource> RenderPageAsync(int pageIndex) => RunLocked(() => Render(_fullReader, pageIndex));

    public Task<BitmapSource> RenderThumbnailAsync(int pageIndex) => RunLocked(() => Render(_thumbReader, pageIndex));

    public Task<string> GetPageTextAsync(int pageIndex) => RunLocked(() =>
    {
        if (_textCache.TryGetValue(pageIndex, out var cached))
            return cached;
        using var page = _thumbReader.GetPageReader(pageIndex);
        var text = page.GetText() ?? string.Empty;
        _textCache[pageIndex] = text;
        return text;
    });

    private async Task<T> RunLocked<T>(Func<T> work)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BitmapSource Render(IDocReader reader, int pageIndex)
    {
        using var page = reader.GetPageReader(pageIndex);
        var width = page.GetPageWidth();
        var height = page.GetPageHeight();
        var pixels = page.GetImage(new NaiveTransparencyRemover(255, 255, 255));
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        _fullReader.Dispose();
        _thumbReader.Dispose();
        _ocrReader?.Dispose();
    }
}
