using System.Windows;
using TrameEditor.App.Services;
using TrameEditor.Core.Ai;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Session;

namespace TrameEditor.App;

public partial class SettingsWindow : Window
{
    private bool _installing;

    public SettingsWindow()
    {
        InitializeComponent();
        var settings = AppSettings.Load();
        EndpointBox.Text = settings.OllamaEndpoint;
        VeraPdfBox.Text = settings.VeraPdfPath;
        Loaded += (_, _) =>
        {
            ShowRequirements();
            ShowVeraPdfStatus();
        };
    }

    public static void ShowEditor() =>
        new SettingsWindow { Owner = Application.Current.MainWindow }.ShowDialog();

    private void ShowRequirements()
    {
        var report = AiRequirements.Collect();
        RequirementsSummary.Text = report.MeetsMinimum
            ? $"✓ Questo PC può usare l'AI locale ({report.TotalRamGb:F0} GB RAM, {report.CpuCores} core, {report.FreeDiskGb:F0} GB liberi)"
            : "⚠ Questo PC NON soddisfa i requisiti minimi per l'AI locale:";
        RequirementsSummary.Foreground = report.MeetsMinimum
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.Firebrick;
        RequirementsNotes.ItemsSource = report.Notes.Select(n => "• " + n).ToList();
        InstallButton.IsEnabled = report.MeetsMinimum;
        if (!report.MeetsMinimum)
            InstallButton.Content = "Installazione disabilitata: requisiti insufficienti";
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        ConnectionResult.Text = "provo…";
        try
        {
            var models = await new OllamaClient(EndpointBox.Text.Trim()).ListModelsAsync();
            ConnectionResult.Text = models.Count == 0
                ? "✓ Ollama risponde, ma non ci sono modelli: usa l'installazione automatica qui sotto."
                : $"✓ Ollama risponde — modelli: {string.Join(", ", models)}";
        }
        catch (Exception ex)
        {
            ConnectionResult.Text = $"✗ Nessuna risposta da {EndpointBox.Text.Trim()} ({ex.Message})";
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
            return;
        _installing = true;
        InstallButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = string.Empty;
        var progress = new Progress<string>(line =>
        {
            ProgressText.Text = (ProgressText.Text.Length > 4000
                ? ProgressText.Text[^2000..]
                : ProgressText.Text) + line + "\n";
        });
        try
        {
            var ok = await OllamaInstaller.InstallAllAsync(EndpointBox.Text.Trim(),
                EmbeddingCheck.IsChecked == true, progress);
            if (ok)
                ConnectionResult.Text = "✓ Assistente pronto: chiudi e premi Riprova nel pannello Chiedi.";
        }
        catch (Exception ex)
        {
            ((IProgress<string>)progress).Report($"Errore: {ex.Message}");
        }
        finally
        {
            _installing = false;
            InstallButton.IsEnabled = true;
        }
    }

    // ----- veraPDF (validazione formale PDF/A) -----

    private void ShowVeraPdfStatus()
    {
        var found = VeraPdfValidator.FindExecutable(VeraPdfBox.Text.Trim());
        if (found is not null)
        {
            VeraPdfStatus.Text = $"✓ veraPDF trovato: {found}";
            VeraPdfStatus.Foreground = System.Windows.Media.Brushes.DarkGreen;
            InstallVeraPdfButton.Content = "Reinstalla veraPDF";
        }
        else
        {
            VeraPdfStatus.Text = VeraPdfInstaller.IsJavaAvailable()
                ? "veraPDF non è installato. Java c'è già, quindi manca solo lui."
                : "veraPDF non è installato (e su questo PC manca anche Java, che serve per farlo girare).";
            VeraPdfStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            InstallVeraPdfButton.Content = "Installa veraPDF automaticamente";
        }
    }

    private void FindVeraPdf_Click(object sender, RoutedEventArgs e)
    {
        var found = VeraPdfValidator.FindExecutable();
        if (found is not null)
            VeraPdfBox.Text = found;
        ShowVeraPdfStatus();
        if (found is null)
            VeraPdfStatus.Text = "Non ho trovato veraPDF nelle cartelle abituali: " +
                "usa «Sfoglia…» oppure installalo qui sotto.";
    }

    private void BrowseVeraPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Scegli verapdf.bat",
            Filter = "veraPDF (verapdf.bat)|verapdf.bat|Tutti i file|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            VeraPdfBox.Text = dialog.FileName;
            ShowVeraPdfStatus();
        }
    }

    private async void InstallVeraPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
            return;
        _installing = true;
        InstallVeraPdfButton.IsEnabled = false;
        VeraPdfProgressPanel.Visibility = Visibility.Visible;
        VeraPdfProgressText.Text = string.Empty;
        var progress = new Progress<string>(line =>
        {
            VeraPdfProgressText.Text = (VeraPdfProgressText.Text.Length > 4000
                ? VeraPdfProgressText.Text[^2000..]
                : VeraPdfProgressText.Text) + line + "\n";
        });
        try
        {
            if (await VeraPdfInstaller.InstallAsync(progress) is { } path)
                VeraPdfBox.Text = path;
            ShowVeraPdfStatus();
        }
        catch (Exception ex)
        {
            ((IProgress<string>)progress).Report($"Errore: {ex.Message}");
        }
        finally
        {
            _installing = false;
            InstallVeraPdfButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        new AppSettings
        {
            OllamaEndpoint = EndpointBox.Text.Trim(),
            VeraPdfPath = VeraPdfBox.Text.Trim(),
        }.Save();
        DialogResult = true;
        Close();
    }
}
