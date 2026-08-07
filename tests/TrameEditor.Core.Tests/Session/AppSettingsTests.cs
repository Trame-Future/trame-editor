using TrameEditor.Core.Ai;
using TrameEditor.Core.Session;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Session;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_CreatesItWithDefaults()
    {
        var path = Path.Combine(_dir, "impostazioni.json");

        var settings = AppSettings.Load(path);

        Assert.Equal(OllamaClient.DefaultBaseUrl, settings.OllamaEndpoint);
        Assert.True(File.Exists(path), "il file va creato così l'utente sa dove configurare");
        Assert.Contains("OllamaEndpoint", File.ReadAllText(path));
    }

    [Fact]
    public void Load_CustomEndpoint_Roundtrip()
    {
        var path = Path.Combine(_dir, "custom.json");
        new AppSettings { OllamaEndpoint = "http://192.168.1.50:11434" }.Save(path);

        Assert.Equal("http://192.168.1.50:11434", AppSettings.Load(path).OllamaEndpoint);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "rotto.json");
        File.WriteAllText(path, "{ non json");

        Assert.Equal(OllamaClient.DefaultBaseUrl, AppSettings.Load(path).OllamaEndpoint);
    }
}
