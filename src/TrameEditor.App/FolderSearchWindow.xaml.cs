using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App;

/// <summary>
/// Cerca una parola dentro tutti i PDF di una cartella. I file in cui non è
/// stato possibile cercare (scansioni senza OCR, file illeggibili) vengono
/// segnalati: un file non esaminato non è un file senza corrispondenze.
/// </summary>
public partial class FolderSearchWindow : Window
{
    private readonly ObservableCollection<FolderSearchHit> _hits = [];
    private string? _folder;
    private bool _searching;

    public FolderSearchWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _hits;
    }

    /// <summary>Chiesto di aprire un risultato: il file e la pagina.</summary>
    public event Action<string, int>? OpenRequested;

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Cartella con i PDF in cui cercare" };
        if (dialog.ShowDialog() != true)
            return;
        _folder = dialog.FolderName;
        FolderText.Text = _folder;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_searching)
            return;
        if (_folder is null || string.IsNullOrWhiteSpace(QueryBox.Text))
        {
            MessageBox.Show("Scegli la cartella e scrivi che cosa cercare.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _searching = true;
        SearchButton.IsEnabled = false;
        _hits.Clear();
        WarningText.Text = string.Empty;
        var query = QueryBox.Text.Trim();
        var folder = _folder;
        var subfolders = SubfoldersCheck.IsChecked == true;
        var progress = new Progress<string>(message => SummaryText.Text = message);

        try
        {
            var report = await Task.Run(() =>
                FolderSearchService.Search(folder, query, subfolders, progress));

            foreach (var hit in report.Hits)
                _hits.Add(hit);

            SummaryText.Text = report.Hits.Count == 0
                ? $"Nessuna corrispondenza in {report.FilesSearched} PDF."
                : $"{report.Hits.Count} corrispondenze in {report.FilesWithHits} " +
                  $"su {report.FilesSearched} PDF. Doppio clic per aprire il documento.";

            var avvisi = new List<string>();
            if (report.FilesWithoutText.Count > 0)
                avvisi.Add($"{report.FilesWithoutText.Count} file non contengono testo " +
                    "(scansioni): in quelli non è stato possibile cercare — passali prima con l'OCR.");
            if (report.FilesNotReadable.Count > 0)
                avvisi.Add($"{report.FilesNotReadable.Count} file non sono leggibili " +
                    "(danneggiati o protetti da password).");
            WarningText.Text = string.Join(" ", avvisi);
        }
        catch (Exception ex)
        {
            SummaryText.Text = "ricerca non riuscita";
            MessageBox.Show($"Ricerca non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _searching = false;
            SearchButton.IsEnabled = true;
        }
    }

    private void Result_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is FolderSearchHit hit)
            OpenRequested?.Invoke(hit.FilePath, hit.PageNumber);
    }
}
