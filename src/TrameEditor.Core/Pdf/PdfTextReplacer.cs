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
    /// rappresentabili diventano '?'. Le righe per cui non si trova nessun operatore
    /// di testo vengono riportate come saltate — mai silenziate.
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
    /// Lancia <see cref="PdfTextEditException"/> se in quella posizione non si trova
    /// nessun operatore di testo da togliere.
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
                        "In quella posizione non è stato trovato nessun operatore di testo: " +
                        "la riga non fa parte del contenuto disegnato della pagina (può venire " +
                        "da un'annotazione o da un livello non modificabile). " +
                        "Modifica non applicabile.");

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

    /// <summary>Cerca il font per nome nelle risorse della pagina e, a scendere, in
    /// quelle dei form XObject: il testo disegnato dentro un modulo usa i font del modulo,
    /// che nelle risorse della pagina non compaiono.</summary>
    private static PdfDictionary? FindFontDictionary(PdfPage page, string fontName) =>
        string.IsNullOrEmpty(fontName)
            ? null
            : FindFontIn(page.GetResources().GetPdfObject(), fontName,
                new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));

    private static PdfDictionary? FindFontIn(PdfDictionary? resources, string fontName,
        HashSet<PdfDictionary> visited)
    {
        if (resources is null || !visited.Add(resources))
            return null;

        var fonts = resources.GetAsDictionary(PdfName.Font);
        if (fonts is not null)
            foreach (var key in fonts.KeySet())
            {
                var candidate = fonts.GetAsDictionary(key);
                if (candidate?.GetAsName(PdfName.BaseFont)?.GetValue() == fontName)
                    return candidate;
            }

        var xobjects = resources.GetAsDictionary(PdfName.XObject);
        if (xobjects is null)
            return null;
        foreach (var key in xobjects.KeySet())
        {
            var form = xobjects.GetAsStream(key);
            if (form is null || !PdfName.Form.Equals(form.GetAsName(PdfName.Subtype)))
                continue;
            if (FindFontIn(form.GetAsDictionary(PdfName.Resources), fontName, visited) is { } found)
                return found;
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

    /// <summary>Font di sistema equivalente a <paramref name="fontName"/>, o null.
    /// Condiviso con la conversione PDF/A, che deve incorporare i font mancanti.</summary>
    internal static string? FindSystemFontFor(string fontName) => FindSystemFont(fontName);

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
    /// riga bersaglio. La ricopiatura scende anche dentro i form XObject, dove molti
    /// gestionali disegnano il corpo del documento: siccome lo stesso form può essere
    /// richiamato da più pagine (o più volte nella stessa), non viene mai modificato
    /// sul posto ma copiato, e solo l'occorrenza in corso punta alla copia. Un form
    /// in cui non si toglie nulla resta il riferimento originale.
    /// </summary>
    private sealed class LineRemovalEditor : PdfCanvasProcessor
    {
        private readonly IReadOnlyList<PdfTextLine> _lines;
        private readonly Dictionary<PdfTextLine, int> _removedPerLine = [];
        private readonly CaptureListener _listener;
        private readonly Stack<Target> _targets = new();
        private PdfDocument _document = null!;

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

        /// <summary>Dove sta andando la ricopiatura in questo momento: la pagina, oppure
        /// la copia del form XObject in cui si è scesi.</summary>
        private sealed class Target(PdfCanvas canvas, PdfResources resources)
        {
            public PdfCanvas Canvas { get; } = canvas;

            public PdfResources Resources { get; } = resources;

            /// <summary>Vero se qui dentro è stato tolto del testo, oppure se un form
            /// figlio è stato sostituito con la propria copia.</summary>
            public bool Modified { get; set; }
        }

        public void EditPage(PdfDocument document, PdfPage page)
        {
            _document = document;
            var resources = page.GetResources();
            var newContent = (PdfStream)new PdfStream().MakeIndirect(document);
            _targets.Push(new Target(new PdfCanvas(newContent, resources, document), resources));
            try
            {
                ProcessPageContent(page);
            }
            finally
            {
                _targets.Clear();
            }
            page.GetPdfObject().Put(PdfName.Contents, newContent);
            page.GetPdfObject().SetModified();
        }

        protected override void InvokeOperator(PdfLiteral oper, IList<PdfObject> operands)
        {
            var op = oper.ToString();

            if (op == "Do")
            {
                InvokeDo(oper, operands);
                return;
            }

            var isShowText = op is "Tj" or "TJ" or "'" or "\"";
            if (isShowText)
                _listener.Pending.Clear();

            base.InvokeOperator(oper, operands);

            if (isShowText && FindMatchedLine() is { } matchedLine)
            {
                RemovedCount++;
                _removedPerLine[matchedLine] =
                    (_removedPerLine.TryGetValue(matchedLine, out var count) ? count : 0) + 1;
                _targets.Peek().Modified = true;
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

        /// <summary>
        /// "/Nome Do": se il riferimento è un form XObject si scende dentro, ricopiandone
        /// il contenuto in una copia. Se lì dentro non si è tolto nulla la copia viene
        /// buttata e resta il riferimento originale, così le parti non toccate del
        /// documento restano quelle di prima.
        /// </summary>
        private void InvokeDo(PdfLiteral oper, IList<PdfObject> operands)
        {
            var parent = _targets.Peek();
            var form = operands.Count > 0 && operands[0] is PdfName name
                ? ResolveForm(parent.Resources, name)
                : null;

            if (form is null)
            {
                // Immagine, o riferimento che non si risolve: non c'è contenuto da riscrivere.
                base.InvokeOperator(oper, operands);
                WriteOperands(operands);
                return;
            }

            var copy = CopyWithoutContent(form);
            var resources = form.GetAsDictionary(PdfName.Resources) is { } own
                ? new PdfResources(own)
                : parent.Resources;

            var target = new Target(new PdfCanvas(copy, resources, _document), resources);
            _targets.Push(target);
            try
            {
                base.InvokeOperator(oper, operands);
            }
            finally
            {
                _targets.Pop();
            }

            if (!target.Modified)
            {
                WriteOperands(operands);
                return;
            }

            copy.MakeIndirect(_document);
            var replacement = parent.Resources.AddForm(copy);
            parent.Modified = true;
            WriteObject(replacement);
            WriteRaw(" Do\n");
        }

        /// <summary>Il form XObject richiamato dal nome, o null se è un'immagine
        /// (o un riferimento che non si risolve).</summary>
        private static PdfStream? ResolveForm(PdfResources resources, PdfName name)
        {
            var stream = resources.GetResource(PdfName.XObject)?.GetAsStream(name);
            return stream is not null && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype))
                ? stream
                : null;
        }

        /// <summary>Copia il dizionario del form (BBox, Matrix, Resources, Group…) ma non
        /// i dati: il contenuto viene riscritto da capo, quindi lunghezza e filtri di
        /// compressione dell'originale non valgono più.</summary>
        private static PdfStream CopyWithoutContent(PdfStream form)
        {
            var copy = new PdfStream();
            foreach (var key in form.KeySet())
            {
                if (key.Equals(PdfName.Length) || key.Equals(PdfName.Filter) ||
                    key.Equals(PdfName.DecodeParms))
                    continue;
                copy.Put(key, form.Get(key));
            }
            return copy;
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

        private PdfOutputStream Output() =>
            _targets.Peek().Canvas.GetContentStream().GetOutputStream();

        private void WriteOperands(IList<PdfObject> operands)
        {
            var output = Output();
            for (var i = 0; i < operands.Count; i++)
            {
                output.Write(operands[i]);
                output.WriteBytes(i == operands.Count - 1 ? NewLine : Space);
            }
        }

        private void WriteObject(PdfObject obj) => Output().Write(obj);

        private void WriteRaw(string text) => Output().WriteString(text);

        private void WriteInlineImage(PdfStream image)
        {
            var output = Output();
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
