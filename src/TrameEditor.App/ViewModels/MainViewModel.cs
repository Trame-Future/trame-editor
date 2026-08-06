using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string OpenFilter =
        "Tutti i file supportati (*.txt;*.md;*.pdf)|*.txt;*.md;*.markdown;*.pdf|" +
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|" +
        "PDF (*.pdf)|*.pdf|Tutti i file (*.*)|*.*";
    private const string TextSaveFilter =
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|Tutti i file (*.*)|*.*";

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private DocumentTabViewModel? _selectedDocument;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    public MainViewModel() => NewDocument();

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
            DocumentTabViewModel document =
                Path.GetExtension(path).ToLowerInvariant() == ".pdf"
                    ? PdfDocumentViewModel.CreateFromFile(path)
                    : TextDocumentViewModel.CreateFromFile(path);
            if (Documents is [TextDocumentViewModel { IsPristineUntitled: true } blank])
                Documents.Remove(blank);
            Documents.Add(document);
            SelectedDocument = document;
        }
        catch (Exception ex)
        {
            ShowError($"Impossibile aprire \"{path}\":\n{ex.Message}");
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
        return dialog.ShowDialog() == true && TrySaveText(document, dialog.FileName);
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
