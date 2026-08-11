using System.Globalization;
using System.Windows;

namespace TrameEditor.App;

/// <summary>Comprimere al meglio, oppure fino a rientrare in un peso preciso.</summary>
public partial class CompressDialog : Window
{
    private CompressDialog() => InitializeComponent();

    /// <summary>Megabyte da rispettare; 0 significa "comprimi al meglio";
    /// null se l'utente annulla.</summary>
    public static double? Ask()
    {
        var dialog = new CompressDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return null;
        return dialog.TargetChoice.IsChecked == true ? dialog.ParseSize() : 0;
    }

    private double ParseSize() =>
        double.TryParse(SizeBox.Text.Replace(',', '.'), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 10;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TargetChoice.IsChecked == true && ParseSize() <= 0)
        {
            MessageBox.Show("Indica un peso valido in megabyte.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
