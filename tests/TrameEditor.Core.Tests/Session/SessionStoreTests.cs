using TrameEditor.Core.Session;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Session;

public class SessionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-session-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SessionStore StoreAt(string name) => new(Path.Combine(_dir, name));

    [Fact]
    public void Load_MissingFile_ReturnsEmptySession()
    {
        var state = StoreAt("mai-salvato.json").Load();
        Assert.Empty(state.OpenFiles);
        Assert.Empty(state.RecentFiles);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip()
    {
        var store = StoreAt("session.json");
        store.Save(new SessionState
        {
            OpenFiles = [@"C:\doc\a.pdf", @"C:\doc\b.md"],
            RecentFiles = [@"C:\doc\a.pdf"],
        });

        var state = store.Load();
        Assert.Equal([@"C:\doc\a.pdf", @"C:\doc\b.md"], state.OpenFiles);
        Assert.Equal([@"C:\doc\a.pdf"], state.RecentFiles);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptySession()
    {
        var path = Path.Combine(_dir, "corrotto.json");
        File.WriteAllText(path, "{ questo non è json ");
        var state = new SessionStore(path).Load();
        Assert.Empty(state.OpenFiles);
    }

    [Fact]
    public void PushRecent_DeduplicatesAndCaps()
    {
        var recents = new List<string>();
        for (var i = 0; i < 15; i++)
            recents = SessionStore.PushRecent(recents, $@"C:\file{i}.txt");

        Assert.Equal(SessionStore.MaxRecentFiles, recents.Count);
        Assert.Equal(@"C:\file14.txt", recents[0]);

        var again = SessionStore.PushRecent(recents, @"C:\FILE10.txt");
        Assert.Equal(SessionStore.MaxRecentFiles, again.Count);
        Assert.Equal(@"C:\FILE10.txt", again[0]);
        Assert.DoesNotContain(again, r => r == @"C:\file10.txt");
    }
}
