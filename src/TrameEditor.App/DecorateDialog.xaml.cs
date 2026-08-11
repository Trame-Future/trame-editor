using System.Windows;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App;

/// <summary>Scelta di numeri di pagina, filigrana e intestazioni da applicare al PDF.</summary>
public partial class DecorateDialog : Window
{
    public sealed record Choice(PageNumbering? Numbering, Watermark? Watermark, HeaderFooter? HeaderFooter);

    private DecorateDialog() => InitializeComponent();

    /// <summary>Null se l'utente annulla o non sceglie nulla.</summary>
    public static Choice? Ask()
    {
        var dialog = new DecorateDialog { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Build() : null;
    }

    private Choice Build()
    {
        PageNumbering? numbering = null;
        if (NumberingCheck.IsChecked == true)
        {
            var format = string.IsNullOrWhiteSpace(FormatBox.Text) ? "{n} / {tot}" : FormatBox.Text;
            var startAt = int.TryParse(StartAtBox.Text, out var parsed) ? parsed : 1;
            numbering = new PageNumbering(format, (PageNumberPosition)PositionBox.SelectedIndex,
                startAt, SkipFirstCheck.IsChecked == true);
        }

        var watermark = WatermarkCheck.IsChecked == true && !string.IsNullOrWhiteSpace(WatermarkBox.Text)
            ? new Watermark(WatermarkBox.Text.Trim())
            : null;

        HeaderFooter? headerFooter = null;
        if (HeaderFooterCheck.IsChecked == true)
        {
            var header = string.IsNullOrWhiteSpace(HeaderBox.Text) ? null : HeaderBox.Text.Trim();
            var footer = string.IsNullOrWhiteSpace(FooterBox.Text) ? null : FooterBox.Text.Trim();
            if (header is not null || footer is not null)
                headerFooter = new HeaderFooter(header, footer);
        }

        return new Choice(numbering, watermark, headerFooter);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var choice = Build();
        if (choice is { Numbering: null, Watermark: null, HeaderFooter: null })
        {
            MessageBox.Show("Scegli almeno una cosa da aggiungere.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
