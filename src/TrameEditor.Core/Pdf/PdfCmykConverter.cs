using System.Runtime.Versioning;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Function;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Traduce in sRGB i colori CMYK di un documento, riscrivendo i flussi di
/// contenuto e le immagini. Serve alla conversione in PDF/A: con un profilo di
/// destinazione sRGB i colori CMYK non sarebbero definiti.
/// <para>
/// Copre i colori diretti (<c>k</c>/<c>K</c>), gli spazi colore nominati
/// (DeviceCMYK e ICCBased a 4 canali) e i <b>colori tinta</b> (Separation e
/// DeviceN) valutandone la funzione di trasformazione. Non copre sfumature,
/// motivi e immagini JPEG in CMYK: quelli restano dichiarati come ostacoli,
/// perché tradurli male sarebbe peggio che non tradurli.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PdfCmykConverter
{
    internal sealed record Outcome(int ColorsConverted, int ImagesConverted, string SourceDescription);

    /// <summary>Uno spazio colore che porta a CMYK, e come arrivarci.</summary>
    private sealed record CmykSpace(int Components, IPdfFunction? TintTransform, CmykToRgb Converter)
    {
        public (float R, float G, float B) ToRgb(double[] values)
        {
            var cmyk = TintTransform is null ? values : TintTransform.Calculate(values);
            return cmyk.Length < 4
                ? (0, 0, 0)
                : Converter.Convert(cmyk);
        }
    }

    internal static Outcome Convert(PdfDocument document)
    {
        using var device = CmykToRgb.ForDeviceCmyk();
        var iccConverters = new List<CmykToRgb>();
        var colors = 0;
        var images = 0;

        try
        {
            var rewritten = new HashSet<PdfStream>();
            for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
            {
                var page = document.GetPage(pageNumber);
                var resources = page.GetResources();

                images += ConvertImages(resources, device, iccConverters);
                colors += RewriteStream(document, page, resources, device, iccConverters);
                colors += RewriteForms(document, resources, device, iccConverters, rewritten);
                RemoveConvertedColorSpaces(resources);
                page.GetPdfObject().SetModified();
            }
        }
        finally
        {
            foreach (var converter in iccConverters)
                converter.Dispose();
        }

        return new Outcome(colors, images, device.UsesIccProfiles
            ? $"conversione colorimetrica con {device.SourceDescription}"
            : "conversione con formula aritmetica (profili ICC non disponibili): " +
              "i colori possono risultare leggermente diversi");
    }

    // ----- Riconoscimento degli spazi colore -----

    private static CmykSpace? Classify(PdfObject? colorSpace, CmykToRgb device,
        List<CmykToRgb> iccConverters)
    {
        switch (colorSpace)
        {
            case PdfName name when PdfName.DeviceCMYK.Equals(name):
                return new CmykSpace(4, null, device);

            case PdfArray array when array.Size() >= 2:
                var family = array.GetAsName(0);

                if (PdfName.ICCBased.Equals(family))
                {
                    var stream = array.GetAsStream(1);
                    if (stream?.GetAsNumber(PdfName.N)?.IntValue() != 4)
                        return null;
                    var converter = CmykToRgb.ForProfile(stream.GetBytes());
                    iccConverters.Add(converter);
                    return new CmykSpace(4, null, converter);
                }

                if (PdfName.Separation.Equals(family) && array.Size() >= 4)
                    return ClassifyTint(array, components: 1, device, iccConverters);

                if (PdfName.DeviceN.Equals(family) && array.Size() >= 4)
                {
                    var colorants = array.GetAsArray(1)?.Size() ?? 0;
                    return colorants == 0 ? null : ClassifyTint(array, colorants, device, iccConverters);
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>Separation e DeviceN definiscono il colore come "tinta" più una
    /// funzione che porta a uno spazio alternativo: se l'alternativo è CMYK,
    /// valutiamo la funzione e traduciamo il risultato.</summary>
    private static CmykSpace? ClassifyTint(PdfArray array, int components, CmykToRgb device,
        List<CmykToRgb> iccConverters)
    {
        var alternate = Classify(array.Get(2), device, iccConverters);
        if (alternate is null)
            return null;

        try
        {
            var tint = PdfFunctionFactory.Create(array.Get(3));
            return tint is null ? null : new CmykSpace(components, tint, alternate.Converter);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Dictionary<string, CmykSpace> MapColorSpaces(PdfResources resources,
        CmykToRgb device, List<CmykToRgb> iccConverters)
    {
        var map = new Dictionary<string, CmykSpace>(StringComparer.Ordinal);
        var declared = resources.GetPdfObject().GetAsDictionary(PdfName.ColorSpace);
        if (declared is null)
            return map;

        foreach (var key in declared.KeySet())
        {
            if (Classify(declared.Get(key), device, iccConverters) is { } space)
                map[key.GetValue()] = space;
        }
        return map;
    }

    /// <summary>Dopo la riscrittura gli spazi CMYK non sono più referenziati:
    /// vanno tolti, altrimenti la verifica li troverebbe ancora.</summary>
    private static void RemoveConvertedColorSpaces(PdfResources resources)
    {
        var declared = resources.GetPdfObject().GetAsDictionary(PdfName.ColorSpace);
        if (declared is null)
            return;

        var toRemove = declared.KeySet()
            .Where(key => IsCmykFamily(declared.Get(key)))
            .ToList();
        foreach (var key in toRemove)
            declared.Remove(key);
        if (declared.Size() == 0)
            resources.GetPdfObject().Remove(PdfName.ColorSpace);
        resources.GetPdfObject().SetModified();
    }

    private static bool IsCmykFamily(PdfObject? colorSpace) => colorSpace switch
    {
        PdfName name => PdfName.DeviceCMYK.Equals(name),
        PdfArray array when array.Size() >= 2 =>
            (PdfName.ICCBased.Equals(array.GetAsName(0)) &&
             array.GetAsStream(1)?.GetAsNumber(PdfName.N)?.IntValue() == 4) ||
            ((PdfName.Separation.Equals(array.GetAsName(0)) || PdfName.DeviceN.Equals(array.GetAsName(0))) &&
             array.Size() >= 3 && IsCmykFamily(array.Get(2))),
        _ => false,
    };

    // ----- Riscrittura dei flussi -----

    private static int RewriteStream(PdfDocument document, PdfPage page, PdfResources resources,
        CmykToRgb device, List<CmykToRgb> iccConverters)
    {
        var content = page.GetPdfObject().Get(PdfName.Contents);
        var bytes = ReadContent(content);
        if (bytes is null)
            return 0;

        var rewriter = new StreamRewriter(MapColorSpaces(resources, device, iccConverters),
            device, iccConverters);
        rewriter.ProcessContent(bytes, resources);
        if (rewriter.Converted == 0)
            return 0;

        var replacement = (PdfStream)new PdfStream(rewriter.Result()).MakeIndirect(document);
        page.GetPdfObject().Put(PdfName.Contents, replacement);
        return rewriter.Converted;
    }

    private static int RewriteForms(PdfDocument document, PdfResources resources, CmykToRgb device,
        List<CmykToRgb> iccConverters, HashSet<PdfStream> rewritten)
    {
        var xobjects = resources.GetPdfObject().GetAsDictionary(PdfName.XObject);
        if (xobjects is null)
            return 0;

        var converted = 0;
        foreach (var key in xobjects.KeySet().ToList())
        {
            var form = xobjects.GetAsStream(key);
            if (form is null || !PdfName.Form.Equals(form.GetAsName(PdfName.Subtype)) || !rewritten.Add(form))
                continue;

            var formResources = form.GetAsDictionary(PdfName.Resources) is { } dictionary
                ? new PdfResources(dictionary)
                : resources;

            converted += ConvertImages(formResources, device, iccConverters);
            converted += RewriteForms(document, formResources, device, iccConverters, rewritten);

            var rewriter = new StreamRewriter(MapColorSpaces(formResources, device, iccConverters),
                device, iccConverters);
            rewriter.ProcessContent(form.GetBytes(), formResources);
            if (rewriter.Converted > 0)
            {
                form.SetData(rewriter.Result());
                converted += rewriter.Converted;
            }
            RemoveConvertedColorSpaces(formResources);
        }
        return converted;
    }

    private static byte[]? ReadContent(PdfObject? contents) => contents switch
    {
        PdfStream stream => stream.GetBytes(),
        PdfArray array => Enumerable.Range(0, array.Size())
            .Select(array.GetAsStream)
            .Where(stream => stream is not null)
            .SelectMany(stream => stream!.GetBytes().Append((byte)'\n'))
            .ToArray(),
        _ => null,
    };

    // ----- Immagini -----

    /// <summary>Converte le immagini CMYK a campioni non compressi in JPEG (DCTDecode
    /// in CMYK non è decodificabile in modo affidabile: resta un ostacolo dichiarato).</summary>
    private static int ConvertImages(PdfResources resources, CmykToRgb device,
        List<CmykToRgb> iccConverters)
    {
        var xobjects = resources.GetPdfObject().GetAsDictionary(PdfName.XObject);
        if (xobjects is null)
            return 0;

        var converted = 0;
        foreach (var key in xobjects.KeySet().ToList())
        {
            var image = xobjects.GetAsStream(key);
            if (image is null || !PdfName.Image.Equals(image.GetAsName(PdfName.Subtype)))
                continue;

            var declared = resources.GetPdfObject().GetAsDictionary(PdfName.ColorSpace);
            var colorSpace = image.Get(PdfName.ColorSpace);
            if (colorSpace is PdfName name && declared?.Get(name) is { } resolved)
                colorSpace = resolved;

            if (Classify(colorSpace, device, iccConverters) is not { } space || space.Components != 4)
                continue;
            if (image.GetAsNumber(PdfName.BitsPerComponent)?.IntValue() != 8)
                continue;
            if (IsJpeg(image))
                continue;

            if (ConvertImageSamples(image, space))
                converted++;
        }
        return converted;
    }

    private static bool IsJpeg(PdfStream image)
    {
        var filter = image.Get(PdfName.Filter);
        return filter switch
        {
            PdfName name => PdfName.DCTDecode.Equals(name) || PdfName.JPXDecode.Equals(name),
            PdfArray array => Enumerable.Range(0, array.Size()).Any(i =>
                PdfName.DCTDecode.Equals(array.GetAsName(i)) || PdfName.JPXDecode.Equals(array.GetAsName(i))),
            _ => false,
        };
    }

    private static bool ConvertImageSamples(PdfStream image, CmykSpace space)
    {
        try
        {
            var samples = image.GetBytes();
            var pixels = samples.Length / 4;
            if (pixels == 0)
                return false;

            var inverted = image.GetAsArray(PdfName.Decode) is { } decode &&
                decode.Size() >= 2 && decode.GetAsNumber(0)?.DoubleValue() == 1;

            var output = new byte[pixels * 3];
            for (var i = 0; i < pixels; i++)
            {
                var c = samples[i * 4] / 255.0;
                var m = samples[i * 4 + 1] / 255.0;
                var y = samples[i * 4 + 2] / 255.0;
                var k = samples[i * 4 + 3] / 255.0;
                if (inverted)
                {
                    c = 1 - c;
                    m = 1 - m;
                    y = 1 - y;
                    k = 1 - k;
                }

                var (r, g, b) = space.ToRgb([c, m, y, k]);
                output[i * 3] = (byte)Math.Round(Math.Clamp(r, 0, 1) * 255);
                output[i * 3 + 1] = (byte)Math.Round(Math.Clamp(g, 0, 1) * 255);
                output[i * 3 + 2] = (byte)Math.Round(Math.Clamp(b, 0, 1) * 255);
            }

            image.Clear();
            image.SetData(output);
            image.Put(PdfName.Type, PdfName.XObject);
            image.Put(PdfName.Subtype, PdfName.Image);
            image.Put(PdfName.ColorSpace, PdfName.DeviceRGB);
            image.Put(PdfName.BitsPerComponent, new PdfNumber(8));
            image.SetModified();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ----- Il riscrittore del flusso -----

    private sealed class StreamRewriter : PdfCanvasProcessor
    {
        private readonly Dictionary<string, CmykSpace> _spaces;
        private readonly CmykToRgb _device;
        private readonly List<CmykToRgb> _iccConverters;
        private readonly MemoryStream _buffer = new();
        private readonly PdfOutputStream _output;
        private readonly Stack<(CmykSpace? Fill, CmykSpace? Stroke)> _saved = new();
        private CmykSpace? _fill;
        private CmykSpace? _stroke;

        public int Converted { get; private set; }

        public StreamRewriter(Dictionary<string, CmykSpace> spaces, CmykToRgb device,
            List<CmykToRgb> iccConverters) : base(new NoOpListener())
        {
            _spaces = spaces;
            _device = device;
            _iccConverters = iccConverters;
            _output = new PdfOutputStream(_buffer);
        }

        public byte[] Result() => _buffer.ToArray();

        protected override void InvokeOperator(PdfLiteral oper, IList<PdfObject> operands)
        {
            var op = oper.ToString();
            switch (op)
            {
                case "q":
                    _saved.Push((_fill, _stroke));
                    break;
                case "Q" when _saved.Count > 0:
                    (_fill, _stroke) = _saved.Pop();
                    break;

                case "k" when TryNumbers(operands, 4, out var fillCmyk):
                    WriteRgb(_device.Convert(fillCmyk[0], fillCmyk[1], fillCmyk[2], fillCmyk[3]), "rg");
                    return;
                case "K" when TryNumbers(operands, 4, out var strokeCmyk):
                    WriteRgb(_device.Convert(strokeCmyk[0], strokeCmyk[1], strokeCmyk[2], strokeCmyk[3]), "RG");
                    return;

                case "cs":
                    _fill = ResolveSpace(operands);
                    if (_fill is not null)
                    {
                        WriteRaw("/DeviceRGB cs\n");
                        Converted++;
                        return;
                    }
                    break;
                case "CS":
                    _stroke = ResolveSpace(operands);
                    if (_stroke is not null)
                    {
                        WriteRaw("/DeviceRGB CS\n");
                        Converted++;
                        return;
                    }
                    break;

                case "sc" or "scn" when _fill is not null &&
                    TryNumbers(operands, _fill.Components, out var fillTint):
                    WriteRgb(_fill.ToRgb(ToDoubles(fillTint)), "sc");
                    return;
                case "SC" or "SCN" when _stroke is not null &&
                    TryNumbers(operands, _stroke.Components, out var strokeTint):
                    WriteRgb(_stroke.ToRgb(ToDoubles(strokeTint)), "SC");
                    return;

                case "g" or "rg":
                    _fill = null;
                    break;
                case "G" or "RG":
                    _stroke = null;
                    break;

                case "Do":
                    // Il contenuto degli XObject viene riscritto a parte: qui non si scende.
                    WriteOperands(operands);
                    return;

                case "EI" when operands.Count > 0 && operands[0] is PdfStream inlineImage:
                    WriteInlineImage(inlineImage);
                    return;
            }

            base.InvokeOperator(oper, operands);
            WriteOperands(operands);
        }

        private CmykSpace? ResolveSpace(IList<PdfObject> operands)
        {
            if (operands.Count < 2 || operands[0] is not PdfName name)
                return null;
            if (_spaces.TryGetValue(name.GetValue(), out var declared))
                return declared;
            // Anche gli spazi "di dispositivo" possono comparire per nome.
            return Classify(name, _device, _iccConverters);
        }

        private static bool TryNumbers(IList<PdfObject> operands, int count, out float[] values)
        {
            values = [];
            if (operands.Count != count + 1)
                return false;
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                if (operands[i] is not PdfNumber number)
                    return false;
                result[i] = number.FloatValue();
            }
            values = result;
            return true;
        }

        private static double[] ToDoubles(float[] values) =>
            [.. values.Select(value => (double)value)];

        private void WriteRgb((float R, float G, float B) rgb, string op)
        {
            WriteRaw(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{rgb.R:0.####} {rgb.G:0.####} {rgb.B:0.####} {op}\n"));
            Converted++;
        }

        private void WriteOperands(IList<PdfObject> operands)
        {
            for (var i = 0; i < operands.Count; i++)
            {
                _output.Write(operands[i]);
                _output.WriteBytes(i == operands.Count - 1 ? NewLine : Space);
            }
        }

        private void WriteRaw(string text) => _output.WriteString(text);

        private void WriteInlineImage(PdfStream image)
        {
            _output.WriteString("BI\n");
            foreach (var key in image.KeySet())
            {
                _output.Write(key);
                _output.WriteBytes(Space);
                _output.Write(image.Get(key));
                _output.WriteBytes(NewLine);
            }
            _output.WriteString("ID\n");
            _output.WriteBytes(image.GetBytes(false));
            _output.WriteString("\nEI\n");
        }

        private static readonly byte[] Space = [(byte)' '];
        private static readonly byte[] NewLine = [(byte)'\n'];
    }

    private sealed class NoOpListener : IEventListener
    {
        public void EventOccurred(IEventData data, EventType type)
        {
        }

        public ICollection<EventType>? GetSupportedEvents() => null;
    }
}
