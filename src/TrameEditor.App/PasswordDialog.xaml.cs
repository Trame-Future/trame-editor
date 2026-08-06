using System.Windows;

namespace TrameEditor.App;

public partial class PasswordDialog : Window
{
    private readonly bool _requireConfirmation;

    private PasswordDialog(string prompt, bool requireConfirmation)
    {
        InitializeComponent();
        _requireConfirmation = requireConfirmation;
        PromptText.Text = prompt;
        if (!requireConfirmation)
        {
            ConfirmLabel.Visibility = Visibility.Collapsed;
            ConfirmInput.Visibility = Visibility.Collapsed;
        }
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>Chiede la password di un PDF protetto. Null se annullato.</summary>
    public static string? Ask(string fileName) =>
        Show(new PasswordDialog($"\"{fileName}\" è protetto da password. Inseriscila per aprirlo:", false));

    /// <summary>Chiede una nuova password (con conferma). Null se annullato.</summary>
    public static string? CreateNew() =>
        Show(new PasswordDialog("Scegli la password con cui proteggere il PDF:", true));

    private static string? Show(PasswordDialog dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        return dialog.ShowDialog() == true ? dialog.PasswordInput.Password : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length == 0)
        {
            ShowError("La password non può essere vuota.");
            return;
        }
        if (_requireConfirmation && PasswordInput.Password != ConfirmInput.Password)
        {
            ShowError("Le due password non coincidono.");
            return;
        }
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
