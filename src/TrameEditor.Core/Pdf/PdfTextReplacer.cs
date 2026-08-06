using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Sostituzione del testo di una riga dentro un PDF: gli operatori di testo
/// originali vengono rimossi dal content stream (non coperti) e il nuovo testo
/// è disegnato nella stessa posizione con il font deciso da <see cref="PlanFor"/>.
/// </summary>
public static class PdfTextReplacer
{
    /// <summary>
    /// Decide il font per il nuovo testo, in ordine di preferenza:
    /// riuso del font incorporato → font di sistema equivalente → sostituto standard.
    /// </summary>
    public static PdfFontPlan PlanFor(string sourcePath, PdfTextLine line, string newText)
    {
        using var document = new PdfDocument(new PdfReader(sourcePath));
        return ResolvePlan(document.GetPage(line.PageNumber), line, newText);
    }

    private static PdfFontPlan ResolvePlan(PdfPage page, PdfTextLine line, string newText)
    {
        var fontDict = FindFontDictionary(page, line.FontName);
        if (fontDict is not null && CanReuse(fontDict, newText))
            return new PdfFontPlan(PdfFontStrategy.ReuseEmbedded,
                $"font originale ({CleanFontName(line.FontName)})", null, null);

        var systemFontPath = FindSystemFont(line.FontName);
        if (systemFontPath is not null)
            return new PdfFontPlan(PdfFontStrategy.SystemFont,
                $"font di sistema {Path.GetFileNameWithoutExtension(systemFontPath)}",
                systemFontPath, null);

        var standard = GuessStandardFont(line.FontName);
        return new PdfFontPlan(PdfFontStrategy.Substitute,
            $"font sostitutivo {standard}", null, standard);
    }

    /// <summary>
    /// Sostituisce più righe in un solo passaggio (usato dall'anonimizzazione):
    /// il piano font è risolto automaticamente riga per riga, i caratteri non
    /// rappresentabili diventano '?'. Le righe i cui operatori non sono nel
    /// flusso della pagina vengono riportate come saltate — mai silenziate.
    /// </summary>
    public static PdfReplaceManyResult ReplaceMany(string sourcePath, string targetPath,
        IReadOnlyList<(PdfTextLine Line, string NewText)> edits)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        var skipped = new List<PdfTextLine>();
        var replaced = 0;
        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                foreach (var pageGroup in edits.GroupBy(e => e.Line.PageNumber))
                {
                    var page = document.GetPage(pageGroup.Key);
                    var editor = new LineRemovalEditor(pageGroup.Select(e => e.Line).ToList());
                    editor.EditPage(document, page);

                    PdfCanvas? canvas = null;
                    foreach (var (line, newText) in pageGroup)
                    {
                        if (editor.RemovedCountFor(line) == 0)
                        {
                            skipped.Add(line);
                            continue;
                        }
                        replaced++;
                        if (newText.Length == 0)
                            continue;

                        canvas ??= PdfOverlayCanvas.Create(document, page);
                        var plan = ResolvePlan(page, line, newText);
                        var font = CreateFont(page, plan, line);
                        var safeText = new string(newText
                            .Select(c => c == ' ' || font.ContainsGlyph(c) ? c : '?').ToArray());
                        canvas.BeginText()
                            .SetFontAndSize(font, (float)line.FontSizePt)
                            .SetColor(new DeviceRgb((float)line.ColorR, (float)line.ColorG, (float)line.ColorB), true)
                            .SetTextMatrix((float)line.BaselineX, (float)line.BaselineY)
                            .ShowText(safeText)
                            .EndText();
                    }
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

        return new PdfReplaceManyResult(replaced, skipped);
    }

    /// <summary>
    /// Applica la sostituzione scrivendo un nuovo PDF in <paramref name="targetPath"/>.
    /// Lancia <see cref="PdfTextEditException"/> se gli operatori della riga non sono
    /// nel flusso principale della pagina (es. testo dentro un form XObject).
    /// </summary>
    public static void Replace(string sourcePath, string targetPath,
        PdfTextLine line, string newText, PdfFontPlan plan)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                var page = document.GetPage(line.PageNumber);

                var editor = new LineRemovalEditor(line);
                editor.EditPage(document, page);
                if (editor.RemovedCount == 0)
                    throw new PdfTextEditException(
                        "Il testo selezionato non è nel flusso principale della pagina " +
                        "(probabilmente è dentro un modulo/XObject): modifica non applicabile.");

                if (newText.Length > 0)
                {
                    var font = CreateFont(page, plan, line);
                    var missing = newText.Where(c => c != ' ' && !font.ContainsGlyph(c)).Distinct().ToList();
                    if (missing.Count > 0)
                        throw new PdfTextEditException(
                            $"Il font scelto non contiene questi caratteri: {string.Join(" ", missing)}.");

                    var canvas = PdfOverlayCanvas.Create(document, page);
                    canvas.BeginText()
                        .SetFontAndSize(font, (float)line.FontSizePt)
                        .SetColor(new DeviceRgb((float)line.ColorR, (float)line.ColorG, (float)line.ColorB), true)
                        .SetTextMatrix((float)line.BaselineX, (float)line.BaselineY)
                        .ShowText(newText)
                        .EndText();
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
    }

    // ----- Font -----

    private static PdfFont CreateFont(PdfPage page, PdfFontPlan plan, PdfTextLine line) =>
        plan.Strategy switch
        {
            PdfFontStrategy.ReuseEmbedded => PdfFontFactory.CreateFont(
                FindFontDictionary(page, line.FontName)
                    ?? throw new PdfTextEditException("Font originale non più presente nel PDF.")),
            PdfFontStrategy.SystemFont => PdfFontFactory.CreateFont(plan.SystemFontPath,
                iText.IO.Font.PdfEncodings.IDENTITY_H),
            _ => PdfFontFactory.CreateFont(plan.StandardFontName ?? StandardFonts.HELVETICA),
        };

    private static PdfDictionary? FindFontDictionary(PdfPage page, string fontName)
    {
        var fonts = page.GetResources().GetResource(PdfName.Font);
        if (fonts is null || string.IsNullOrEmpty(fontName))
            return null;
        foreach (var key in fonts.KeySet())
        {
            var candidate = fonts.GetAsDictionary(key);
            var baseFont = candidate?.GetAsName(PdfName.BaseFont)?.GetValue();
            if (baseFont == fontName)
                return candidate;
        }
        return null;
    }

    private static bool CanReuse(PdfDictionary fontDict, string newText)
    {
        try
        {
            var font = PdfFontFactory.CreateFont(fontDict);
            return newText.All(c => font.ContainsGlyph(c));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nome senza l'eventuale prefisso di subset ("ABCDEF+Arial" → "Arial").</summary>
    private static string CleanFontName(string fontName)
    {
        if (fontName.Length > 7 && fontName[6] == '+' &&
            fontName.Take(6).All(char.IsAsciiLetterUpper))
            return fontName[7..];
        return fontName;
    }

    private static readonly (string Key, string Regular, string Bold, string Italic, string BoldItalic)[]
        SystemFontMap =
        [
            ("arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
            ("helvetica", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
            ("timesnewroman", "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
            ("times", "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
            ("couriernew", "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
            ("courier", "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
            ("calibri", "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"),
            ("cambria", "cambria.ttc", "cambriab.ttf", "cambriai.ttf", "cambriaz.ttf"),
            ("verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
            ("georgia", "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"),
            ("tahoma", "tahoma.ttf", "tahomabd.ttf", "tahoma.ttf", "tahomabd.ttf"),
            ("trebuchet", "trebuc.ttf", "trebucbd.ttf", "trebucit.ttf", "trebucbi.ttf"),
            ("segoeui", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        ];

    private static string? FindSystemFont(string fontName)
    {
        var normalized = new string(CleanFontName(fontName)
            .Where(char.IsAsciiLetter).Select(char.ToLowerInvariant).ToArray());
        var bold = normalized.Contains("bold");
        var italic = normalized.Contains("italic") || normalized.Contains("oblique");

        foreach (var entry in SystemFontMap)
        {
            if (!normalized.Contains(entry.Key))
                continue;
            var file = (bold, italic) switch
            {
                (true, true) => entry.BoldItalic,
                (true, false) => entry.Bold,
                (false, true) => entry.Italic,
                _ => entry.Regular,
            };
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", file);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string GuessStandardFont(string fontName)
    {
        var normalized = CleanFontName(fontName).ToLowerInvariant();
        var bold = normalized.Contains("bold");
        var italic = normalized.Contains("italic") || normalized.Contains("oblique");
        if (normalized.Contains("courier") || normalized.Contains("mono"))
            return (bold, italic) switch
            {
                (true, true) => StandardFonts.COURIER_BOLDOBLIQUE,
                (true, false) => StandardFonts.COURIER_BOLD,
                (false, true) => StandardFonts.COURIER_OBLIQUE,
                _ => StandardFonts.COURIER,
            };
        if (normalized.Contains("times") || normalized.Contains("serif") ||
            normalized.Contains("georgia") || normalized.Contains("garamond"))
            return (bold, italic) switch
            {
                (true, true) => StandardFonts.TIMES_BOLDITALIC,
                (true, false) => StandardFonts.TIMES_BOLD,
                (false, true) => StandardFonts.TIMES_ITALIC,
                _ => StandardFonts.TIMES_ROMAN,
            };
        return (bold, italic) switch
        {
            (true, true) => StandardFonts.HELVETICA_BOLDOBLIQUE,
            (true, false) => StandardFonts.HELVETICA_BOLD,
            (false, true) => StandardFonts.HELVETICA_OBLIQUE,
            _ => StandardFonts.HELVETICA,
        };
    }

    // ----- Riscrittura del content stream -----

    /// <summary>
    /// Ricopia il content stream della pagina operatore per operatore, eliminando
    /// gli operatori di testo (Tj/TJ/'/") il cui punto di partenza cade dentro la
    /// riga bersaglio. Gli operatori dentro form XObject non vengono toccati
    /// (il loro contenuto resta referenziato dall'operatore Do).
    /// </summary>
    private sealed class LineRemovalEditor : PdfCanvasProcessor
    {
        private readonly IReadOnlyList<PdfTextLine> _lines;
        private readonly Dictionary<PdfTextLine, int> _removedPerLine = [];
        private readonly CaptureListener _listener;
        private PdfCanvas _canvas = null!;
        private int _xobjectDepth;

        public int RemovedCount { get; private set; }

        public int RemovedCountFor(PdfTextLine line) =>
            _removedPerLine.TryGetValue(line, out var count) ? count : 0;

        public LineRemovalEditor(PdfTextLine line) : this([line], new CaptureListener())
        {
        }

        public LineRemovalEditor(IReadOnlyList<PdfTextLine> lines) : this(lines, new CaptureListener())
        {
        }

        private LineRemovalEditor(IReadOnlyList<PdfTextLine> lines, CaptureListener listener) : base(listener)
        {
            _lines = lines;
            _listener = listener;
        }

        public void EditPage(PdfDocument document, PdfPage page)
        {
            var newContent = (PdfStream)new PdfStream().MakeIndirect(document);
            _canvas = new PdfCanvas(newContent, page.GetResources(), document);
            ProcessPageContent(page);
            page.GetPdfObject().Put(PdfName.Contents, newContent);
            page.GetPdfObject().SetModified();
        }

        protected override void InvokeOperator(PdfLiteral oper, IList<PdfObject> operands)
        {
            var op = oper.ToString();

            if (op == "Do")
            {
                WriteOperands(operands);
                _xobjectDepth++;
                try
                {
                    base.InvokeOperator(oper, operands);
                }
                finally
                {
                    _xobjectDepth--;
                }
                return;
            }

            var isShowText = op is "Tj" or "TJ" or "'" or "\"";
            if (isShowText)
                _listener.Pending.Clear();

            base.InvokeOperator(oper, operands);

            if (_xobjectDepth > 0)
                return;

            if (isShowText && FindMatchedLine() is { } matchedLine)
            {
                RemovedCount++;
                _removedPerLine[matchedLine] =
                    (_removedPerLine.TryGetValue(matchedLine, out var count) ? count : 0) + 1;
                // Preserva gli effetti collaterali di ' e " sul cursore di testo.
                if (op == "'")
                {
                    WriteRaw("T*\n");
                }
                else if (op == "\"")
                {
                    WriteObject(operands[0]);
                    WriteRaw(" Tw ");
                    WriteObject(operands[1]);
                    WriteRaw(" Tc T*\n");
                }
                return;
            }

            if (op == "EI" && operands.Count > 0 && operands[0] is PdfStream inlineImage)
            {
                WriteInlineImage(inlineImage);
                return;
            }

            WriteOperands(operands);
        }

        private PdfTextLine? FindMatchedLine()
        {
            foreach (var start in _listener.Pending)
            {
                foreach (var line in _lines)
                {
                    if (Math.Abs(start.Y - line.BaselineY) <= 2.5 &&
                        start.X >= line.Left - 2 &&
                        start.X <= line.Left + line.Width + 2)
                        return line;
                }
            }
            return null;
        }

        private void WriteOperands(IList<PdfObject> operands)
        {
            var output = _canvas.GetContentStream().GetOutputStream();
            for (var i = 0; i < operands.Count; i++)
            {
                output.Write(operands[i]);
                output.WriteBytes(i == operands.Count - 1 ? NewLine : Space);
            }
        }

        private void WriteObject(PdfObject obj) =>
            _canvas.GetContentStream().GetOutputStream().Write(obj);

        private void WriteRaw(string text) =>
            _canvas.GetContentStream().GetOutputStream().WriteString(text);

        private void WriteInlineImage(PdfStream image)
        {
            var output = _canvas.GetContentStream().GetOutputStream();
            output.WriteString("BI\n");
            foreach (var key in image.KeySet())
            {
                output.Write(key);
                output.WriteBytes(Space);
                output.Write(image.Get(key));
                output.WriteBytes(NewLine);
            }
            output.WriteString("ID\n");
            output.WriteBytes(image.GetBytes(false));
            output.WriteString("\nEI\n");
        }

        private static readonly byte[] Space = [(byte)' '];
        private static readonly byte[] NewLine = [(byte)'\n'];
    }

    private sealed class CaptureListener : IEventListener
    {
        public List<(double X, double Y)> Pending { get; } = [];

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo info)
            {
                var start = info.GetBaseline().GetStartPoint();
                Pending.Add((start.Get(0), start.Get(1)));
            }
        }

        public ICollection<EventType>? GetSupportedEvents() => null;
    }
}
