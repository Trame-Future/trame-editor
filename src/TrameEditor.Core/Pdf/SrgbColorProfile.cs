using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Profilo colore sRGB usato come <i>output intent</i> del PDF/A: senza di esso
/// i colori "di dispositivo" (DeviceRGB/DeviceGray) non avrebbero un significato
/// definito e il file non sarebbe conforme.
/// <para>
/// Usiamo il profilo sRGB fornito da Windows invece di incorporarne una copia nel
/// programma: è il profilo di riferimento del sistema, e così non ridistribuiamo
/// file di terzi in un progetto AGPL. Se manca lo diciamo, invece di inventarne
/// uno che potrebbe rendere il PDF/A non valido.
/// </para>
/// </summary>
public static class SrgbColorProfile
{
    private const string OutputConditionIdentifier = "sRGB IEC61966-2.1";

    public static string OutputCondition => OutputConditionIdentifier;

    /// <summary>Percorso del profilo sul sistema, o null se non è disponibile.</summary>
    public static string? FindPath()
    {
        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public static bool IsAvailable => FindPath() is not null;

    /// <summary>Byte del profilo ICC da incorporare nel documento.</summary>
    /// <exception cref="PdfAConversionException">Profilo non trovato sul sistema.</exception>
    public static byte[] Load()
    {
        var path = FindPath() ?? throw new PdfAConversionException(
            "Profilo colore sRGB di Windows non trovato: senza di esso non è possibile " +
            "produrre un PDF/A valido. Di norma si trova in " +
            @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm.");
        return File.ReadAllBytes(path);
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var colorDirectories = new[]
        {
            Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "spool", "drivers", "color"),
        };

        // Il nome cambia di poco tra le versioni di Windows.
        string[] names =
        [
            "sRGB Color Space Profile.icm",
            "sRGB_v4_ICC_preference.icc",
            "sRGB.icm",
        ];

        foreach (var directory in colorDirectories)
        {
            foreach (var name in names)
                yield return Path.Combine(directory, name);
        }
    }
}
