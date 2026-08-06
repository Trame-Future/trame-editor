using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TrameEditor.Core.Markdown;

namespace TrameEditor.App;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadManualAsync();
        Unloaded += (_, _) => Browser.Dispose();
    }

    private async Task LoadManualAsync()
    {
        try
        {
            using var stream = GetType().Assembly
                .GetManifestResourceStream("TrameEditor.App.Assets.Manuale.md")!;
            using var reader = new StreamReader(stream);
            var html = MarkdownRenderService.RenderDocument(reader.ReadToEnd(), "Guida di TrameEditor");

            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrameEditor", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            // I link esterni della guida si aprono nel browser di sistema
            Browser.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true });
            };
            Browser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Guida non disponibile:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }
}
