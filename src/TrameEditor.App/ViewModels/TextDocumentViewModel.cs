using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;
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

    private TextDocumentViewModel()
    {
        EditorDocument.TextChanged += (_, _) =>
        {
            if (!_suppressDirtyTracking)
                IsDirty = true;
        };
        PropertyChanged += OnSelfPropertyChanged;
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

    public static TextDocumentViewModel CreateUntitled() => new();

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
}
