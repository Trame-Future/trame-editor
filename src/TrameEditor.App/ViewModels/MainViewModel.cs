using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Session;

namespace TrameEditor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string OpenFilter =
        "Tutti i file supportati (*.txt;*.md;*.pdf)|*.txt;*.md;*.markdown;*.pdf|" +
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|" +
        "PDF (*.pdf)|*.pdf|Tutti i file (*.*)|*.*";
    private const string TextSaveFilter =
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|Tutti i file (*.*)|*.*";

    private readonly SessionStore _sessionStore = SessionStore.CreateDefault();
    private readonly DraftStore _draftStore = DraftStore.CreateDefault();
    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer;

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = [];

    public ObservableCollection<string> RecentFiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private DocumentTabViewModel? _selectedDocument;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    public MainViewModel()
    {
        foreach (var recent in _sessionStore.Load().RecentFiles)
            RecentFiles.Add(recent);
        NewDocument();
        _autosaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _autosaveTimer.Tick += (_, _) => SaveDrafts();
        _autosaveTimer.Start();
    }

    /// <summary>Autosalvataggio: bozze dei documenti di testo con modifiche non salvate.</summary>
    private void SaveDrafts()
    {
        try
        {
            var dirty = Documents.OfType<TextDocumentViewModel>().Where(d => d.IsDirty).ToList();
            foreach (var document in dirty)
            {
                _draftStore.Save(new DocumentDraft
                {
                    Id = document.DraftId,
                    OriginalPath = document.FilePath,
                    DisplayName = document.FileName,
                    Content = document.EditorDocument.Text,
                    SavedAtUtc = DateTime.UtcNow,
                });
            }
            var activeIds = dirty.Select(d => d.DraftId).ToHashSet();
            foreach (var stale in _draftStore.LoadAll().Where(d => !activeIds.Contains(d.Id)))
                _draftStore.Delete(stale.Id);
        }
        catch
        {
            // l'autosalvataggio non deve mai disturbare il lavoro
        }
    }

    /// <summary>All'avvio: se ci sono bozze la sessione precedente è stata interrotta.
    /// Restituisce true se l'utente le ha ripristinate.</summary>
    public bool TryRestoreDrafts()
    {
        var drafts = _draftStore.LoadAll();
        if (drafts.Count == 0)
            return false;

        var answer = MessageBox.Show(
            $"La sessione precedente si è interrotta con {drafts.Count} " +
            (drafts.Count == 1 ? "documento non salvato" : "documenti non salvati") +
            ".\nVuoi ripristinare le bozze?",
            "TrameEditor", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        _draftStore.Clear();
        if (answer != MessageBoxResult.Yes)
            return false;

        foreach (var draft in drafts)
        {
            var document = TextDocumentViewModel.CreateFromDraft(draft);
            if (Documents is [TextDocumentViewModel { IsPristineUntitled: true } blank])
                Documents.Remove(blank);
            Documents.Add(document);
            SelectedDocument = document;
        }
        return true;
    }

    [RelayCommand]
    private void Print()
    {
        switch (SelectedDocument)
        {
            case PdfDocumentViewModel pdf:
                pdf.PrintCommand.Execute(null);
                break;
            case TextDocumentViewModel text:
                text.RequestPrint();
                break;
        }
    }

    /// <summary>Riapre i file dell'ultima sessione (chiamato all'avvio senza argomenti).</summary>
    public void RestoreSession()
    {
        foreach (var path in _sessionStore.Load().OpenFiles.Where(File.Exists))
            OpenPath(path);
    }

    /// <summary>Salva sessione e recenti (chiamato alla chiusura confermata).
    /// La chiusura è pulita: le bozze di autosalvataggio non servono più.</summary>
    public void SaveSession()
    {
        try
        {
            _autosaveTimer.Stop();
            _draftStore.Clear();
            _sessionStore.Save(new SessionState
            {
                OpenFiles = [.. Documents.Select(d => d.FilePath).OfType<string>()],
                RecentFiles = [.. RecentFiles],
            });
        }
        catch
        {
            // la sessione è un comfort: mai bloccare la chiusura per questo
        }
    }

    private void RegisterRecent(string path)
    {
        var updated = SessionStore.PushRecent(RecentFiles, Path.GetFullPath(path));
        RecentFiles.Clear();
        foreach (var item in updated)
            RecentFiles.Add(item);
    }

    [RelayCommand]
    private void OpenRecent(string path)
    {
        if (File.Exists(path))
            OpenPath(path);
        else
        {
            ShowError($"Il file non esiste più:\n{path}");
            RecentFiles.Remove(path);
        }
    }

    public string WindowTitle => SelectedDocument is null
        ? "TrameEditor"
        : $"{SelectedDocument.DisplayName} — TrameEditor";

    partial void OnSelectedDocumentChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentTabViewModel.DisplayName))
            OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void NewDocument()
    {
        var document = TextDocumentViewModel.CreateUntitled();
        Documents.Add(document);
        SelectedDocument = document;
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog { Filter = OpenFilter, Multiselect = true };
        if (dialog.ShowDialog() == true)
            foreach (var path in dialog.FileNames)
                OpenPath(path);
    }

    public void OpenPath(string path)
    {
        var existing = Documents.FirstOrDefault(d => d.RepresentsFile(path));
        if (existing is not null)
        {
            SelectedDocument = existing;
            return;
        }

        try
        {
            DocumentTabViewModel? document;
            if (Path.GetExtension(path).ToLowerInvariant() == ".pdf")
            {
                document = PdfCryptoService.IsPasswordProtected(path)
                    ? OpenProtectedPdf(path)
                    : PdfDocumentViewModel.CreateFromFile(path);
                if (document is null)
                    return; // apertura annullata dall'utente
            }
            else
            {
                document = TextDocumentViewModel.CreateFromFile(path);
            }
            if (Documents is [TextDocumentViewModel { IsPristineUntitled: true } blank])
                Documents.Remove(blank);
            Documents.Add(document);
            SelectedDocument = document;
            RegisterRecent(path);
        }
        catch (Exception ex)
        {
            ShowError($"Impossibile aprire \"{path}\":\n{ex.Message}");
        }
    }

    /// <summary>Chiede la password e apre il PDF su una copia decifrata temporanea.
    /// Null se l'utente annulla.</summary>
    private PdfDocumentViewModel? OpenProtectedPdf(string path)
    {
        while (true)
        {
            var password = PasswordDialog.Ask(Path.GetFileName(path));
            if (password is null)
                return null;

            var workingDirectory = Path.Combine(Path.GetTempPath(), "TrameEditor");
            Directory.CreateDirectory(workingDirectory);
            var workingPath = Path.Combine(workingDirectory, $"{Guid.NewGuid():N}.pdf");
            try
            {
                PdfCryptoService.Decrypt(path, workingPath, password);
                return PdfDocumentViewModel.CreateFromFile(path, workingPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                ShowError("Password errata: riprova.");
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedDocument is not null)
            SaveDocument(SelectedDocument);
    }

    [RelayCommand]
    private void SaveAs()
    {
        if (SelectedDocument is TextDocumentViewModel text)
            SaveTextAs(text);
        else if (SelectedDocument is PdfDocumentViewModel pdf)
            pdf.SaveAs();
    }

    [RelayCommand]
    private void CloseActiveDocument()
    {
        if (SelectedDocument is not null)
            CloseDocument(SelectedDocument);
    }

    [RelayCommand]
    private void CloseDocument(DocumentTabViewModel document)
    {
        if (!ConfirmClose(document))
            return;
        Documents.Remove(document);
        if (document is TextDocumentViewModel text)
            _draftStore.Delete(text.DraftId);
        document.Dispose();
    }

    [RelayCommand]
    private void ExportHtml()
    {
        if (SelectedDocument is not TextDocumentViewModel { IsMarkdown: true } document)
        {
            ShowError("L'export HTML è disponibile solo per i file Markdown (.md).");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Pagina HTML (*.html)|*.html",
            FileName = Path.ChangeExtension(document.FileName, ".html"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName,
                MarkdownRenderService.RenderDocument(document.EditorDocument.Text, document.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Impossibile salvare \"{dialog.FileName}\":\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportPdf()
    {
        if (SelectedDocument is not TextDocumentViewModel { IsMarkdown: true } document)
        {
            ShowError("L'export PDF è disponibile solo per i file Markdown (.md).");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.ChangeExtension(document.FileName, ".pdf"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            MarkdownPdfExporter.Export(document.EditorDocument.Text, document.FileName, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Export PDF non riuscito:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ImagesToPdf()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "Immagini (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = true,
            Title = "Scegli le immagini da unire in un PDF (nell'ordine di selezione)",
        };
        if (openDialog.ShowDialog() != true || openDialog.FileNames.Length == 0)
            return;

        var saveDialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "immagini.pdf" };
        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            ImagesToPdfConverter.Convert(openDialog.FileNames, saveDialog.FileName);
            OpenPath(saveDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Conversione non riuscita:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void MergePdfs()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Scegli i PDF da unire (nell'ordine di selezione)",
        };
        if (openDialog.ShowDialog() != true)
            return;
        if (openDialog.FileNames.Length < 2)
        {
            ShowError("Per unire servono almeno due PDF.");
            return;
        }

        var saveDialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "unito.pdf" };
        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            PdfPageOperations.Merge(openDialog.FileNames, saveDialog.FileName);
            OpenPath(saveDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Unione non riuscita:\n{ex.Message}");
        }
    }

    private bool SaveDocument(DocumentTabViewModel document) => document switch
    {
        TextDocumentViewModel text => text.FilePath is null
            ? SaveTextAs(text)
            : TrySaveText(text, text.FilePath),
        PdfDocumentViewModel pdf => pdf.SaveAs(),
        _ => false,
    };

    private bool SaveTextAs(TextDocumentViewModel document)
    {
        var dialog = new SaveFileDialog
        {
            Filter = TextSaveFilter,
            FileName = document.FileName,
            DefaultExt = ".txt",
        };
        if (dialog.ShowDialog() != true || !TrySaveText(document, dialog.FileName))
            return false;
        RegisterRecent(dialog.FileName);
        return true;
    }

    private static bool TrySaveText(TextDocumentViewModel document, string path)
    {
        try
        {
            document.SaveTo(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Impossibile salvare \"{path}\":\n{ex.Message}");
            return false;
        }
    }

    private bool ConfirmClose(DocumentTabViewModel document)
    {
        if (!document.IsDirty)
            return true;

        SelectedDocument = document;
        var result = MessageBox.Show(
            $"Salvare le modifiche a \"{document.FileName}\"?",
            "TrameEditor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(document),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>Chiamato alla chiusura della finestra: false annulla la chiusura.</summary>
    public bool ConfirmCloseAll()
    {
        foreach (var document in Documents.ToList())
        {
            if (!ConfirmClose(document))
                return false;
        }
        return true;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
}
