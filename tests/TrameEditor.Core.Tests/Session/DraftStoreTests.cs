using TrameEditor.Core.Session;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Session;

public class DraftStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-drafts-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveLoadDeleteClear_Lifecycle()
    {
        var store = new DraftStore(Path.Combine(_dir, "drafts"));
        var draft = new DocumentDraft
        {
            OriginalPath = @"C:\doc\relazione.md",
            DisplayName = "relazione.md",
            Content = "# Bozza\ncontenuto non salvato",
            SavedAtUtc = DateTime.UtcNow,
        };

        store.Save(draft);
        store.Save(draft); // sovrascrittura idempotente
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(draft.Content, loaded[0].Content);
        Assert.Equal(draft.OriginalPath, loaded[0].OriginalPath);

        store.Delete(draft.Id);
        Assert.Empty(store.LoadAll());

        store.Save(draft);
        store.Save(new DocumentDraft { DisplayName = "Nuovo 1", Content = "x", SavedAtUtc = DateTime.UtcNow });
        store.Clear();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void LoadAll_MissingDirectoryOrCorruptFile_IsSafe()
    {
        var store = new DraftStore(Path.Combine(_dir, "mai-creata"));
        Assert.Empty(store.LoadAll());

        var dir = Path.Combine(_dir, "conCorrotto");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "rotto.json"), "{ non json");
        Assert.Empty(new DraftStore(dir).LoadAll());
    }
}
