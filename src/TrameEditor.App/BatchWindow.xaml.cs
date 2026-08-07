using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TrameEditor.App.Services;
using TrameEditor.Core.Pdf;

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

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;
        _files = dialog.FileNames;
        FilesText.Text = $"{_files.Length} file scelti";
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Cartella dove salvare i PDF elaborati" };
        if (dialog.ShowDialog() != true)
            return;
        _outputFolder = dialog.FolderName;
        FolderText.Text = _outputFolder;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var recipe = new BatchRecipe(
            OcrCheck.IsChecked == true,
            RedactCheck.IsChecked == true,
            CompressCheck.IsChecked == true,
            ProtectCheck.IsChecked == true ? ProtectPassword.Password : null);

        if (_files.Length == 0 || _outputFolder is null)
        {
            MessageBox.Show("Scegli i PDF da elaborare e la cartella di output.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
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

        _results.Clear();
        _cancellation = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
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

                var result = await Task.Run(() =>
                {
                    if (!recipe.RunOcr)
                        return BatchProcessor.ProcessFile(file, _outputFolder, recipe);
                    using var renderer = new PdfRenderService(file);
                    return BatchProcessor.ProcessFile(file, _outputFolder, recipe, tessdata,
                        pageNumber => renderer.RenderPagePngForOcr(pageNumber - 1),
                        PdfRenderService.OcrScale);
                });

                if (result.Success)
                    succeeded++;
                else
                    failed++;
                _results.Add($"{(result.Success ? "✓" : "✗")} {Path.GetFileName(file)} — {result.Outcome}");
            }
            ProgressText.Text = $"Fatto: {succeeded} elaborati, {failed} saltati/errori.";
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
