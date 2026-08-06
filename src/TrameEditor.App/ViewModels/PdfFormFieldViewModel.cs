using CommunityToolkit.Mvvm.ComponentModel;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App.ViewModels;

public partial class PdfFormFieldViewModel : ObservableObject
{
    private readonly PdfFormFieldInfo _info;

    public string Name => _info.Name;
    public bool IsText => _info.Kind == PdfFormFieldKind.Text;
    public bool IsCheckbox => _info.Kind == PdfFormFieldKind.Checkbox;
    public bool IsChoice => _info.Kind == PdfFormFieldKind.Choice;
    public IReadOnlyList<string> Options => _info.Options;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isChecked;

    public PdfFormFieldViewModel(PdfFormFieldInfo info)
    {
        _info = info;
        _value = info.Value;
        _isChecked = info.Kind == PdfFormFieldKind.Checkbox &&
                     info.Value == info.CheckedValue &&
                     !string.IsNullOrEmpty(info.Value);
    }

    /// <summary>Il valore da scrivere nel PDF.</summary>
    public string ResultValue => IsCheckbox
        ? (IsChecked ? _info.CheckedValue : "Off")
        : Value;
}
