using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Una sola lettura del contenuto della pagina che raccoglie ciò che serve per
/// giudicarne la conformità PDF/A: se usa colori CMYK e, per ogni font, quali
/// caratteri sono <b>davvero</b> scritti.
/// <para>
/// I caratteri usati contano: due font metricamente compatibili (Helvetica e
/// Arial) differiscono su una manciata di simboli rari. Se quei simboli nel
/// documento non ci sono, sostituire il font non cambia nulla; se ci sono, non
/// possiamo farlo. Senza questa distinzione rifiuteremmo documenti perfettamente
/// convertibili.
/// </para>
/// </summary>
internal sealed class PdfPageScan
{
    private readonly Dictionary<object, HashSet<int>> _charactersByFont = [];

    public bool UsesCmyk { get; private set; }

    /// <summary>Caratteri (code point) scritti con questo font nella pagina.</summary>
    public IReadOnlyCollection<int> CharactersUsedBy(PdfDictionary font) =>
        _charactersByFont.TryGetValue(KeyOf(font), out var characters) ? characters : [];

    /// <summary>I dizionari vengono da strade diverse (risorse, processore del
    /// contenuto): il riferimento indiretto è l'identità affidabile.</summary>
    private static object KeyOf(PdfDictionary font) =>
        (object?)font.GetIndirectReference() ?? font;

    public static PdfPageScan Run(PdfPage page)
    {
        var scan = new PdfPageScan();
        var listener = new TextListener(scan);
        var processor = new ColorAndTextProcessor(scan, listener);
        processor.ProcessPageContent(page);
        return scan;
    }

    private void NoteCmyk() => UsesCmyk = true;

    private void NoteCharacters(PdfDictionary font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var key = KeyOf(font);
        if (!_charactersByFont.TryGetValue(key, out var characters))
            _charactersByFont[key] = characters = [];

        for (var i = 0; i < text.Length; i++)
        {
            var codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                ? char.ConvertToUtf32(text[i], text[++i])
                : text[i];
            characters.Add(codePoint);
        }
    }

    private sealed class ColorAndTextProcessor(PdfPageScan scan, IEventListener listener)
        : PdfCanvasProcessor(listener)
    {
        protected override void InvokeOperator(PdfLiteral oper, IList<PdfObject> operands)
        {
            if (oper.ToString() is "k" or "K")
                scan.NoteCmyk();
            base.InvokeOperator(oper, operands);
        }
    }

    private sealed class TextListener(PdfPageScan scan) : IEventListener
    {
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info)
                return;
            var font = info.GetFont()?.GetPdfObject();
            if (font is not null)
                scan.NoteCharacters(font, info.GetText());
        }

        public ICollection<EventType>? GetSupportedEvents() => null;
    }
}
