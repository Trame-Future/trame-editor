using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TrameEditor.App.Controls;

/// <summary>
/// Indicatore di attesa Trame Future: i 12 fotogrammi della rotazione del
/// pianeta (sprite sheet 4×3) riprodotti in ciclo. L'animazione gira solo
/// quando il controllo è visibile.
/// </summary>
public partial class PlanetSpinner : UserControl
{
    private const int Columns = 4;
    private const int Rows = 3;
    private static BitmapSource[]? _frames;

    private readonly DispatcherTimer _timer;
    private int _frameIndex;

    public PlanetSpinner()
    {
        InitializeComponent();
        _frames ??= LoadFrames();
        FrameImage.Source = _frames[0];
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += (_, _) =>
        {
            _frameIndex = (_frameIndex + 1) % _frames.Length;
            FrameImage.Source = _frames[_frameIndex];
        };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                _timer.Start();
            else
                _timer.Stop();
        };
    }

    private static BitmapSource[] LoadFrames()
    {
        var sheet = new BitmapImage(new Uri("pack://application:,,,/Assets/planet-sprite.png"));
        var frameWidth = sheet.PixelWidth / Columns;
        var frameHeight = sheet.PixelHeight / Rows;
        var frames = new BitmapSource[Columns * Rows];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var frame = new CroppedBitmap(sheet,
                    new Int32Rect(column * frameWidth, row * frameHeight, frameWidth, frameHeight));
                frame.Freeze();
                frames[row * Columns + column] = frame;
            }
        }
        return frames;
    }
}
