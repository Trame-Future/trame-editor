using TrameEditor.Core.Ui;
using Xunit;

namespace TrameEditor.Core.Tests.Ui;

public class CommandCatalogTests
{
    [Fact]
    public void Gli_identificatori_sono_unici()
    {
        var duplicati = CommandCatalog.All
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicati);
    }

    [Fact]
    public void Ogni_comando_sta_in_un_menu_dichiarato()
    {
        foreach (var command in CommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Menu), command.Id);
            Assert.Contains(command.Menu, CommandCatalog.Menus);
        }
    }

    /// <summary>
    /// L'invariante che rende sicura la personalizzazione: qualunque cosa
    /// l'utente faccia alla barra multifunzione, ogni funzione resta
    /// raggiungibile dal menu classico.
    /// </summary>
    [Fact]
    public void Il_menu_classico_copre_tutte_le_funzioni()
    {
        var nelMenu = CommandCatalog.Menus
            .SelectMany(CommandCatalog.OfMenu)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(CommandCatalog.All.Count, nelMenu.Count);
        foreach (var command in CommandCatalog.All)
            Assert.Contains(command.Id, nelMenu);
    }

    [Fact]
    public void Nessun_menu_e_vuoto()
    {
        foreach (var menu in CommandCatalog.Menus)
            Assert.NotEmpty(CommandCatalog.OfMenu(menu));
    }

    [Fact]
    public void Etichette_e_descrizioni_sono_compilate()
    {
        foreach (var command in CommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Label), command.Id);
            Assert.False(string.IsNullOrWhiteSpace(command.MenuLabel), command.Id);
            Assert.False(string.IsNullOrWhiteSpace(command.Description), command.Id);
        }
    }

    [Fact]
    public void Ogni_menu_ha_una_lettera_di_scelta_rapida_e_nessuna_e_ripetuta()
    {
        var lettere = new List<char>();
        foreach (var menu in CommandCatalog.Menus)
        {
            var header = CommandCatalog.MenuHeader(menu);
            var posizione = header.IndexOf('_');
            Assert.True(posizione >= 0 && posizione < header.Length - 1,
                $"il menu {menu} non ha la lettera di scelta rapida");
            Assert.Equal(menu, header.Replace("_", string.Empty));
            lettere.Add(char.ToLowerInvariant(header[posizione + 1]));
        }

        Assert.Equal(lettere.Count, lettere.Distinct().Count());
    }

    [Fact]
    public void Find_trova_per_identificatore_e_torna_null_se_non_esiste()
    {
        Assert.Equal("Salva", CommandCatalog.Find("save")?.Label);
        Assert.Null(CommandCatalog.Find("non-esiste"));
    }
}
