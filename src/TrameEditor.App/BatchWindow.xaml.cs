using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrameEditor.App.Services;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Signatures;

namespace TrameEditor.App;

public partial class BatchWindow : Window
{
    private string[] _files = [];
    private string? _outputFolder;
    private CancellationTokenSource? _cancellation;
    private readonly ObservableCollection<string> _results = [];

    public BatchWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    /// <summary>Vero quando è attiva la scheda "Estrai da file firmati".</summary>
    private bool ExtractMode => ModeTabs?.SelectedIndex == 1;

    private string FileFilter => ExtractMode
        ? "File firmati (*.p7m)|*.p7m|Tutti i file (*.*)|*.*"
        : "PDF (*.pdf)|*.pdf";

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        // I file scelti valgono per l'altra modalità: si riparte pulito.
        _files = [];
        FilesText.Text = "nessun file scelto";
        PickFilesButton.Content = ExtractMode ? "Scegli file .p7m…" : "Scegli PDF…";
    }

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = FileFilter, Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;
        _files = dialog.FileNames;
        FilesText.Text = $"{_files.Length} file scelti";
    }

    private void PickInputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = ExtractMode ? "Cartella con i file firmati" : "Cartella con i PDF da elaborare",
        };
        if (dialog.ShowDialog() != true)
            return;

        _files = ExtractMode
            ? [.. SignedFileExtractor.FindSignedFiles(dialog.FolderName)]
            : [.. Directory.EnumerateFiles(dialog.FolderName, "*.pdf").Order()];

        FilesText.Text = _files.Length == 0
            ? $"nessun file adatto in {Path.GetFileName(dialog.FolderName)}"
            : $"{_files.Length} file da {Path.GetFileName(dialog.FolderName)}";
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Cartella dove salvare i risultati" };
        if (dialog.ShowDialog() != true)
            return;
        _outputFolder = dialog.FolderName;
        FolderText.Text = _outputFolder;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Length == 0 || _outputFolder is null)
        {
            MessageBox.Show("Scegli i file da elaborare e la cartella dei risultati.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ExtractMode)
            await RunAsync(ExtractOne);
        else
            await RunRecipeAsync();
    }

    // ----- Estrazione dai file firmati -----

    private Task<(bool Success, string Outcome)> ExtractOne(string file)
    {
        var renderInvoices = RenderInvoicesCheck.IsChecked == true;
        var output = _outputFolder!;
        return Task.Run(() =>
        {
            var result = SignedFileExtractor.Extract(file, output, renderInvoices);
            return (result.Success, result.Outcome);
        });
    }

    // ----- Ricetta sui PDF -----

    private async Task RunRecipeAsync()
    {
        var recipe = new BatchRecipe(
            OcrCheck.IsChecked == true,
            RedactCheck.IsChecked == true,
            CompressCheck.IsChecked == true,
            ProtectCheck.IsChecked == true ? ProtectPassword.Password : null);

        if (recipe is { RunOcr: false, Redact: false, Compress: false, ProtectPassword: null or "" })
        {
            MessageBox.Show("Scegli almeno un passo della ricetta.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ProtectCheck.IsChecked == true && ProtectPassword.Password.Length == 0)
        {
            MessageBox.Show("Inserisci la password di protezione.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        await RunAsync(file => Task.Run(() =>
        {
            BatchFileResult result;
            if (!recipe.RunOcr)
            {
                result = BatchProcessor.ProcessFile(file, _outputFolder!, recipe);
            }
            else
            {
                using var renderer = new PdfRenderService(file);
                result = BatchProcessor.ProcessFile(file, _outputFolder!, recipe, tessdata,
                    pageNumber => renderer.RenderPagePngForOcr(pageNumber - 1),
                    PdfRenderService.OcrScale);
            }
            return (result.Success, result.Outcome);
        }));
    }

    // ----- Ciclo comune -----

    private async Task RunAsync(Func<string, Task<(bool Success, string Outcome)>> processFile)
    {
        _results.Clear();
        _cancellation = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var succeeded = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < _files.Length; i++)
            {
                if (_cancellation.IsCancellationRequested)
                {
                    _results.Add("⏹ interrotto dall'utente");
                    break;
                }
                var file = _files[i];
                ProgressText.Text = $"{i + 1} di {_files.Length}: {Path.GetFileName(file)}…";

                var (success, outcome) = await processFile(file);
                if (success)
                    succeeded++;
                else
                    failed++;
                _results.Add($"{(success ? "✓" : "✗")} {Path.GetFileName(file)} — {outcome}");
            }
            ProgressText.Text = $"Fatto: {succeeded} riusciti, {failed} saltati/errori.";
        }
        finally
        {
            RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
}
