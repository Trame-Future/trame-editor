using System.Windows;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App;

public partial class RedactionDialog : Window
{
    public sealed class RedactionItem
    {
        public required SensitiveMatch Match { get; init; }
        public bool IsSelected { get; set; } = true;
        public string KindName => $"[{Match.Kind.DisplayName()}]";
        public string Value => Match.Value;
        public string PageInfo => $"— pag. {Match.Line.PageNumber}";
    }

    private readonly List<RedactionItem> _items;

    private RedactionDialog(IReadOnlyList<SensitiveMatch> matches)
    {
        InitializeComponent();
        _items = matches.Select(m => new RedactionItem { Match = m }).ToList();
        ItemsList.ItemsSource = _items;
        CountText.Text = $"{_items.Count} dati riconosciuti";
    }

    /// <summary>Null se l'utente annulla.</summary>
    public static (IReadOnlyList<SensitiveMatch> Selected, bool StripMetadata)? Show(
        IReadOnlyList<SensitiveMatch> matches)
    {
        var dialog = new RedactionDialog(matches) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return null;
        return (dialog._items.Where(i => i.IsSelected).Select(i => i.Match).ToList(),
            dialog.StripMetadataCheck.IsChecked == true);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
