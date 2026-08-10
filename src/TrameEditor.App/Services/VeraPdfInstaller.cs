using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App.Services;

/// <summary>
/// Installazione su richiesta di <b>veraPDF</b>, il validatore formale PDF/A.
/// Stessa filosofia dell'AI locale: non lo mettiamo nel programma, lo installa
/// chi ne ha bisogno, e ogni passo dice cosa sta facendo.
/// <para>
/// veraPDF gira su Java, che di solito su Windows non c'è: se manca lo
/// installiamo con winget (Eclipse Temurin, gratuito e open source). Poi
/// scarichiamo l'installatore ufficiale e lo eseguiamo in modo non presidiato,
/// scegliendo il solo componente che ci serve.
/// </para>
/// </summary>
public static class VeraPdfInstaller
{
    private const string InstallerUrl = "https://software.verapdf.org/rel/verapdf-installer.zip";

    private static readonly string[] JavaPackages =
        ["EclipseAdoptium.Temurin.21.JRE", "EclipseAdoptium.Temurin.17.JRE"];

    public static string TargetDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrameEditor", "verapdf");

    /// <summary>Java è disponibile su questo computer?</summary>
    public static bool IsJavaAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("java", "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;
            process.WaitForExit(15000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Installa Java (se manca) e veraPDF. Restituisce il percorso di
    /// verapdf.bat, oppure null se un passo non è riuscito: il progresso dice quale.</summary>
    public static async Task<string?> InstallAsync(IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        if (VeraPdfValidator.FindExecutable() is { } already)
        {
            progress.Report("veraPDF è già installato su questo computer.");
            return already;
        }

        if (!IsJavaAvailable())
        {
            progress.Report("veraPDF ha bisogno di Java, che qui non c'è: lo installo con winget…");
            if (!await InstallJavaAsync(progress, cancellationToken))
                return null;
            if (!IsJavaAvailable())
            {
                progress.Report("Java risulta installato ma non ancora raggiungibile: " +
                    "chiudi e riapri TrameEditor (o riavvia il PC) e riprova.");
                return null;
            }
            progress.Report("Java installato.");
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), "TrameEditor", "verapdf-setup");
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var zipPath = Path.Combine(workingDirectory, "verapdf-installer.zip");

            progress.Report("Scarico l'installatore ufficiale di veraPDF (circa 33 MB)…");
            if (!await DownloadAsync(zipPath, progress, cancellationToken))
                return null;

            progress.Report("Estraggo l'installatore…");
            var extracted = Path.Combine(workingDirectory, "estratto");
            if (Directory.Exists(extracted))
                Directory.Delete(extracted, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, extracted);

            var installerJar = Directory
                .EnumerateFiles(extracted, "verapdf-izpack-installer-*.jar", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (installerJar is null)
            {
                progress.Report("L'installatore scaricato non ha il formato atteso: " +
                    "installa veraPDF a mano da verapdf.org e indica qui il percorso.");
                return null;
            }

            progress.Report("Installo veraPDF…");
            var answers = Path.Combine(workingDirectory, "auto-install.xml");
            await File.WriteAllTextAsync(answers, BuildAutomatedInstallation(TargetDirectory),
                cancellationToken);

            if (!await RunAsync("java", ["-jar", installerJar, answers], progress, cancellationToken))
                return null;

            var executable = FindExecutableUnder(TargetDirectory)
                ?? VeraPdfValidator.FindExecutable();
            if (executable is null)
            {
                progress.Report("Installazione terminata ma verapdf.bat non è stato trovato: " +
                    "indica qui il percorso a mano.");
                return null;
            }

            progress.Report($"✓ veraPDF pronto: {executable}");
            return executable;
        }
        catch (OperationCanceledException)
        {
            progress.Report("Installazione annullata.");
            return null;
        }
        catch (Exception ex)
        {
            progress.Report($"Installazione non riuscita ({ex.Message}): " +
                "puoi installare veraPDF a mano da verapdf.org e indicare qui il percorso.");
            return null;
        }
        finally
        {
            TryDelete(workingDirectory);
        }
    }

    private static string? FindExecutableUnder(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "verapdf.bat", SearchOption.AllDirectories)
                .FirstOrDefault()
            : null;

    /// <summary>
    /// Le risposte per l'installazione non presidiata dell'installatore IzPack di
    /// veraPDF: installiamo solo la riga di comando, che è ciò che usiamo.
    /// I nomi dei pacchetti sono quelli dichiarati dall'installatore ufficiale.
    /// </summary>
    private static string BuildAutomatedInstallation(string installPath) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <AutomatedInstallation langpack="eng">
          <com.izforge.izpack.panels.htmlhello.HTMLHelloPanel />
          <com.izforge.izpack.panels.target.TargetPanel>
            <installpath>{System.Security.SecurityElement.Escape(installPath)}</installpath>
          </com.izforge.izpack.panels.target.TargetPanel>
          <com.izforge.izpack.panels.packs.PacksPanel>
            <pack index="0" name="veraPDF GUI" selected="true" />
            <pack index="1" name="veraPDF CLI" selected="true" />
            <pack index="2" name="veraPDF Documentation" selected="false" />
            <pack index="3" name="veraPDF Sample Plugins" selected="false" />
          </com.izforge.izpack.panels.packs.PacksPanel>
          <com.izforge.izpack.panels.install.InstallPanel />
          <com.izforge.izpack.panels.finish.FinishPanel />
        </AutomatedInstallation>
        """;

    private static async Task<bool> InstallJavaAsync(IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        foreach (var package in JavaPackages)
        {
            if (await RunAsync("winget",
                    ["install", "-e", "--id", package, "--silent",
                     "--accept-package-agreements", "--accept-source-agreements"],
                    progress, cancellationToken))
                return true;
            progress.Report($"{package} non disponibile, provo un'altra versione…");
        }

        progress.Report("Non sono riuscito a installare Java automaticamente: " +
            "installalo da adoptium.net e riprova.");
        return false;
    }

    private static async Task<bool> DownloadAsync(string targetPath, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            progress.Report($"Download non riuscito ({(int)response.StatusCode}): " +
                "controlla la connessione o scarica veraPDF a mano da verapdf.org.");
            return false;
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(targetPath);

        var buffer = new byte[81920];
        long written = 0;
        var lastReported = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total <= 0)
                continue;
            var percent = (int)(written * 100 / total);
            if (percent >= lastReported + 10)
            {
                lastReported = percent;
                progress.Report($"Download… {percent}%");
            }
        }
        return true;
    }

    private static async Task<bool> RunAsync(string fileName, IEnumerable<string> arguments,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                progress.Report($"{fileName} non disponibile su questo computer.");
                return false;
            }

            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 3 &&
                    !trimmed.All(c => c is '-' or '\\' or '|' or '/' or ' ' or '█' or '▒'))
                    progress.Report(trimmed);
            }
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress.Report($"{fileName}: {ex.Message}");
            return false;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // file temporanei: non vale la pena disturbare l'utente
        }
    }
}
