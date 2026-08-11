using TrameEditor.Core.Ui;
using Xunit;

namespace TrameEditor.Core.Tests.Ui;

public class RibbonLayoutTests
{
    [Fact]
    public void La_barra_predefinita_usa_solo_comandi_esistenti()
    {
        var sconosciuti = RibbonLayout.Default().CommandIds
            .Where(id => CommandCatalog.Find(id) is null)
            .ToList();

        Assert.Empty(sconosciuti);
    }

    [Fact]
    public void La_barra_predefinita_non_ripete_lo_stesso_pulsante()
    {
        var ripetuti = RibbonLayout.Default().CommandIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(ripetuti);
    }

    [Fact]
    public void Sanitize_butta_i_comandi_sconosciuti()
    {
        var layout = new RibbonLayout
        {
            Tabs =
            {
                new RibbonTab("Home", new RibbonGroup("Prova",
                    new RibbonItem("save"),
                    new RibbonItem("comando-di-una-versione-futura"))),
            },
        };

        var pulita = layout.Sanitize();

        Assert.Equal(new[] { "save" }, pulita.CommandIds);
    }

    [Fact]
    public void Sanitize_butta_riquadri_e_schede_rimasti_vuoti()
    {
        var layout = new RibbonLayout
        {
            Tabs =
            {
                new RibbonTab("Buona", new RibbonGroup("Pieno", new RibbonItem("open"))),
                new RibbonTab("Vuota", new RibbonGroup("Vuoto", new RibbonItem("boh"))),
            },
        };

        var pulita = layout.Sanitize();

        Assert.Single(pulita.Tabs);
        Assert.Equal("Buona", pulita.Tabs[0].Title);
    }

    [Fact]
    public void Una_barra_senza_pulsanti_validi_torna_quella_predefinita()
    {
        var layout = new RibbonLayout
        {
            Tabs = { new RibbonTab("Vuota", new RibbonGroup("Vuoto", new RibbonItem("boh"))) },
        };

        Assert.Equal(RibbonLayout.Default().CommandIds, layout.Sanitize().CommandIds);
    }

    [Fact]
    public void Sanitize_non_tocca_l_originale()
    {
        var layout = new RibbonLayout
        {
            Tabs = { new RibbonTab("Home", new RibbonGroup("Prova", new RibbonItem("save"), new RibbonItem("boh"))) },
        };

        layout.Sanitize();

        Assert.Equal(2, layout.Tabs[0].Groups[0].Items.Count);
    }

    [Fact]
    public void Clone_e_indipendente_dall_originale()
    {
        var layout = RibbonLayout.Default();
        var copia = layout.Clone();

        copia.Tabs[0].Groups[0].Items.Clear();
        copia.Tabs[0].Title = "Cambiata";

        Assert.NotEmpty(layout.Tabs[0].Groups[0].Items);
        Assert.Equal("Home", layout.Tabs[0].Title);
    }
}
