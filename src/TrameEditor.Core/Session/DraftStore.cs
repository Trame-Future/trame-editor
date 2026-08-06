using System.Text.Json;

namespace TrameEditor.Core.Session;

/// <summary>Bozza di un documento di testo non salvato (autosalvataggio).</summary>
public sealed class DocumentDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OriginalPath { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; }
}

/// <summary>
/// Bozze di autosalvataggio su disco (un JSON per documento). Dopo una chiusura
/// pulita la cartella viene svuotata: se all'avvio ci sono bozze, la sessione
/// precedente è stata interrotta (crash, blackout) e si può proporre il ripristino.
/// </summary>
public sealed class DraftStore
{
    private readonly string _directory;

    public DraftStore(string directory) => _directory = directory;

    public static DraftStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrameEditor", "drafts"));

    public void Save(DocumentDraft draft)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(draft.Id);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(draft));
        if (File.Exists(path))
            File.Replace(tempPath, path, destinationBackupFileName: null);
        else
            File.Move(tempPath, path);
    }

    public IReadOnlyList<DocumentDraft> LoadAll()
    {
        if (!Directory.Exists(_directory))
            return [];
        var drafts = new List<DocumentDraft>();
        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<DocumentDraft>(File.ReadAllText(file));
                if (draft is not null)
                    drafts.Add(draft);
            }
            catch
            {
                // bozza corrotta: ignorata
            }
        }
        return drafts.OrderBy(d => d.SavedAtUtc).ToList();
    }

    public void Delete(Guid id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    public void Clear()
    {
        if (!Directory.Exists(_directory))
            return;
        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // best effort
            }
        }
    }

    private string PathFor(Guid id) => Path.Combine(_directory, $"{id:N}.json");
}
