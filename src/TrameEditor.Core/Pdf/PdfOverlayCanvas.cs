using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace TrameEditor.Core.Pdf;

internal static class PdfOverlayCanvas
{
    /// <summary>
    /// Canvas per disegnare SOPRA il contenuto esistente di una pagina con stato
    /// grafico pulito: molti PDF reali (es. generati da Word) lasciano attive
    /// trasformazioni (q/cm mai richiusi) in coda al content stream, e un canvas
    /// accodato ingenuamente le erediterebbe, disegnando scalato o fuori posto.
    /// Il contenuto originale viene avvolto tra "q" (stream in testa) e "Q"
    /// (in coda, prima dei nostri operatori). Scritti raw perché PdfCanvas
    /// rifiuta un RestoreState senza SaveState corrispondente nello stesso stream.
    /// </summary>
    public static PdfCanvas Create(PdfDocument document, PdfPage page)
    {
        new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), document)
            .GetContentStream().GetOutputStream().WriteString("q\n");
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), document);
        canvas.GetContentStream().GetOutputStream().WriteString("Q\n");
        return canvas;
    }
}
