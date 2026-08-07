using System.Text;
using TrameEditor.Core.Profile;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Profile;

public class PersonalDataVaultTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-vault-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveAndLoad_Roundtrip_AndFileIsNotPlaintext()
    {
        var vault = new PersonalDataVault(Path.Combine(_dir, "profilo.dat"));
        vault.Save(new Dictionary<string, string>
        {
            [ProfileKeys.Nome] = "Pietro",
            [ProfileKeys.CodiceFiscale] = "RCCPTR80A01H501U",
        });

        var loaded = vault.Load();
        Assert.Equal("Pietro", loaded[ProfileKeys.Nome]);
        Assert.Equal("RCCPTR80A01H501U", loaded[ProfileKeys.CodiceFiscale]);

        // il file su disco è cifrato: i dati non devono comparire in chiaro
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_dir, "profilo.dat")));
        Assert.DoesNotContain("Pietro", raw);
        Assert.DoesNotContain("RCCPTR", raw);
    }

    [Fact]
    public void Load_MissingOrCorruptFile_ReturnsEmpty()
    {
        Assert.Empty(new PersonalDataVault(Path.Combine(_dir, "mai-creato.dat")).Load());

        var corrupt = Path.Combine(_dir, "rotto.dat");
        File.WriteAllBytes(corrupt, [1, 2, 3, 4, 5]);
        Assert.Empty(new PersonalDataVault(corrupt).Load());
    }
}
