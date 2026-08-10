using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Conversione di colori CMYK in sRGB, usata dalla conversione in PDF/A: con un
/// profilo di destinazione sRGB i colori CMYK non avrebbero un significato
/// definito, quindi vanno tradotti.
/// <para>
/// Quando è possibile la traduzione passa per il <b>sistema colore di Windows</b>
/// (mscms) con profili ICC veri: sorgente il profilo incorporato nel PDF, se c'è,
/// altrimenti <i>US Web Coated (SWOP)</i> — la stessa assunzione che fanno Acrobat
/// e gli altri strumenti quando il PDF non dichiara da quale CMYK proviene. È
/// un'assunzione, e come tale viene dichiarata all'utente.
/// </para>
/// <para>
/// Se il sistema colore non è disponibile si ripiega sulla formula aritmetica
/// (<c>R = (1-C)(1-K)</c>): sempre meglio che rinunciare, ma il risultato è meno
/// fedele — anche questo viene dichiarato.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CmykToRgb : IDisposable
{
    private readonly nint _transform;
    private readonly Dictionary<(float, float, float, float), (float R, float G, float B)> _cache = [];

    /// <summary>Vero se la conversione usa profili ICC veri, falso se usa la formula.</summary>
    public bool UsesIccProfiles => _transform != 0;

    /// <summary>Descrizione della sorgente assunta, per il rapporto all'utente.</summary>
    public string SourceDescription { get; }

    private CmykToRgb(nint transform, string sourceDescription)
    {
        _transform = transform;
        SourceDescription = sourceDescription;
    }

    /// <summary>Convertitore per i colori DeviceCMYK, che non dichiarano un profilo.</summary>
    public static CmykToRgb ForDeviceCmyk()
    {
        var cmykProfile = FindDefaultCmykProfile();
        var transform = cmykProfile is null ? 0 : TryCreateTransform(File.ReadAllBytes(cmykProfile));
        return new CmykToRgb(transform, transform == 0
            ? "formula aritmetica (profili ICC non disponibili)"
            : $"profilo {Path.GetFileNameWithoutExtension(cmykProfile!)} di Windows");
    }

    /// <summary>Convertitore per un CMYK che porta con sé il proprio profilo ICC.</summary>
    public static CmykToRgb ForProfile(byte[] iccProfile)
    {
        var transform = TryCreateTransform(iccProfile);
        return transform == 0
            ? ForDeviceCmyk()
            : new CmykToRgb(transform, "profilo ICC incorporato nel documento");
    }

    public (float R, float G, float B) Convert(float cyan, float magenta, float yellow, float black)
    {
        var key = (cyan, magenta, yellow, black);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = IsPureBlack(cyan, magenta, yellow)
            ? Neutral(black)
            : _transform == 0
                ? Arithmetic(cyan, magenta, yellow, black)
                : Translate(cyan, magenta, yellow, black);

        // Una pagina usa poche decine di colori distinti: la cache basta piccola.
        if (_cache.Count < 4096)
            _cache[key] = result;
        return result;
    }

    public (float R, float G, float B) Convert(IReadOnlyList<double> cmyk) =>
        Convert((float)cmyk[0], (float)cmyk[1], (float)cmyk[2], (float)cmyk[3]);

    /// <summary>
    /// Il nero e i grigi scritti col solo canale K restano neutri: un profilo di
    /// stampa descrive il nero <i>stampato</i>, che è un grigio molto scuro, e
    /// applicarlo trasformerebbe il testo nero di un documento — LaTeX e mezzo
    /// mondo scrivono il nero proprio come "0 0 0 1 k" — in testo grigio.
    /// In un archivio è una modifica che non si può accettare.
    /// </summary>
    private static bool IsPureBlack(float cyan, float magenta, float yellow) =>
        cyan <= 0.001f && magenta <= 0.001f && yellow <= 0.001f;

    private static (float R, float G, float B) Neutral(float black)
    {
        var level = 1 - Math.Clamp(black, 0, 1);
        return (level, level, level);
    }

    private static (float R, float G, float B) Arithmetic(float c, float m, float y, float k) =>
        ((1 - Math.Clamp(c, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(m, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(y, 0, 1)) * (1 - Math.Clamp(k, 0, 1)));

    private (float R, float G, float B) Translate(float c, float m, float y, float k)
    {
        if (Raw(c, m, y, k) is not { } color)
            return Arithmetic(c, m, y, k);
        return CompensateBlackPoint(color);
    }

    private (float R, float G, float B)? Raw(float c, float m, float y, float k)
    {
        var input = new Color();
        input.Channel0 = ToChannel(c);
        input.Channel1 = ToChannel(m);
        input.Channel2 = ToChannel(y);
        input.Channel3 = ToChannel(k);
        var output = new Color();

        if (!TranslateColors(_transform, ref input, 1, ColorTypeCmyk, ref output, ColorTypeRgb))
            return null;
        return (output.Channel0 / 65535f, output.Channel1 / 65535f, output.Channel2 / 65535f);
    }

    /// <summary>
    /// Il nero di un profilo di stampa non è nero pieno: preso alla lettera, i neri
    /// densi del documento arriverebbero nell'archivio come grigi con una dominante
    /// di colore. Riportiamo il punto di nero del profilo sullo zero e riscaliamo:
    /// è la <i>compensazione del punto di nero</i>, la stessa cosa che fanno gli
    /// strumenti di prestampa.
    /// </summary>
    private (float R, float G, float B) CompensateBlackPoint((float R, float G, float B) color)
    {
        _blackPoint ??= Raw(0, 0, 0, 1) ?? (0, 0, 0);
        var (blackR, blackG, blackB) = _blackPoint.Value;
        return (Rescale(color.R, blackR), Rescale(color.G, blackG), Rescale(color.B, blackB));
    }

    private static float Rescale(float value, float black) =>
        black >= 1 ? value : Math.Clamp((value - black) / (1 - black), 0, 1);

    private (float R, float G, float B)? _blackPoint;

    private static ushort ToChannel(float value) =>
        (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535);

    private static string? FindDefaultCmykProfile()
    {
        var directory = Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color");
        foreach (var name in new[] { "RSWOP.icm", "USWebCoatedSWOP.icc" })
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static nint TryCreateTransform(byte[] cmykProfile)
    {
        nint source = 0, destination = 0;
        try
        {
            var srgb = SrgbColorProfile.FindPath();
            if (srgb is null)
                return 0;

            source = OpenProfile(cmykProfile);
            destination = OpenProfile(File.ReadAllBytes(srgb));
            if (source == 0 || destination == 0)
                return 0;

            var profiles = new[] { source, destination };
            // Colorimetrico relativo: i colori dentro il gamut restano quelli,
            // che è ciò che serve a un archivio.
            var intents = new[] { IntentRelativeColorimetric };
            return CreateMultiProfileTransform(profiles, 2, intents, 1, BestMode, 0);
        }
        catch (Exception)
        {
            return 0;
        }
        finally
        {
            if (source != 0)
                CloseColorProfile(source);
            if (destination != 0)
                CloseColorProfile(destination);
        }
    }

    private static nint OpenProfile(byte[] data)
    {
        var buffer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            var profile = new ProfileHeader
            {
                Type = ProfileMemBuffer,
                Data = buffer,
                Size = (uint)data.Length,
            };
            return OpenColorProfile(ref profile, ProfileRead, FileShareRead, OpenExisting);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_transform != 0)
            DeleteColorTransform(_transform);
    }

    // ----- Sistema colore di Windows (mscms) -----

    private const uint ProfileMemBuffer = 2;
    private const uint ProfileRead = 1;
    private const uint FileShareRead = 1;
    private const uint OpenExisting = 3;
    private const uint IntentRelativeColorimetric = 1;
    private const uint BestMode = 3;
    private const uint ColorTypeRgb = 2;
    private const uint ColorTypeCmyk = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProfileHeader
    {
        public uint Type;
        public nint Data;
        public uint Size;
    }

    /// <summary>Un colore per mscms: otto canali a 16 bit, i primi quattro usati.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Color
    {
        public ushort Channel0;
        public ushort Channel1;
        public ushort Channel2;
        public ushort Channel3;
        public ushort Channel4;
        public ushort Channel5;
        public ushort Channel6;
        public ushort Channel7;
    }

    [DllImport("mscms.dll", EntryPoint = "OpenColorProfileW", SetLastError = true)]
    private static extern nint OpenColorProfile(ref ProfileHeader profile, uint desiredAccess,
        uint shareMode, uint creationMode);

    [DllImport("mscms.dll", SetLastError = true)]
    private static extern bool CloseColorProfile(nint profile);

    [DllImport("mscms.dll", SetLastError = true)]
    private static extern nint CreateMultiProfileTransform(nint[] profiles, uint profileCount,
        uint[] intents, uint intentCount, uint flags, uint preferredCmm);

    [DllImport("mscms.dll", SetLastError = true)]
    private static extern bool DeleteColorTransform(nint transform);

    [DllImport("mscms.dll", SetLastError = true)]
    private static extern bool TranslateColors(nint transform, ref Color input, uint colorCount,
        uint inputType, ref Color output, uint outputType);
}
