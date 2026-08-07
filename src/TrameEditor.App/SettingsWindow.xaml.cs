using System.Windows;
using TrameEditor.App.Services;
using TrameEditor.Core.Ai;
using TrameEditor.Core.Session;

namespace TrameEditor.App;

public partial class SettingsWindow : Window
{
    private bool _installing;

    public SettingsWindow()
    {
        InitializeComponent();
        EndpointBox.Text = AppSettings.Load().OllamaEndpoint;
        Loaded += (_, _) => ShowRequirements();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        new AppSettings { OllamaEndpoint = EndpointBox.Text.Trim() }.Save();
        DialogResult = true;
        Close();
    }
}
