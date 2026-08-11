using Microsoft.Win32;
using TrameEditor.Core.Shell;
using Xunit;

namespace TrameEditor.Core.Tests.Shell;

/// <summary>
/// Le voci vengono scritte davvero nel registro, ma sotto una radice di prova
/// (<c>HKCU\Software\TrameEditorTest\…</c>): il menu contestuale vero
/// dell'utente non viene toccato.
/// </summary>
public class ExplorerIntegrationTests : IDisposable
{
    private readonly string _root = @"Software\TrameEditorTest\" + Guid.NewGuid().ToString("N")[..8];
    private const string Exe = @"C:\Program Files\Trame Future\TrameEditor\TrameEditor.exe";

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        }
        catch
        {
            // pulizia opportunistica
        }
    }

    [Fact]
    public void Le_voci_coprono_i_tipi_di_file_che_l_app_sa_aprire()
    {
        var associazioni = ExplorerIntegration.Verbs.Select(v => v.Association).Distinct().ToList();

        Assert.Contains(@"SystemFileAssociations\.pdf", associazioni);
        Assert.Contains(@"SystemFileAssociations\.p7m", associazioni);
        Assert.Contains(@"SystemFileAssociations\.xml", associazioni);
        Assert.Contains("Directory", associazioni);
    }

    [Fact]
    public void Ogni_voce_ha_una_chiave_diversa_nella_sua_associazione()
    {
        var doppie = ExplorerIntegration.Verbs
            .GroupBy(v => (v.Association, v.Name))
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(doppie);
    }

    [Fact]
    public void Il_comando_mette_il_percorso_fra_virgolette()
    {
        var apri = ExplorerIntegration.Verbs.First(v => v.Verb == StartupVerb.Open);
        var pdfa = ExplorerIntegration.Verbs.First(v => v.Verb == StartupVerb.ConvertToPdfA);

        Assert.Equal($"\"{Exe}\" \"%1\"", ExplorerIntegration.CommandLine(apri, Exe));
        Assert.Equal($"\"{Exe}\" --pdfa \"%1\"", ExplorerIntegration.CommandLine(pdfa, Exe));
    }

    [Fact]
    public void Installa_scrive_etichetta_icona_e_comando()
    {
        ExplorerIntegration.Install(Exe, _root);

        var verb = ExplorerIntegration.Verbs.First(v => v.Verb == StartupVerb.ConvertToPdfA);
        using var key = Registry.CurrentUser.OpenSubKey(ExplorerIntegration.KeyPath(verb, _root));
        Assert.NotNull(key);
        Assert.Equal(verb.Label, key!.GetValue(null));
        Assert.Equal($"\"{Exe}\",0", key.GetValue("Icon"));

        using var command = key.OpenSubKey("command");
        Assert.Equal($"\"{Exe}\" --pdfa \"%1\"", command!.GetValue(null));
    }

    [Fact]
    public void Installa_e_disinstalla_lasciano_il_registro_come_prima()
    {
        Assert.False(ExplorerIntegration.IsPresent(_root));

        ExplorerIntegration.Install(Exe, _root);
        Assert.True(ExplorerIntegration.IsPresent(_root));
        Assert.True(ExplorerIntegration.IsInstalled(Exe, _root));

        ExplorerIntegration.Uninstall(_root);
        Assert.False(ExplorerIntegration.IsPresent(_root));
        Assert.False(ExplorerIntegration.IsInstalled(Exe, _root));
    }

    [Fact]
    public void Installare_due_volte_non_da_fastidio()
    {
        ExplorerIntegration.Install(Exe, _root);
        ExplorerIntegration.Install(Exe, _root);

        Assert.True(ExplorerIntegration.IsInstalled(Exe, _root));
    }

    [Fact]
    public void Se_l_app_e_stata_spostata_le_voci_risultano_da_riscrivere()
    {
        ExplorerIntegration.Install(@"C:\vecchio\TrameEditor.exe", _root);

        Assert.True(ExplorerIntegration.IsPresent(_root));
        Assert.False(ExplorerIntegration.IsInstalled(Exe, _root));

        ExplorerIntegration.Install(Exe, _root);
        Assert.True(ExplorerIntegration.IsInstalled(Exe, _root));
    }

    [Fact]
    public void Disinstallare_senza_aver_installato_non_solleva_errori()
    {
        ExplorerIntegration.Uninstall(_root);
        Assert.False(ExplorerIntegration.IsPresent(_root));
    }
}
