using System.Diagnostics;
using System.IO;
using TrameEditor.Core.Ai;

namespace TrameEditor.App.Services;

/// <summary>
/// Installazione guidata dell'AI locale: winget (Ollama) → avvio del servizio →
/// download dei modelli con progresso. Ogni passo riporta cosa sta facendo.
/// </summary>
public static class OllamaInstaller
{
    public const string ChatModel = "qwen2.5:3b";
    public const string EmbeddingModel = "nomic-embed-text";

    public static async Task<bool> IsEndpointUpAsync(string endpoint)
    {
        try
        {
            await new OllamaClient(endpoint).ListModelsAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Esegue l'intera installazione. Restituisce false se un passo
    /// fallisce (il progresso spiega quale e perché).</summary>
    public static async Task<bool> InstallAllAsync(string endpoint, bool includeEmbedding,
        IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (await IsEndpointUpAsync(endpoint))
        {
            progress.Report("Ollama è già attivo.");
        }
        else
        {
            if (TryFindInstalledOllama() is { } installedPath)
            {
                progress.Report("Ollama è installato ma non attivo: lo avvio…");
                StartOllama(installedPath);
            }
            else
            {
                progress.Report("Installo Ollama con winget (può richiedere qualche minuto)…");
                if (!await RunWingetInstallAsync(progress, cancellationToken))
                    return false;
                var path = TryFindInstalledOllama();
                if (path is null)
                {
                    progress.Report("Installazione terminata ma ollama.exe non trovato: riavvia il PC e riprova.");
                    return false;
                }
                progress.Report("Avvio Ollama…");
                StartOllama(path);
            }

            progress.Report("Attendo che Ollama risponda…");
            if (!await WaitForEndpointAsync(endpoint, TimeSpan.FromSeconds(90), cancellationToken))
            {
                progress.Report($"Ollama non risponde su {endpoint}. Prova a riavviare il PC.");
                return false;
            }
        }

        var client = new OllamaClient(endpoint);
        var models = await client.ListModelsAsync(cancellationToken);

        if (!models.Any(m => m.StartsWith(ChatModel, StringComparison.OrdinalIgnoreCase)))
        {
            progress.Report($"Scarico il modello {ChatModel} (~2 GB)…");
            await client.PullModelAsync(ChatModel, progress, cancellationToken);
        }
        else
        {
            progress.Report($"Modello {ChatModel} già presente.");
        }

        if (includeEmbedding &&
            !models.Any(m => m.StartsWith(EmbeddingModel, StringComparison.OrdinalIgnoreCase)))
        {
            progress.Report($"Scarico il modello {EmbeddingModel} (~270 MB)…");
            await client.PullModelAsync(EmbeddingModel, progress, cancellationToken);
        }

        progress.Report("✓ Tutto pronto: l'assistente è utilizzabile.");
        return true;
    }

    private static string? TryFindInstalledOllama()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Ollama", "ollama.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void StartOllama(string ollamaExePath)
    {
        // "ollama app.exe" (l'app di sistema con icona e chat) se c'è, altrimenti il server puro
        var appExe = Path.Combine(Path.GetDirectoryName(ollamaExePath)!, "ollama app.exe");
        var startInfo = File.Exists(appExe)
            ? new ProcessStartInfo(appExe) { UseShellExecute = true }
            : new ProcessStartInfo(ollamaExePath, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        Process.Start(startInfo);
    }

    private static async Task<bool> RunWingetInstallAsync(IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("winget",
                "install -e --id Ollama.Ollama --silent --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                progress.Report("winget non disponibile: installa Ollama manualmente da ollama.com.");
                return false;
            }
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 3 && !trimmed.All(c => c is '-' or '\\' or '|' or '/' or ' ' or '█' or '▒'))
                    progress.Report(trimmed);
            }
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0)
                return true;
            progress.Report($"winget è uscito con codice {process.ExitCode}: " +
                "installa Ollama manualmente da ollama.com e riprova.");
            return false;
        }
        catch (Exception ex)
        {
            progress.Report($"Installazione non riuscita ({ex.Message}): " +
                "installa Ollama manualmente da ollama.com.");
            return false;
        }
    }

    private static async Task<bool> WaitForEndpointAsync(string endpoint, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsEndpointUpAsync(endpoint))
                return true;
            await Task.Delay(2000, cancellationToken);
        }
        return false;
    }
}
