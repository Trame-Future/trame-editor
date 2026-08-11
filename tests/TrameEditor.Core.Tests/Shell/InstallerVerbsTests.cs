using TrameEditor.Core.Shell;
using Xunit;

namespace TrameEditor.Core.Tests.Shell;

/// <summary>
/// L'installer scrive le stesse voci che l'app scrive da Impostazioni. Sono due
/// elenchi in due linguaggi diversi (Inno Setup e C#): senza questo controllo si
/// disallineano alla prima voce aggiunta, e l'utente si ritrova un menu diverso
/// a seconda di come l'ha attivato.
/// </summary>
public class InstallerVerbsTests
{
    private static string InstallerScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "installer", "TrameEditor.iss");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(
            "installer/TrameEditor.iss non trovato a partire da " + AppContext.BaseDirectory);
    }

    /// <summary>In Inno Setup le virgolette dentro una stringa si raddoppiano.</summary>
    private static string InnoQuoted(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    [Fact]
    public void Ogni_voce_del_catalogo_e_prevista_dall_installer()
    {
        var script = InstallerScript();

        foreach (var verb in ExplorerIntegration.Verbs)
        {
            var key = $@"Software\Classes\{verb.Association}\shell\{verb.Name}";
            Assert.True(script.Contains(key, StringComparison.Ordinal),
                $"l'installer non crea la chiave {key}");
            Assert.True(script.Contains(verb.Label, StringComparison.Ordinal),
                $"l'installer non usa l'etichetta \"{verb.Label}\"");

            var command = InnoQuoted(
                ExplorerIntegration.CommandLine(verb, @"{app}\{#MyAppExeName}"));
            Assert.True(script.Contains(command, StringComparison.Ordinal),
                $"l'installer non contiene il comando per {verb.Name}: {command}");
        }
    }

    [Fact]
    public void Ogni_voce_viene_tolta_alla_disinstallazione()
    {
        var script = InstallerScript();

        foreach (var verb in ExplorerIntegration.Verbs)
        {
            var line = $@"Subkey: ""Software\Classes\{verb.Association}\shell\{verb.Name}""; "
                       + "Flags: dontcreatekey uninsdeletekey";
            Assert.True(script.Contains(line, StringComparison.Ordinal),
                $"manca la pulizia alla disinstallazione di {verb.Association}\\{verb.Name}");
        }
    }
}
