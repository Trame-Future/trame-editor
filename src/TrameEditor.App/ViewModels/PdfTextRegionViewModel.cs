using TrameEditor.Core.Pdf;

namespace TrameEditor.App.ViewModels;

/// <summary>
/// Riga di testo cliccabile sovrapposta alla pagina renderizzata.
/// Coordinate in unità display a zoom 100% (che coincidono con i punti PDF,
/// dato che il render è a 2x e mostrato a metà), origine in alto a sinistra.
/// </summary>
public sealed class PdfTextRegionViewModel
{
    public PdfTextLine Line { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public bool IsEditable => Line.IsEditable;

    public string ToolTipText => Line.IsEditable
        ? Line.Text
        : Line.NotEditableReason ?? "Non modificabile";

    public PdfTextRegionViewModel(PdfTextLine line, double pageHeight)
    {
        Line = line;
        X = line.Left;
        Y = pageHeight - line.Top;
        Width = line.Width;
        Height = line.Height;
    }
}
