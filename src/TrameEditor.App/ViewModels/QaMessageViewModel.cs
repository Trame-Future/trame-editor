using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrameEditor.App.ViewModels;

public partial class QaMessageViewModel : ObservableObject
{
    public bool IsUser { get; init; }

    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>Pagine usate come contesto per la risposta (citazioni cliccabili).</summary>
    public ObservableCollection<int> SourcePages { get; } = [];
}
