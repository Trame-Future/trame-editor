using System.Runtime.Versioning;
using TrameEditor.Core.Invoices;
using TrameEditor.Core.Markdown;
using Path = System.IO.Path;

namespace TrameEditor.Core.Signatures;

public sealed record SignedExtractionResult(
    string SourcePath,
    IReadOnlyList<string> OutputPaths,
    bool Success,
    string Outcome);

/// <summary>
/// Estrazione in serie dei documenti dalle buste firmate <c>.p7m</c>: si punta
/// una cartella piena di file firmati e se ne ricavano i documenti veri,
/// apribili con qualunque lettore.
/// <para>
/// Quando dentro la busta c'è una <b>fattura elettronica</b> — il caso più
/// frequente — oltre all'XML si produce anche la sua trascrizione in PDF: un
/// XML "estratto" resterebbe illeggibile come prima, e non è quello che serve.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class SignedFileExtractor
{
    /// <summary>I file firmati di una cartella, in ordine alfabetico.</summary>
    public static IReadOnlyList<string> FindSignedFiles(string folder, bool includeSubfolders = false) =>
        [.. Directory.EnumerateFiles(folder, "*.p7m",
                includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

    /// <param name="renderInvoices">Se dentro c'è una fattura elettronica,
    /// produce anche la trascrizione leggibile in PDF.</param>
    public static SignedExtractionResult Extract(string p7mPath, string outputDirectory,
        bool renderInvoices = true)
    {
        P7mContent content;
        try
        {
            content = P7mReader.Read(p7mPath);
        }
        catch (SignatureReadException ex)
        {
            return new SignedExtractionResult(p7mPath, [], false, ex.Message);
        }
        catch (Exception ex)
        {
            return new SignedExtractionResult(p7mPath, [], false, $"errore: {ex.Message}");
        }

        var outputs = new List<string>();
        var notes = new List<string> { DescribeSigners(content) };

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var extracted = UniquePath(outputDirectory, content.SuggestedFileName);
            File.WriteAllBytes(extracted, content.Data);
            outputs.Add(extracted);
            notes.Add($"estratto {Path.GetFileName(extracted)}");

            if (renderInvoices && LooksLikeInvoice(content))
            {
                if (TryRenderInvoice(content, outputDirectory, out var readable, out var problem))
                {
                    outputs.Add(readable!);
                    notes.Add($"fattura tradotta in {Path.GetFileName(readable!)}");
                }
                else
                {
                    notes.Add($"fattura non tradotta ({problem})");
                }
            }
        }
        catch (Exception ex)
        {
            return new SignedExtractionResult(p7mPath, outputs, false, $"errore di scrittura: {ex.Message}");
        }

        return new SignedExtractionResult(p7mPath, outputs, true, string.Join("; ", notes));
    }

    private static string DescribeSigners(P7mContent content)
    {
        if (content.Signers.Count == 0)
            return "nessun firmatario nella busta";

        var descrizioni = content.Signers.Select(signer => signer.IntegrityVerified
            ? $"firmato da {signer.DisplayName}"
            : $"ATTENZIONE {signer.DisplayName}: {signer.Problem ?? "firma non verificabile"}");
        return string.Join(", ", descrizioni);
    }

    private static bool LooksLikeInvoice(P7mContent content)
    {
        if (content.IsPdf || content.Data.Length == 0)
            return false;
        var head = System.Text.Encoding.UTF8.GetString(
            content.Data, 0, Math.Min(content.Data.Length, 4096));
        return head.Contains("FatturaElettronica", StringComparison.Ordinal);
    }

    private static bool TryRenderInvoice(P7mContent content, string outputDirectory,
        out string? readablePath, out string? problem)
    {
        readablePath = null;
        try
        {
            var invoice = FatturaElettronicaReader.Parse(
                System.Text.Encoding.UTF8.GetString(content.Data));
            var baseName = Path.GetFileNameWithoutExtension(content.SuggestedFileName);
            readablePath = UniquePath(outputDirectory, $"{baseName} - leggibile.pdf");
            MarkdownPdfExporter.Export(
                FatturaRenderer.ToMarkdown(invoice, content.SuggestedFileName),
                baseName, readablePath);
            problem = null;
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>Non si sovrascrive mai un file già presente nella cartella scelta.</summary>
    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var counter = 2; ; counter++)
        {
            candidate = Path.Combine(directory, $"{name} ({counter}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
