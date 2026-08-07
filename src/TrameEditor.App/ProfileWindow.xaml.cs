using System.Windows;
using TrameEditor.Core.Profile;

namespace TrameEditor.App;

public partial class ProfileWindow : Window
{
    public sealed class ProfileField
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public string Value { get; set; } = string.Empty;
    }

    private readonly PersonalDataVault _vault;
    private readonly List<ProfileField> _fields;

    public ProfileWindow()
    {
        InitializeComponent();
        _vault = PersonalDataVault.CreateDefault();
        var data = _vault.Load();
        _fields = ProfileKeys.Standard
            .Select(entry => new ProfileField
            {
                Key = entry.Key,
                Label = entry.Label,
                Value = data.TryGetValue(entry.Key, out var value) ? value : string.Empty,
            })
            .ToList();
        FieldsList.ItemsSource = _fields;
    }

    /// <summary>Apre l'editor del profilo; true se l'utente ha salvato.</summary>
    public static bool ShowEditor() =>
        new ProfileWindow { Owner = Application.Current.MainWindow }.ShowDialog() == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = _fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                .ToDictionary(f => f.Key, f => f.Value.Trim());
            _vault.Save(data);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Salvataggio non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
