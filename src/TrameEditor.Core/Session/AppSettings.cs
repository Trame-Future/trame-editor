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
