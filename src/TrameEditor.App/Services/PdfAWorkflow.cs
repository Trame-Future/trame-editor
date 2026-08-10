using System.IO;
using System.Windows;
using Microsoft.Win32;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Session;

namespace TrameEditor.App.Services;

/// <summary>
/// Il percorso completo della conversione in PDF/A: esame del documento →
/// rapporto all'utente → scelta del percorso → conversione → validazione formale
/// se veraPDF è installato.
/// <para>
/// Sta qui e non in un ViewModel perché non dipende dal tipo di documento: un PDF
/// aperto e un file di testo esportato in PDF passano esattamente dagli stessi
/// controlli e ricevono le stesse spiegazioni.
/// </para>
/// </summary>
public static class PdfAWorkflow
{
    /// <summary>Esito da mostrare all'utente; null se ha annullato.</summary>
    public sealed record Outcome(string Message, MessageBoxImage Icon);

    /// <param name="sourcePdfPath">PDF già pronto da convertire (copia di lavoro
    /// o export temporaneo): non viene modificato.</param>
    /// <param name="renderPagePng">Render delle pagine per la conversione per
    /// immagine; null se non disponibile.</param>
    public static async Task<Outcome?> RunAsync(string sourcePdfPath, string suggestedName,
        string title, Func<int, byte[]>? renderPagePng, double renderScale, string? tessdataPath)
    {
        if (!SrgbColorProfile.IsAvailable)
        {
            return new Outcome(
                "Il profilo colore sRGB di Windows non è disponibile su questo computer: " +
                "senza di esso non è possibile produrre un PDF/A valido.",
                MessageBoxImage.Warning);
        }

        PdfAAnalysisReport report;
        try
        {
            report = await Task.Run(() => PdfAAnalyzer.Analyze(sourcePdfPath));
        }
        catch (Exception ex)
        {
            return new Outcome($"Analisi del documento non riuscita:\n{ex.Message}", MessageBoxImage.Error);
        }

        var canRasterize = renderPagePng is not null;
        var ocrAvailable = canRasterize && tessdataPath is not null && Directory.Exists(tessdataPath);
        var choice = PdfAWindow.Ask(report, ocrAvailable, canRasterize);
        if (choice is null)
            return null;

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = suggestedName,
        };
        if (dialog.ShowDialog() != true)
            return null;

        var target = dialog.FileName;
        try
        {
            var result = await Task.Run(() => choice == PdfAWindow.Choice.Fedele
                ? PdfAConverter.ConvertFaithfully(sourcePdfPath, target, title)
                : PdfAConverter.ConvertByRasterizing(sourcePdfPath, target, renderPagePng!,
                    renderScale, ocrAvailable ? tessdataPath : null, title));

            // Se l'utente ha installato veraPDF, la parola definitiva è la sua.
            var veraPdfPath = VeraPdfValidator.FindExecutable(AppSettings.Load().VeraPdfPath);
            var validation = veraPdfPath is null
                ? null
                : await Task.Run(() => VeraPdfValidator.Validate(veraPdfPath, target, result.Level));

            return new Outcome(Describe(result, Path.GetFileName(target), validation),
                MessageBoxImage.Information);
        }
        catch (PdfAConversionException ex)
        {
            var suggerimento = canRasterize
                ? "\n\nPuoi riprovare scegliendo la conversione per immagine."
                : string.Empty;
            return new Outcome(ex.Message + suggerimento, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            return new Outcome($"Conversione in PDF/A non riuscita:\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private static string Describe(PdfAConversionResult result, string fileName, VeraPdfReport? validation)
    {
        var level = result.Level == PdfALevel.A2u ? "PDF/A-2u" : "PDF/A-2b";
        var metodo = result.Method == PdfAConversionMethod.Fedele
            ? "conversione fedele"
            : "conversione per immagine";
        var message = $"\"{fileName}\" salvato in {level} ({metodo}).\n\n" +
            "Cosa è cambiato rispetto all'originale:\n" +
            string.Join("\n", result.Changes.Select(c => $"• {c}"));

        if (!result.VerificationClean)
        {
            var residui = result.Verification.Issues
                .Where(i => i.Severity != PdfAIssueSeverity.Corretto)
                .Select(i => $"• {i}");
            message += "\n\nATTENZIONE — la verifica sul file prodotto segnala ancora:\n" +
                string.Join("\n", residui);
        }

        if (validation is null)
        {
            return message + "\n\nLa nostra verifica è interna: per un deposito a norma fai validare " +
                "il file con veraPDF. Puoi installarlo da File → Impostazioni.";
        }

        if (!validation.DidRun)
            return message + $"\n\nValidazione veraPDF non eseguita: {validation.Error}";

        if (validation.IsCompliant)
            return message + "\n\n✓ VALIDAZIONE FORMALE SUPERATA: veraPDF certifica il file come " +
                $"PDF/A-{validation.Flavour}.";

        var motivi = validation.Failures.Take(8).Select(f => $"• {f}");
        return message + $"\n\n✗ VALIDAZIONE FORMALE NON SUPERATA (PDF/A-{validation.Flavour}). " +
            "veraPDF segnala:\n" + string.Join("\n", motivi) +
            (validation.Failures.Count > 8 ? $"\n… e altre {validation.Failures.Count - 8} regole." : "") +
            "\n\nSegnalalo: ogni caso del genere per noi diventa un test.";
    }
}
