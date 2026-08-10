using System.Diagnostics;
using System.Xml.Linq;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>Esito della validazione formale, quella che noi non sappiamo dare.</summary>
public sealed record VeraPdfReport(
    bool IsCompliant,
    string Flavour,
    IReadOnlyList<string> Failures,
    string? Error = null)
{
    public bool DidRun => Error is null;
}

/// <summary>
/// Validazione formale PDF/A con <b>veraPDF</b>, il validatore libero di
/// riferimento (usato anche dagli archivi nazionali).
/// <para>
/// veraPDF è scritto in Java e pesa una trentina di megabyte: non lo
/// incorporiamo nel programma. Chi ne ha bisogno — tipicamente chi deve
/// depositare a norma — lo installa dalle impostazioni, esattamente come si fa
/// con il modello di AI locale. Senza veraPDF tutto il resto continua a
/// funzionare: cambia solo che la verifica resta quella interna.
/// </para>
/// </summary>
public static class VeraPdfValidator
{
    /// <summary>Percorso dell'eseguibile veraPDF: quello configurato se valido,
    /// altrimenti cercato dove l'installatore lo mette di solito.</summary>
    public static string? FindExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        foreach (var directory in DefaultInstallDirectories())
        {
            var candidate = Path.Combine(directory, "verapdf.bat");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>Dove installiamo veraPDF quando lo fa il programma, e dove lo
    /// cerchiamo se l'ha installato l'utente.</summary>
    public static IEnumerable<string> DefaultInstallDirectories()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrameEditor", "verapdf");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "veraPDF");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "verapdf");
    }

    public static bool IsAvailable(string? configuredPath = null) =>
        FindExecutable(configuredPath) is not null;

    /// <summary>
    /// Valida il file e restituisce il verdetto formale. Non lancia eccezioni:
    /// se veraPDF non parte o non risponde, l'esito lo dice — un errore dello
    /// strumento di verifica non deve mai passare per un file non conforme.
    /// </summary>
    public static VeraPdfReport Validate(string veraPdfPath, string pdfPath, PdfALevel level,
        TimeSpan? timeout = null)
    {
        var flavour = level == PdfALevel.A2u ? "2u" : "2b";
        try
        {
            var startInfo = new ProcessStartInfo(veraPdfPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(veraPdfPath)) ?? ".",
            };
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("xml");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(flavour);
            startInfo.ArgumentList.Add(Path.GetFullPath(pdfPath));

            using var process = Process.Start(startInfo);
            if (process is null)
                return Failed(flavour, "veraPDF non è stato avviato.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // il processo è già finito
                }
                return Failed(flavour, "veraPDF non ha risposto entro il tempo previsto.");
            }

            return Parse(output, flavour) ?? Failed(flavour,
                string.IsNullOrWhiteSpace(error)
                    ? "veraPDF non ha prodotto un rapporto leggibile."
                    : $"veraPDF ha segnalato: {error.Trim()}");
        }
        catch (Exception ex)
        {
            return Failed(flavour, $"veraPDF non eseguibile ({ex.Message}).");
        }
    }

    private static VeraPdfReport Failed(string flavour, string error) =>
        new(false, flavour, [], error);

    /// <summary>
    /// Legge il rapporto XML di veraPDF. La lettura è volutamente tollerante
    /// (cerca gli attributi ovunque nell'albero) perché la forma del rapporto è
    /// cambiata fra le versioni dello strumento.
    /// </summary>
    public static VeraPdfReport? Parse(string xml, string flavour)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return null;
        }

        var compliantAttribute = document.Descendants()
            .Select(element => element.Attribute("isCompliant"))
            .FirstOrDefault(attribute => attribute is not null);
        if (compliantAttribute is null)
            return null;

        var isCompliant = string.Equals(compliantAttribute.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        var declaredFlavour = document.Descendants()
            .Select(element => element.Attribute("flavour")?.Value)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? flavour;

        var failures = document.Descendants()
            .Where(element => element.Name.LocalName == "rule" &&
                string.Equals(element.Attribute("status")?.Value, "failed",
                    StringComparison.OrdinalIgnoreCase))
            .Select(DescribeRule)
            .Where(description => description.Length > 0)
            .Distinct()
            .ToList();

        return new VeraPdfReport(isCompliant, declaredFlavour, failures);
    }

    private static string DescribeRule(XElement rule)
    {
        var clause = rule.Attribute("clause")?.Value;
        var test = rule.Attribute("testNumber")?.Value;
        var description = rule.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "description")?.Value
            ?? rule.Attribute("description")?.Value;
        var failed = rule.Attribute("failedChecks")?.Value;

        var reference = clause is null ? string.Empty : $"[{clause}{(test is null ? "" : $"-{test}")}] ";
        var count = failed is null or "0" ? string.Empty : $" ({failed} occorrenze)";
        return string.IsNullOrWhiteSpace(description)
            ? (reference.Length > 0 ? $"{reference.Trim()}{count}" : string.Empty)
            : $"{reference}{description.Trim()}{count}";
    }
}
