using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TrameEditor.App.Services;

namespace TrameEditor.App.ViewModels;

public partial class PdfPageViewModel : ObservableObject
{
    private PdfRenderService _renderer;
    private bool _imageRequested;
    private bool _thumbnailRequested;

    /// <summary>Cresce a ogni cambio di file: un disegno partito sul file vecchio che arriva
    /// dopo va buttato, o coprirebbe la pagina già aggiornata con quella di prima.</summary>
    private int _generation;

    public int OriginalIndex { get; }

    public int PageNumber => OriginalIndex + 1;

    [ObservableProperty]
    private int _rotationDelta;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _image;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    /// <summary>Larghezza a zoom 100% (in unità WPF); A4 finché la pagina non è renderizzata.</summary>
    [ObservableProperty]
    private double _baseWidth = 595;

    /// <summary>Altezza a zoom 100% (coincide con i punti PDF), per convertire i click in coordinate PDF.</summary>
    [ObservableProperty]
    private double _baseHeight = 842;

    /// <summary>Righe di testo cliccabili in modalità "Modifica testo".</summary>
    public ObservableCollection<PdfTextRegionViewModel> EditRegions { get; } = [];

    /// <summary>Rettangoli gialli dei risultati di ricerca sulla pagina.</summary>
    public ObservableCollection<PdfTextRegionViewModel> SearchHighlights { get; } = [];

    public PdfPageViewModel(PdfRenderService renderer, int originalIndex)
    {
        _renderer = renderer;
        OriginalIndex = originalIndex;
    }

    /// <summary>
    /// Passa la pagina al file di lavoro nuovo, dopo una modifica, <b>senza ricrearla</b>.
    /// Ricrearla voleva dire svuotare l'elenco delle pagine: la vista restava un istante
    /// vuota, lo scorrimento tornava a zero, e chi correggeva righe in fondo al documento si
    /// ritrovava all'inizio a ogni modifica.
    /// <para>
    /// L'immagine vecchia resta finché non è pronta la nuova: la pagina non cambia altezza
    /// nel frattempo, quindi niente scivola sotto gli occhi di chi sta lavorando.
    /// </para>
    /// </summary>
    public void Rebind(PdfRenderService renderer)
    {
        _renderer = renderer;
        _generation++;

        // Le regioni e i risultati di ricerca parlavano del file vecchio.
        EditRegions.Clear();
        SearchHighlights.Clear();

        // Le pagine già disegnate vanno ridisegnate subito: la vista le ha già caricate e
        // non chiederà più il disegno da sé. Quelle mai viste lo chiederanno quando serve.
        var wasShown = Image is not null;
        var hadThumbnail = Thumbnail is not null;
        _imageRequested = false;
        _thumbnailRequested = false;
        if (wasShown)
            _ = EnsureImageAsync();
        if (hadThumbnail)
            _ = EnsureThumbnailAsync();
    }

    public async Task EnsureImageAsync()
    {
        if (_imageRequested)
            return;
        _imageRequested = true;
        var generation = _generation;
        try
        {
            var image = await _renderer.RenderPageAsync(OriginalIndex);
            if (generation != _generation)
                return; // arrivato dal file di prima: c'è già un disegno più recente in corso
            Image = image;
            var bitmap = (System.Windows.Media.Imaging.BitmapSource)image;
            BaseWidth = bitmap.PixelWidth / PdfRenderService.FullScale;
            BaseHeight = bitmap.PixelHeight / PdfRenderService.FullScale;
        }
        catch
        {
            if (generation == _generation)
                _imageRequested = false; // pagina non renderizzabile ora: si ritenterà
        }
    }

    public async Task EnsureThumbnailAsync()
    {
        if (_thumbnailRequested)
            return;
        _thumbnailRequested = true;
        var generation = _generation;
        try
        {
            var thumbnail = await _renderer.RenderThumbnailAsync(OriginalIndex);
            if (generation == _generation)
                Thumbnail = thumbnail;
        }
        catch
        {
            if (generation == _generation)
                _thumbnailRequested = false;
        }
    }

    public void Rotate(int delta) => RotationDelta = ((RotationDelta + delta) % 360 + 360) % 360;
}
