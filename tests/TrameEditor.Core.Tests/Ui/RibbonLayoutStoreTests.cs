using TrameEditor.Core.Ui;
using Xunit;

namespace TrameEditor.Core.Tests.Ui;

public class RibbonLayoutStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "te-ribbon-" + Guid.NewGuid().ToString("N")[..8]);

    private string Percorso => Path.Combine(_folder, "barra-multifunzione.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // pulizia opportunistica
        }
    }

    [Fact]
    public void Senza_file_si_usa_la_barra_predefinita_e_il_file_non_viene_creato()
    {
        var layout = RibbonLayoutStore.Load(Percorso);

        Assert.Equal(RibbonLayout.Default().CommandIds, layout.CommandIds);
        Assert.False(File.Exists(Percorso));
        Assert.False(RibbonLayoutStore.IsCustomized(Percorso));
    }

    [Fact]
    public void Salva_e_ricarica_la_stessa_disposizione()
    {
        var layout = new RibbonLayout
        {
            Tabs =
            {
                new RibbonTab("Mia scheda", new RibbonGroup("Mio riquadro",
                    new RibbonItem("pdfa", large: true),
                    new RibbonItem("batch"))),
            },
        };

        RibbonLayoutStore.Save(layout, Percorso);
        var riletta = RibbonLayoutStore.Load(Percorso);

        Assert.True(RibbonLayoutStore.IsCustomized(Percorso));
        Assert.Single(riletta.Tabs);
        Assert.Equal("Mia scheda", riletta.Tabs[0].Title);
        Assert.Equal("Mio riquadro", riletta.Tabs[0].Groups[0].Title);
        Assert.Equal(new[] { "pdfa", "batch" }, riletta.CommandIds);
        Assert.True(riletta.Tabs[0].Groups[0].Items[0].Large);
        Assert.False(riletta.Tabs[0].Groups[0].Items[1].Large);
    }

    [Fact]
    public void Un_file_rotto_non_lascia_l_utente_senza_barra()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Percorso, "{ questo non è JSON");

        Assert.Equal(RibbonLayout.Default().CommandIds, RibbonLayoutStore.Load(Percorso).CommandIds);
    }

    [Fact]
    public void Ripristina_cancella_la_personalizzazione()
    {
        RibbonLayoutStore.Save(
            new RibbonLayout { Tabs = { new RibbonTab("X", new RibbonGroup("Y", new RibbonItem("save"))) } },
            Percorso);

        var ripristinata = RibbonLayoutStore.Reset(Percorso);

        Assert.False(File.Exists(Percorso));
        Assert.False(RibbonLayoutStore.IsCustomized(Percorso));
        Assert.Equal(RibbonLayout.Default().CommandIds, ripristinata.CommandIds);
        Assert.Equal(RibbonLayout.Default().CommandIds, RibbonLayoutStore.Load(Percorso).CommandIds);
    }

    [Fact]
    public void Una_personalizzazione_con_comandi_scomparsi_viene_ripulita_al_caricamento()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Percorso, """
            {
              "Tabs": [
                { "Title": "Home", "Groups": [
                  { "Title": "Prova", "Items": [
                    { "CommandId": "save", "Large": true },
                    { "CommandId": "funzione-tolta-in-una-versione-futura", "Large": false }
                  ]}
                ]}
              ]
            }
            """);

        Assert.Equal(new[] { "save" }, RibbonLayoutStore.Load(Percorso).CommandIds);
    }
}
