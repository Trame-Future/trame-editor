using System.Windows;

namespace TrameEditor.App;

public partial class NoteDialog : Window
{
    public NoteDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NoteText.Focus();
    }

    public static string? Prompt()
    {
        var dialog = new NoteDialog { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NoteText.Text)
            ? dialog.NoteText.Text.Trim()
            : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
