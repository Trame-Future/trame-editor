using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrameEditor.App.ViewModels;

public abstract partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string? _filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isDirty;

    public virtual string FileName => FilePath is not null ? Path.GetFileName(FilePath) : "Documento";

    public string DisplayName => IsDirty ? $"{FileName} ●" : FileName;

    public bool RepresentsFile(string path) =>
        FilePath is not null &&
        string.Equals(FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    public virtual void Dispose()
    {
    }
}
