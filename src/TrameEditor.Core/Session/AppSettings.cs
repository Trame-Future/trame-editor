using System.Text.Json;
using TrameEditor.Core.Ai;

namespace TrameEditor.Core.Session;

/// <summary>
/// Impostazioni dell'applicazione, in un JSON per-utente leggibile e
/// modificabile a mano (%APPDATA%\TrameEditor\impostazioni.json).
/// Il file viene creato con i valori predefiniti al primo caricamento,
/// così l'utente sa dove e cosa può configurare.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Endpoint di Ollama per "Chiedi al documento".
    /// Da cambiare solo se Ollama gira su porta o host diversi.</summary>
    public string OllamaEndpoint { get; set; } = OllamaClient.DefaultBaseUrl;

    /// <summary>Percorso di <c>verapdf.bat</c> per la validazione formale PDF/A.
    /// Vuoto finché veraPDF non viene installato: è un componente opzionale.</summary>
    public string VeraPdfPath { get; set; } = string.Empty;

    /// <summary>Se avvisare quando esce una versione nuova. <c>null</c> finché non lo si è
    /// chiesto all'utente: il controllo è l'unica cosa che TrameEditor manda in rete, quindi
    /// non parte da sé prima di un sì esplicito (regola 2 del progetto).</summary>
    public bool? UpdateCheckEnabled { get; set; }

    /// <summary>Quando è stato fatto l'ultimo controllo: si guarda una volta al giorno,
    /// non a ogni avvio.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>L'ultima versione già annunciata, per non ripetere lo stesso avviso a ogni
    /// avvio finché l'utente non aggiorna.</summary>
    public string LastAnnouncedVersion { get; set; } = string.Empty;

    /// <summary>Vero se è ora di guardare: consenso dato e ultimo controllo di ieri o prima.</summary>
    public bool ShouldCheckForUpdates(DateTime utcNow) =>
        UpdateCheckEnabled == true &&
        (LastUpdateCheckUtc is null || utcNow - LastUpdateCheckUtc.Value >= TimeSpan.FromDays(1));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrameEditor", "impostazioni.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
                    ?? new AppSettings();

            var defaults = new AppSettings();
            defaults.Save(path); // il file esiste da subito: l'utente sa dove configurare
            return defaults;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // le impostazioni sono un comfort: mai bloccare l'app
        }
    }
}
