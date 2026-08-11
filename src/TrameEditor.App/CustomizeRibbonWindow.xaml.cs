using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrameEditor.Core.Ui;

namespace TrameEditor.App;

public enum LayoutNodeKind
{
    Tab,
    Group,
    Item,
}

/// <summary>Una riga dell'albero della barra multifunzione: scheda, riquadro o pulsante.</summary>
public sealed class LayoutNode : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _large;

    public LayoutNodeKind Kind { get; init; }

    public string? CommandId { get; init; }

    public LayoutNode? Parent { get; set; }

    public ObservableCollection<LayoutNode> Children { get; } = new();

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Large
    {
        get => _large;
        set
        {
            if (Set(ref _large, value))
                Notify(nameof(LargeHint));
        }
    }

    public string Symbol => Kind switch
    {
        LayoutNodeKind.Tab => "▣",
        LayoutNodeKind.Group => "▤",
        _ => "•",
    };

    public Visibility LargeHint =>
        Kind == LayoutNodeKind.Item && Large ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Il nome che leggono le tecnologie assistive (e i test).</summary>
    public override string ToString() => Title;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Una funzione nell'elenco di sinistra.</summary>
public sealed class AvailableCommand : INotifyPropertyChanged
{
    private bool _inRibbon;

    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Menu { get; init; }
    public required string Description { get; init; }

    public bool InRibbon
    {
        get => _inRibbon;
        set
        {
            if (_inRibbon == value)
                return;
            _inRibbon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InRibbon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InRibbonHint)));
        }
    }

    public string InRibbonHint => InRibbon ? "· già nella barra" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Il nome che leggono le tecnologie assistive (e i test).</summary>
    public override string ToString() => Label;
}

public partial class CustomizeRibbonWindow : Window
{
    private readonly ObservableCollection<LayoutNode> _roots = new();
    private readonly List<AvailableCommand> _available = CommandCatalog.All
        .Select(c => new AvailableCommand
        {
            Id = c.Id,
            Label = c.Label,
            Menu = c.Menu,
            Description = c.Description,
        })
        .ToList();

    private CustomizeRibbonWindow()
    {
        InitializeComponent();

        var view = new CollectionViewSource { Source = _available };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AvailableCommand.Menu)));
        AvailableList.ItemsSource = view.View;

        LayoutTree.ItemsSource = _roots;
    }

    /// <summary>La disposizione scelta, oppure <c>null</c> se l'utente ha annullato.</summary>
    public RibbonLayout? Result { get; private set; }

    public static RibbonLayout? Show(Window owner, RibbonLayout current)
    {
        var window = new CustomizeRibbonWindow { Owner = owner };
        window.Load(current);
        return window.ShowDialog() == true ? window.Result : null;
    }

    // ── Albero ↔ modello ────────────────────────────────────────────────────

    private void Load(RibbonLayout layout)
    {
        _roots.Clear();
        foreach (var tab in layout.Tabs)
        {
            var tabNode = new LayoutNode { Kind = LayoutNodeKind.Tab, Title = tab.Title };
            foreach (var group in tab.Groups)
            {
                var groupNode = new LayoutNode
                {
                    Kind = LayoutNodeKind.Group,
                    Title = group.Title,
                    Parent = tabNode,
                };

                foreach (var item in group.Items)
                {
                    var command = CommandCatalog.Find(item.CommandId);
                    if (command is null)
                        continue;

                    groupNode.Children.Add(new LayoutNode
                    {
                        Kind = LayoutNodeKind.Item,
                        Title = command.Label,
                        CommandId = command.Id,
                        Large = item.Large,
                        Parent = groupNode,
                    });
                }

                tabNode.Children.Add(groupNode);
            }

            _roots.Add(tabNode);
        }

        RefreshAvailability();
    }

    private RibbonLayout BuildLayout()
    {
        var layout = new RibbonLayout();
        foreach (var tabNode in _roots)
        {
            var tab = new RibbonTab { Title = tabNode.Title };
            foreach (var groupNode in tabNode.Children)
            {
                var group = new RibbonGroup { Title = groupNode.Title };
                foreach (var itemNode in groupNode.Children)
                    group.Items.Add(new RibbonItem(itemNode.CommandId!, itemNode.Large));
                tab.Groups.Add(group);
            }

            layout.Tabs.Add(tab);
        }

        return layout;
    }

    private void RefreshAvailability()
    {
        var used = _roots
            .SelectMany(t => t.Children)
            .SelectMany(g => g.Children)
            .Select(i => i.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var command in _available)
            command.InRibbon = used.Contains(command.Id);
    }

    private LayoutNode? Selected => LayoutTree.SelectedItem as LayoutNode;

    private ObservableCollection<LayoutNode> SiblingsOf(LayoutNode node) =>
        node.Parent?.Children ?? _roots;

    // ── Comandi della finestra ──────────────────────────────────────────────

    private void LayoutTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var node = Selected;
        LargeCheck.IsEnabled = node?.Kind == LayoutNodeKind.Item;
        LargeCheck.IsChecked = node?.Kind == LayoutNodeKind.Item && node.Large;
    }

    private void Available_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Add_Click(sender, e);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not AvailableCommand command)
        {
            Inform("Scegli prima una funzione nell'elenco di sinistra.");
            return;
        }

        var target = TargetGroup();
        if (target is null)
        {
            Inform("Crea prima una scheda e un riquadro dove mettere il pulsante.");
            return;
        }

        var node = new LayoutNode
        {
            Kind = LayoutNodeKind.Item,
            Title = command.Label,
            CommandId = command.Id,
            Parent = target,
        };

        var selected = Selected;
        if (selected?.Kind == LayoutNodeKind.Item && ReferenceEquals(selected.Parent, target))
            target.Children.Insert(target.Children.IndexOf(selected) + 1, node);
        else
            target.Children.Add(node);

        RefreshAvailability();
        Select(node);
    }

    /// <summary>Il riquadro in cui aggiungere, dedotto dalla selezione.</summary>
    private LayoutNode? TargetGroup()
    {
        var node = Selected;
        return node?.Kind switch
        {
            LayoutNodeKind.Group => node,
            LayoutNodeKind.Item => node.Parent,
            LayoutNodeKind.Tab => node.Children.LastOrDefault(),
            _ => _roots.LastOrDefault()?.Children.LastOrDefault(),
        };
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node is null)
        {
            Inform("Scegli prima qualcosa nella barra a destra.");
            return;
        }

        if (node.Kind == LayoutNodeKind.Tab && _roots.Count == 1)
        {
            Inform("Deve restare almeno una scheda nella barra multifunzione.");
            return;
        }

        SiblingsOf(node).Remove(node);
        RefreshAvailability();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        var node = Selected;
        if (node is null)
            return;

        var siblings = SiblingsOf(node);
        var index = siblings.IndexOf(node);
        var destination = index + delta;
        if (index < 0 || destination < 0 || destination >= siblings.Count)
            return;

        siblings.Move(index, destination);
        Select(node);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var title = TextPromptDialog.Ask(this, "Nuova scheda", "Nome della scheda:", "Nuova scheda");
        if (title is null)
            return;

        var tab = new LayoutNode { Kind = LayoutNodeKind.Tab, Title = title };
        var group = new LayoutNode { Kind = LayoutNodeKind.Group, Title = "Nuovo riquadro", Parent = tab };
        tab.Children.Add(group);
        _roots.Add(tab);
        Select(group);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        var tab = node?.Kind switch
        {
            LayoutNodeKind.Tab => node,
            LayoutNodeKind.Group => node.Parent,
            LayoutNodeKind.Item => node.Parent?.Parent,
            _ => _roots.LastOrDefault(),
        };

        if (tab is null)
        {
            Inform("Crea prima una scheda.");
            return;
        }

        var title = TextPromptDialog.Ask(this, "Nuovo riquadro", "Nome del riquadro:", "Nuovo riquadro");
        if (title is null)
            return;

        var group = new LayoutNode { Kind = LayoutNodeKind.Group, Title = title, Parent = tab };
        tab.Children.Add(group);
        Select(group);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node is null || node.Kind == LayoutNodeKind.Item)
        {
            Inform("Si possono rinominare le schede e i riquadri. "
                   + "Il nome dei pulsanti viene dalla funzione.");
            return;
        }

        var title = TextPromptDialog.Ask(this, "Rinomina",
            node.Kind == LayoutNodeKind.Tab ? "Nome della scheda:" : "Nome del riquadro:", node.Title);
        if (title is not null)
            node.Title = title;
    }

    private void Large_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Kind: LayoutNodeKind.Item } node)
            node.Large = LargeCheck.IsChecked == true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Rimetto la barra multifunzione come era all'installazione?\n\n"
            + "La personalizzazione attuale viene persa. Da quel momento la barra "
            + "torna anche ad aggiornarsi da sola con le funzioni aggiunte dai prossimi aggiornamenti.",
            "Ripristina la barra predefinita", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        Result = RibbonLayoutStore.Reset();
        DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var layout = BuildLayout();
        if (!layout.CommandIds.Any())
        {
            Inform("La barra multifunzione è rimasta senza pulsanti. "
                   + "Aggiungine almeno uno, oppure usa \"Ripristina impostazioni predefinite\".");
            return;
        }

        layout = layout.Sanitize();
        try
        {
            RibbonLayoutStore.Save(layout);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "La barra è stata cambiata, ma non sono riuscito a salvarla: "
                + ex.Message + "\n\nAlla prossima apertura tornerà come prima.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Result = layout;
        DialogResult = true;
    }

    private void Select(LayoutNode node)
    {
        var container = FindContainer(LayoutTree, node);
        if (container is not null)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, LayoutNode node)
    {
        parent.UpdateLayout();
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                continue;
            if (ReferenceEquals(item, node))
                return container;

            container.IsExpanded = true;
            var found = FindContainer(container, node);
            if (found is not null)
                return found;
        }

        return null;
    }

    private void Inform(string message) =>
        MessageBox.Show(this, message, "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
}
