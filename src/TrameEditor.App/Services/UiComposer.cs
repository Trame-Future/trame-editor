using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using TrameEditor.Core.Ui;

namespace TrameEditor.App.Services;

/// <summary>
/// Costruisce il <b>menu classico</b> e la <b>barra multifunzione</b> a partire
/// dallo stesso catalogo di comandi: due modi di mostrare le stesse funzioni,
/// mai due elenchi da tenere allineati a mano.
/// </summary>
public sealed class UiComposer
{
    private sealed record Source(string? Path, ICommand? Command);

    private readonly Dictionary<string, Source> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _toggles = new(StringComparer.Ordinal);

    /// <summary>Collega un comando a una proprietà del DataContext
    /// (es. <c>SaveCommand</c>, <c>SelectedDocument.DecorateCommand</c>).</summary>
    public void BindPath(string id, string path) => _sources[id] = new Source(path, null);

    /// <summary>Collega un comando già pronto (comandi WPF come Annulla/Ripeti).</summary>
    public void BindCommand(string id, ICommand command) => _sources[id] = new Source(null, command);

    /// <summary>Collega un comando a un semplice metodo dell'applicazione.</summary>
    public void BindAction(string id, Action action) =>
        _sources[id] = new Source(null, new RelayCommand(action));

    /// <summary>Collega una voce a interruttore a una proprietà booleana
    /// del DataContext (es. <c>WordWrap</c>).</summary>
    public void BindToggle(string id, string path) => _toggles[id] = path;

    /// <summary>I comandi del catalogo rimasti senza collegamento: se ce n'è
    /// uno è un errore di programmazione, non un problema dell'utente.</summary>
    public IReadOnlyList<string> UnboundCommands() =>
        CommandCatalog.All
            .Where(c => c.IsToggle ? !_toggles.ContainsKey(c.Id) : !_sources.ContainsKey(c.Id))
            .Select(c => c.Id)
            .ToList();

    // ── Menu classico ───────────────────────────────────────────────────────

    /// <summary>
    /// Riempie la barra dei menu con tutte le funzioni del catalogo, un menu
    /// per classe logica. <paramref name="decorate"/> permette di aggiungere
    /// voci che non sono comandi (i file recenti, Esci).
    /// </summary>
    public void BuildMenuBar(Menu target, Action<string, ItemCollection>? decorate = null)
    {
        target.Items.Clear();
        foreach (var menu in CommandCatalog.Menus)
        {
            var top = new MenuItem { Header = CommandCatalog.MenuHeader(menu) };
            foreach (var command in CommandCatalog.OfMenu(menu))
            {
                if (command.SeparatorBefore && top.Items.Count > 0)
                    top.Items.Add(new Separator());
                top.Items.Add(CreateMenuItem(command));
            }

            decorate?.Invoke(menu, top.Items);
            target.Items.Add(top);
        }
    }

    private MenuItem CreateMenuItem(UiCommand command)
    {
        var item = new MenuItem
        {
            Header = command.MenuLabel,
            ToolTip = command.Description,
            InputGestureText = command.Shortcut,
        };

        if (command.IsToggle)
        {
            item.IsCheckable = true;
            ApplyToggle(item, MenuItem.IsCheckedProperty, command.Id);
            return item;
        }

        if (command.Glyph.Length > 0)
            item.Icon = CreateGlyph(command, 14);

        ApplyCommand(item, MenuItem.CommandProperty, command.Id);
        return item;
    }

    // ── Barra multifunzione ─────────────────────────────────────────────────

    public void BuildRibbon(Fluent.Ribbon ribbon, RibbonLayout layout)
    {
        var selected = (ribbon.SelectedTabItem as Fluent.RibbonTabItem)?.Header as string;
        ribbon.Tabs.Clear();

        foreach (var tab in layout.Tabs)
        {
            var tabItem = new Fluent.RibbonTabItem { Header = tab.Title };
            foreach (var group in tab.Groups)
            {
                var box = new Fluent.RibbonGroupBox { Header = group.Title };
                foreach (var item in group.Items)
                {
                    var command = CommandCatalog.Find(item.CommandId);
                    if (command is not null)
                        box.Items.Add(CreateRibbonControl(command, item.Large));
                }

                tabItem.Groups.Add(box);
            }

            ribbon.Tabs.Add(tabItem);
        }

        ribbon.SelectedTabItem = ribbon.Tabs.FirstOrDefault(t => (t.Header as string) == selected)
                                 ?? ribbon.Tabs.FirstOrDefault();
    }

    private FrameworkElement CreateRibbonControl(UiCommand command, bool large)
    {
        if (command.IsToggle)
        {
            var check = new Fluent.CheckBox { Header = command.Label, ToolTip = command.Description };
            ApplyToggle(check, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, command.Id);
            return check;
        }

        var button = new Fluent.Button
        {
            Header = command.Label,
            ToolTip = command.Description,
            Focusable = false,
            Size = large ? Fluent.RibbonControlSize.Large : Fluent.RibbonControlSize.Middle,
            // Senza questo il riquadro riporta tutti i pulsanti alla misura del
            // proprio stato, e "grande/piccolo" non avrebbe alcun effetto.
            SizeDefinition = large
                ? new Fluent.RibbonControlSizeDefinition(
                    Fluent.RibbonControlSize.Large,
                    Fluent.RibbonControlSize.Middle,
                    Fluent.RibbonControlSize.Small)
                : new Fluent.RibbonControlSizeDefinition(
                    Fluent.RibbonControlSize.Middle,
                    Fluent.RibbonControlSize.Middle,
                    Fluent.RibbonControlSize.Small),
        };

        var icon = CreateIcon(command, large);
        if (icon is not null)
        {
            if (large)
                button.LargeIcon = icon;
            else
                button.Icon = icon;
        }

        ApplyCommand(button, Fluent.Button.CommandProperty, command.Id);
        return button;
    }

    /// <summary>Il logo di Trame Future per "Informazioni", il glifo per tutti gli altri.</summary>
    private static object? CreateIcon(UiCommand command, bool large)
    {
        if (command.Id == "about")
        {
            var size = large ? 32 : 16;
            return new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icon-256.png")),
                Width = size,
                Height = size,
            };
        }

        return command.Glyph.Length == 0 ? null : CreateGlyph(command, large ? 26 : 14);
    }

    private static FrameworkElement CreateGlyph(UiCommand command, double size)
    {
        var glyph = new TextBlock
        {
            Text = command.Glyph,
            FontSize = command.GlyphFont == GlyphFont.Fluent ? size : size - 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = command.GlyphFont switch
            {
                GlyphFont.Fluent => new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                GlyphFont.Emoji => new FontFamily("Segoe UI Emoji"),
                _ => SystemFonts.MessageFontFamily,
            },
        };

        if (size >= 20 && Application.Current?.TryFindResource("AccentBrush") is Brush accent)
            glyph.Foreground = accent;

        // Una sigla ("OCR") non sta nello spazio di un'icona: la si rimpicciolisce
        // finché ci sta, invece di lasciarla tagliata a metà.
        if (command.GlyphFont == GlyphFont.Text && command.Glyph.Length > 1)
        {
            return new Viewbox
            {
                Child = glyph,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
            };
        }

        return glyph;
    }

    // ── Collegamenti ────────────────────────────────────────────────────────

    private void ApplyCommand(FrameworkElement element, DependencyProperty property, string id)
    {
        if (!_sources.TryGetValue(id, out var source))
        {
            element.IsEnabled = false; // meglio spento che finto funzionante
            return;
        }

        if (source.Command is not null)
        {
            element.SetValue(property, source.Command);
            return;
        }

        if (source.Path is null)
            return;

        BindingOperations.SetBinding(element, property, new Binding(source.Path));

        // Un comando che il documento aperto non ha (ruotare le pagine di un
        // file di testo) lascerebbe la voce accesa e senza effetto: la
        // spegniamo, invece di far cliccare a vuoto.
        BindingOperations.SetBinding(element, UIElement.IsEnabledProperty, new Binding(source.Path)
        {
            Converter = ExistsConverter.Instance,
            FallbackValue = false,
        });
    }

    private sealed class ExistsConverter : IValueConverter
    {
        public static readonly ExistsConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private void ApplyToggle(FrameworkElement element, DependencyProperty property, string id)
    {
        if (!_toggles.TryGetValue(id, out var path))
        {
            element.IsEnabled = false;
            return;
        }

        BindingOperations.SetBinding(element, property, new Binding(path) { Mode = BindingMode.TwoWay });
    }
}
