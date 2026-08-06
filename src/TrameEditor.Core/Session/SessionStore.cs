using System.Text.Json;

namespace TrameEditor.Core.Session;

public sealed class SessionState
{
    public List<string> OpenFiles { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
}

/// <summary>
/// Persistenza della sessione (file aperti all'uscita, file recenti) in un JSON
/// per-utente. Robusto: file mancante o corrotto → sessione vuota.
/// </summary>
public sealed class SessionStore
{
    public const int MaxRecentFiles = 10;

    private readonly string _filePath;

    public SessionStore(string filePath) => _filePath = filePath;

    public static SessionStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrameEditor", "session.json"));

    public SessionState Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new SessionState();
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_filePath))
                ?? new SessionState();
        }
        catch
        {
            return new SessionState();
        }
    }

    public void Save(SessionState state)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);
        var tempPath = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Nuova lista recenti: il file in testa, senza duplicati, al massimo
    /// <see cref="MaxRecentFiles"/> voci.</summary>
    public static List<string> PushRecent(IEnumerable<string> recents, string path)
    {
        var result = new List<string> { path };
        result.AddRange(recents.Where(r =>
            !string.Equals(r, path, StringComparison.OrdinalIgnoreCase)));
        return result.Take(MaxRecentFiles).ToList();
    }
}
