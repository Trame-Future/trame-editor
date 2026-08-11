using System.Windows;

namespace TrameEditor.App;

/// <summary>Chiede una riga di testo (il nome di una scheda o di un riquadro).</summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
    }

    public static string? Ask(Window owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new TextPromptDialog
        {
            Owner = owner,
            Title = title,
        };
        dialog.PromptText.Text = prompt;
        dialog.ValueBox.Text = initialValue;
        dialog.Loaded += (_, _) =>
        {
            dialog.ValueBox.SelectAll();
            dialog.ValueBox.Focus();
        };

        return dialog.ShowDialog() == true ? dialog.ValueBox.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            MessageBox.Show(this, "Serve un nome.", "TrameEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
