using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TrameEditor.App.Services;

namespace TrameEditor.App.ViewModels;

public partial class PdfPageViewModel : ObservableObject
{
    private readonly PdfRenderService _renderer;
    private bool _imageRequested;
    private bool _thumbnailRequested;

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

    /// <summary>Righe di testo cliccabili in modalità "Modifica testo".</summary>
    public ObservableCollection<PdfTextRegionViewModel> EditRegions { get; } = [];

    public PdfPageViewModel(PdfRenderService renderer, int originalIndex)
    {
        _renderer = renderer;
        OriginalIndex = originalIndex;
    }

    public async Task EnsureImageAsync()
    {
        if (_imageRequested)
            return;
        _imageRequested = true;
        try
        {
            var image = await _renderer.RenderPageAsync(OriginalIndex);
            Image = image;
            BaseWidth = ((System.Windows.Media.Imaging.BitmapSource)image).PixelWidth / PdfRenderService.FullScale;
        }
        catch
        {
            _imageRequested = false; // pagina non renderizzabile ora: si ritenterà
        }
    }

    public async Task EnsureThumbnailAsync()
    {
        if (_thumbnailRequested)
            return;
        _thumbnailRequested = true;
        try
        {
            Thumbnail = await _renderer.RenderThumbnailAsync(OriginalIndex);
        }
        catch
        {
            _thumbnailRequested = false;
        }
    }

    public void Rotate(int delta) => RotationDelta = ((RotationDelta + delta) % 360 + 360) % 360;
}
