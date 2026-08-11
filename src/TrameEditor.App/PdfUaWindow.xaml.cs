using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Session;

namespace TrameEditor.App;

/// <summary>
/// Esame di accessibilità di un PDF: che cosa manca, che cosa possiamo sistemare
/// noi, e — se veraPDF è installato — il verdetto formale PDF/UA.
/// </summary>
public partial class PdfUaWindow : Window
{
    private readonly string _sourcePath;
    private readonly string _displayName;
    private string _lastSavedPath;

    public sealed class IssueRow
    {
        public required string Symbol { get; init; }
        public required Brush Accent { get; init; }
        public required string Text { get; init; }
    }

    private PdfUaWindow(string sourcePath, string displayName, PdfUaReport report)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        // Il documento in esame è una copia di lavoro con un nome tecnico:
        // all'utente si propone il nome del suo file.
        _displayName = Path.GetFileNameWithoutExtension(displayName);
        _lastSavedPath = sourcePath;
        Show(report);
    }

    /// <param name="sourcePath">Il PDF da esaminare (di norma la copia di lavoro).</param>
    /// <param name="displayName">Il nome del documento come lo conosce l'utente.</param>
    public static void ShowFor(string sourcePath, string displayName, PdfUaReport report) =>
        new PdfUaWindow(sourcePath, displayName, report)
        {
            Owner = Application.Current.MainWindow,
        }.ShowDialog();

    private void Show(PdfUaReport report)
    {
        IssuesList.ItemsSource = report.Issues.Select(issue => new IssueRow
        {
            Symbol = issue.Severity switch
            {
                PdfUaSeverity.Bloccante => "✖",
                PdfUaSeverity.Rimediabile => "!",
                _ => "✔",
            },
            Accent = issue.Severity switch
            {
                PdfUaSeverity.Bloccante => Brushes.Firebrick,
                PdfUaSeverity.Rimediabile => Brushes.DarkOrange,
                _ => Brushes.SeaGreen,
            },
            Text = issue.ToString(),
        }).ToList();

        CountText.Text = $"{report.PageCount} pagine esaminate";
        LanguageBox.Text = string.IsNullOrWhiteSpace(report.Language)
            ? PdfUaChecker.DefaultLanguage
            : report.Language;
        TitleBox.Text = string.IsNullOrWhiteSpace(report.Title) ? _displayName : report.Title;
        FixButton.IsEnabled = report.Fixable.Count > 0;

        var veraPdfPresente = VeraPdfValidator.IsAvailable(AppSettings.Load().VeraPdfPath);
        ValidateButton.IsEnabled = veraPdfPresente;
        LegalNote.Text = veraPdfPresente
            ? "Il verdetto formale lo dà veraPDF, che hai installato. Attenzione: verifica le regole "
              + "controllabili da una macchina — un documento che le supera tutte può essere ancora "
              + "poco accessibile, se le marcature ci sono ma sono sbagliate."
            : "Per il verdetto formale PDF/UA serve veraPDF, il validatore libero di riferimento: "
              + "puoi installarlo da Strumenti → Impostazioni. Anche lui però verifica solo le regole "
              + "controllabili da una macchina.";
    }

    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = _displayName + " - accessibile.pdf",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var report = PdfUaChecker.Fix(_sourcePath, dialog.FileName,
                LanguageBox.Text, TitleBox.Text);
            _lastSavedPath = dialog.FileName;
            Show(report);

            var restano = report.Blocking.Count;
            MessageBox.Show(this,
                $"\"{Path.GetFileName(dialog.FileName)}\" salvato con lingua e titolo."
                + (restano == 0
                    ? "\n\nNon restano punti bloccanti fra quelli che sappiamo controllare."
                    : $"\n\nRestano {restano} punti che non possiamo sistemare noi: richiedono di marcare "
                      + "il contenuto, e va fatto nel programma con cui il documento è stato scritto "
                      + "(Word, LibreOffice, InDesign) esportando un PDF accessibile."),
                "Accessibilità", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Non sono riuscito a salvare la copia: " + ex.Message,
                "Accessibilità", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var veraPdfPath = VeraPdfValidator.FindExecutable(AppSettings.Load().VeraPdfPath);
        if (veraPdfPath is null)
            return;

        Cursor = System.Windows.Input.Cursors.Wait;
        ValidateButton.IsEnabled = false;
        try
        {
            var report = VeraPdfValidator.ValidateFlavour(veraPdfPath, _lastSavedPath,
                VeraPdfValidator.AccessibilityFlavour);
            var quale = _lastSavedPath == _sourcePath ? "il documento" : "la copia appena salvata";

            if (!report.DidRun)
            {
                MessageBox.Show(this, $"Validazione non eseguita: {report.Error}",
                    "veraPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (report.IsCompliant)
            {
                MessageBox.Show(this,
                    $"✓ veraPDF certifica {quale} come PDF/UA-1.\n\n"
                    + "Resta vero che le regole verificabili da una macchina non esauriscono "
                    + "l'accessibilità: la qualità delle marcature va guardata da una persona.",
                    "veraPDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var motivi = report.Failures.Take(10).Select(f => "• " + f);
            MessageBox.Show(this,
                $"✗ {char.ToUpper(quale[0]) + quale[1..]} non è conforme a PDF/UA-1. veraPDF segnala:\n\n"
                + string.Join("\n", motivi)
                + (report.Failures.Count > 10 ? $"\n… e altre {report.Failures.Count - 10} regole." : ""),
                "veraPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
            ValidateButton.IsEnabled = true;
        }
    }
}
