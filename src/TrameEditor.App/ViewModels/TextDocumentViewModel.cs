using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using TrameEditor.App.Services;
using TrameEditor.Core.Documents;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.TextFiles;

namespace TrameEditor.App.ViewModels;

public partial class TextDocumentViewModel : DocumentTabViewModel
{
    private static int _untitledCounter;
    private string? _untitledName;
    private bool _suppressDirtyTracking;

    public TextDocument EditorDocument { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatDisplay))]
    private TextFileFormat _format = TextFileFormat.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVisible))]
    private bool _showPreview = true;

    /// <summary>L'assistente AI locale, lo stesso dei PDF: qui cita le righe
    /// invece delle pagine e legge il testo dell'editor, non il file su disco —
    /// così risponde su quello che hai davanti, comprese le modifiche non salvate.</summary>
    public DocumentQaViewModel Qa { get; }

    [ObservableProperty]
    private bool _askMode;

    /// <summary>Chiesta una riga (clic su una citazione): la vista ci porta il cursore.</summary>
    public event EventHandler<int>? LineRequested;

    private TextDocumentViewModel()
    {
        EditorDocument.TextChanged += (_, _) =>
        {
            if (!_suppressDirtyTracking)
                IsDirty = true;
            _qaContentChanged = true;
        };
        PropertyChanged += OnSelfPropertyChanged;

        Qa = new DocumentQaViewModel(() => DocumentTextReader.ReadText(EditorDocument.Text));
        Qa.ReferenceRequested += line => LineRequested?.Invoke(this, line);
    }

    private bool _qaContentChanged;

    partial void OnAskModeChanged(bool value)
    {
        if (!value)
            return;
        // Il testo può essere cambiato da quando l'assistente l'ha letto.
        if (_qaContentChanged)
        {
            Qa.Invalidate(reinitializeNow: false);
            _qaContentChanged = false;
        }
        if (!Qa.Ready && !Qa.Busy)
            _ = Qa.InitializeAsync();
    }

    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilePath))
        {
            OnPropertyChanged(nameof(IsMarkdown));
            OnPropertyChanged(nameof(PreviewVisible));
        }
    }

    public override string FileName => FilePath is not null
        ? Path.GetFileName(FilePath)
        : _untitledName ??= $"Nuovo {++_untitledCounter}";

    public string FormatDisplay => $"{Format.EncodingDisplayName}  ·  {Format.LineEnding.DisplayName()}";

    public bool IsMarkdown =>
        Path.GetExtension(FilePath ?? string.Empty).ToLowerInvariant() is ".md" or ".markdown";

    public bool PreviewVisible => IsMarkdown && ShowPreview;

    /// <summary>Identità stabile della bozza di autosalvataggio di questo documento.</summary>
    public Guid DraftId { get; private set; } = Guid.NewGuid();

    /// <summary>La vista che ospita l'editor esegue la stampa quando richiesta.</summary>
    public event EventHandler? PrintRequested;

    public void RequestPrint() => PrintRequested?.Invoke(this, EventArgs.Empty);

    public static TextDocumentViewModel CreateUntitled() => new();

    /// <summary>Ripristina una bozza di autosalvataggio (sessione interrotta).</summary>
    public static TextDocumentViewModel CreateFromDraft(TrameEditor.Core.Session.DocumentDraft draft)
    {
        var document = new TextDocumentViewModel();
        document._suppressDirtyTracking = true;
        document.EditorDocument.Text = draft.Content;
        document._suppressDirtyTracking = false;
        document.EditorDocument.UndoStack.ClearAll();
        document.DraftId = draft.Id;
        if (draft.OriginalPath is not null)
        {
            document.FilePath = draft.OriginalPath;
            if (File.Exists(draft.OriginalPath))
                document.Format = TextFileService.Load(draft.OriginalPath).Format;
        }
        document.IsDirty = true;
        return document;
    }

    public static TextDocumentViewModel CreateFromFile(string path)
    {
        var loaded = TextFileService.Load(path);
        var document = new TextDocumentViewModel();
        document._suppressDirtyTracking = true;
        document.EditorDocument.Text = loaded.Content;
        document._suppressDirtyTracking = false;
        document.EditorDocument.UndoStack.ClearAll();
        document.Format = loaded.Format;
        document.FilePath = Path.GetFullPath(path);
        return document;
    }

    public void SaveTo(string path)
    {
        TextFileService.Save(path, EditorDocument.Text, Format);
        FilePath = Path.GetFullPath(path);
        IsDirty = false;
    }

    public bool IsPristineUntitled =>
        FilePath is null && !IsDirty && EditorDocument.TextLength == 0;

    // ----- Esportazione in PDF e PDF/A -----

    /// <summary>Il documento com'è adesso. Il testo di AvalonEdit si può leggere
    /// <b>solo dal thread della UI</b>: chi lavora in background riceve questa
    /// copia, non il documento vivo.</summary>
    private (string Text, bool IsMarkdown, string Title) Snapshot() =>
        (EditorDocument.Text, IsMarkdown, Path.GetFileNameWithoutExtension(FileName));

    /// <summary>
    /// Scrive il documento in PDF con la resa giusta per il tipo: il Markdown
    /// impaginato come nell'anteprima, un .txt come testo semplice a spaziatura
    /// fissa (un asterisco resta un asterisco).
    /// </summary>
    public void WritePdf(string targetPath)
    {
        var snapshot = Snapshot();
        WritePdf(snapshot, targetPath);
    }

    private static void WritePdf((string Text, bool IsMarkdown, string Title) snapshot, string targetPath)
    {
        if (snapshot.IsMarkdown)
            MarkdownPdfExporter.Export(snapshot.Text, snapshot.Title, targetPath);
        else
            MarkdownPdfExporter.ExportPlainText(snapshot.Text, snapshot.Title, targetPath);
    }

    private static string NewTemporaryPdfPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrameEditor");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
    }

    /// <summary>
    /// Salva il documento direttamente in PDF/A, il formato dell'archiviazione a
    /// lungo termine: si passa dal PDF, ma l'utente non deve fare due giri.
    /// </summary>
    [RelayCommand]
    private async Task ConvertToPdfAAsync()
    {
        if (EditorDocument.TextLength == 0)
        {
            MessageBox.Show("Il documento è vuoto: non c'è nulla da archiviare.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var snapshot = Snapshot();
        string staged;
        try
        {
            staged = await Task.Run(() =>
            {
                var path = NewTemporaryPdfPath();
                WritePdf(snapshot, path);
                return path;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Non sono riuscito a preparare il PDF di partenza:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // Il documento appena creato si può anche rasterizzare: serve un
            // renderer, che qui costruiamo sul PDF temporaneo.
            using var renderer = new PdfRenderService(staged);
            var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
            var outcome = await PdfAWorkflow.RunAsync(staged,
                Path.GetFileNameWithoutExtension(FileName) + " - PDFA.pdf",
                Path.GetFileNameWithoutExtension(FileName),
                pageNumber => renderer.RenderPagePngForOcr(pageNumber - 1),
                PdfRenderService.OcrScale, tessdata);

            if (outcome is not null)
                MessageBox.Show(outcome.Message, "TrameEditor", MessageBoxButton.OK, outcome.Icon);
        }
        finally
        {
            try
            {
                File.Delete(staged);
            }
            catch
            {
                // file temporaneo: non vale la pena disturbare l'utente
            }
        }
    }
}
