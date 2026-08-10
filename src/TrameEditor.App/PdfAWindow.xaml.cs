using System.Windows;
using System.Windows.Media;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App;

/// <summary>
/// Rapporto di conformità PDF/A e scelta del percorso di conversione. Tutto
/// quello che cambieremo nel documento è scritto qui <b>prima</b> di toccarlo.
/// </summary>
public partial class PdfAWindow : Window
{
    public enum Choice
    {
        Fedele,
        Raster,
    }

    private sealed class IssueRow
    {
        public required string Symbol { get; init; }
        public required Brush Accent { get; init; }
        public required string Text { get; init; }
    }

    private PdfAWindow(PdfAAnalysisReport report, bool ocrAvailable)
    {
        InitializeComponent();

        var rows = report.Issues.Select(issue => new IssueRow
        {
            Symbol = issue.Severity switch
            {
                PdfAIssueSeverity.Bloccante => "✖",
                PdfAIssueSeverity.Declassa => "!",
                _ => "✔",
            },
            Accent = issue.Severity switch
            {
                PdfAIssueSeverity.Bloccante => Brushes.Firebrick,
                PdfAIssueSeverity.Declassa => Brushes.DarkOrange,
                _ => Brushes.SeaGreen,
            },
            Text = issue.ToString(),
        }).ToList();

        if (rows.Count == 0)
            rows.Add(new IssueRow
            {
                Symbol = "✔",
                Accent = Brushes.SeaGreen,
                Text = "Nessun ostacolo: il documento si converte senza modifiche al contenuto.",
            });

        IssuesList.ItemsSource = rows;
        CountText.Text = $"{report.PageCount} pagine esaminate";

        if (report.CanConvertFaithfully)
        {
            var level = report.BestLevel == PdfALevel.A2u ? "PDF/A-2u" : "PDF/A-2b";
            var extra = report.BestLevel == PdfALevel.A2u
                ? "il testo resta selezionabile e cercabile."
                : "attenzione: parte del testo non è estraibile in modo affidabile, " +
                  "per questo il livello scende da 2u a 2b.";
            FaithfulDetail.Text = $"— {level}: il documento resta com'è, {extra}";
        }
        else
        {
            FaithfulChoice.IsEnabled = false;
            FaithfulChoice.IsChecked = false;
            RasterChoice.IsChecked = true;
            FaithfulDetail.Text = "— non possibile su questo documento: vedi i punti in rosso qui sopra.";
        }

        RasterDetail.Text = ocrAvailable
            ? "— ogni pagina diventa un'immagine con sotto il testo riconosciuto dall'OCR: " +
              "la conformità è garantita, ma il testo originale è perduto e il file pesa di più."
            : "— ogni pagina diventa un'immagine: la conformità è garantita, ma il testo " +
              "è perduto e il documento non sarà ricercabile (OCR non disponibile).";
    }

    /// <summary>Null se l'utente annulla.</summary>
    public static Choice? Ask(PdfAAnalysisReport report, bool ocrAvailable)
    {
        var dialog = new PdfAWindow(report, ocrAvailable) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return null;
        return dialog.FaithfulChoice.IsChecked == true ? Choice.Fedele : Choice.Raster;
    }

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
