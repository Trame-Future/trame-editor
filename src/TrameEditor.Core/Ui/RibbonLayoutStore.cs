using System.Text.Json;

namespace TrameEditor.Core.Ui;

/// <summary>
/// Dove finisce la barra multifunzione personalizzata:
/// <c>%APPDATA%\TrameEditor\barra-multifunzione.json</c>.
/// </summary>
/// <remarks>
/// Il file <b>non</b> viene creato finché l'utente non personalizza davvero.
/// È voluto: chi non tocca niente riceve la barra della versione che ha
/// installato, comprese le funzioni aggiunte dagli aggiornamenti. Chi
/// personalizza si prende in carico la propria disposizione — e "Ripristina"
/// cancella il file, tornando alla barra predefinita.
/// </remarks>
public static class RibbonLayoutStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrameEditor", "barra-multifunzione.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Vero se l'utente ha una disposizione sua.</summary>
    public static bool IsCustomized(string? path = null) => File.Exists(path ?? DefaultPath);

    /// <summary>
    /// Carica la disposizione salvata, già ripulita. Se il file manca, è
    /// illeggibile o è rimasto senza pulsanti validi, torna quella predefinita.
    /// </summary>
    public static RibbonLayout Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return RibbonLayout.Default();

            var layout = JsonSerializer.Deserialize<RibbonLayout>(File.ReadAllText(path));
            return layout is null ? RibbonLayout.Default() : layout.Sanitize();
        }
        catch
        {
            // un file rotto non deve lasciare l'utente senza barra
            return RibbonLayout.Default();
        }
    }

    public static void Save(RibbonLayout layout, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(layout, Options));
    }

    /// <summary>Torna alla barra predefinita cancellando la personalizzazione.</summary>
    public static RibbonLayout Reset(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // se il file non si lascia cancellare, almeno restituiamo il predefinito
        }

        return RibbonLayout.Default();
    }
}
