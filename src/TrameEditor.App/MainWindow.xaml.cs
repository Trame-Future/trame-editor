using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrameEditor.App.Services;
using TrameEditor.App.ViewModels;
using TrameEditor.Core.Shell;
using TrameEditor.Core.Ui;

namespace TrameEditor.App;

public partial class MainWindow : Fluent.RibbonWindow
{
    private readonly MainViewModel _viewModel = new();
    private readonly UiComposer _ui = new();
    private RibbonLayout _ribbonLayout = RibbonLayoutStore.Load();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        BindCommands();
        _ui.BuildMenuBar(ClassicMenu, DecorateMenu);
        _ui.BuildRibbon(RibbonBar, _ribbonLayout);
        Loaded += async (_, _) => await StartAsync();
    }

    /// <summary>
    /// Che cosa fare all'avvio: aprire i file passati sulla riga di comando,
    /// eseguire l'azione chiesta dal menu contestuale di Esplora risorse,
    /// oppure — se non è stato chiesto niente — riprendere da dove si era rimasti.
    /// </summary>
    private async Task StartAsync()
    {
        var restoredDrafts = _viewModel.TryRestoreDrafts();
        var request = StartupArguments.Parse(Environment.GetCommandLineArgs().Skip(1));

        switch (request.Verb)
        {
            case StartupVerb.SearchFolder when Directory.Exists(request.FirstPath):
                _viewModel.ShowFolderSearch(request.FirstPath);
                return;

            case StartupVerb.ExtractSigned when Directory.Exists(request.FirstPath):
                _viewModel.ShowBatch(request.FirstPath);
                return;
        }

        var files = request.Paths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            if (!restoredDrafts)
                _viewModel.RestoreSession();
            return;
        }

        foreach (var path in files)
            _viewModel.OpenPath(path);

        await RunOnOpenedDocumentAsync(request.Verb);
    }

    /// <summary>L'azione da eseguire sul documento appena aperto. Se il
    /// documento non la prevede non succede niente: meglio del silenzio di
    /// un comando che finge di aver funzionato.</summary>
    private async Task RunOnOpenedDocumentAsync(StartupVerb verb)
    {
        switch (verb)
        {
            case StartupVerb.ConvertToPdfA when _viewModel.SelectedDocument is PdfDocumentViewModel pdf:
                await pdf.ConvertToPdfACommand.ExecuteAsync(null);
                break;

            case StartupVerb.ConvertToPdfA when _viewModel.SelectedDocument is TextDocumentViewModel text:
                await text.ConvertToPdfACommand.ExecuteAsync(null);
                break;

            case StartupVerb.Redact when _viewModel.SelectedDocument is PdfDocumentViewModel pdf:
                await pdf.RedactCommand.ExecuteAsync(null);
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_viewModel.ConfirmCloseAll();
        if (!e.Cancel)
            _viewModel.SaveSession();
        base.OnClosing(e);
    }

    /// <summary>
    /// Collega ogni voce del catalogo al comando che la esegue. È l'unico
    /// punto in cui menu classico e barra multifunzione si incontrano col
    /// resto dell'applicazione.
    /// </summary>
    private void BindCommands()
    {
        // File
        _ui.BindPath("new", nameof(MainViewModel.NewDocumentCommand));
        _ui.BindPath("open", nameof(MainViewModel.OpenFileCommand));
        _ui.BindPath("save", nameof(MainViewModel.SaveCommand));
        _ui.BindPath("save-as", nameof(MainViewModel.SaveAsCommand));
        _ui.BindPath("print", nameof(MainViewModel.PrintCommand));
        _ui.BindPath("close-tab", nameof(MainViewModel.CloseActiveDocumentCommand));

        // Modifica
        _ui.BindCommand("undo", ApplicationCommands.Undo);
        _ui.BindCommand("redo", ApplicationCommands.Redo);
        _ui.BindCommand("find", ApplicationCommands.Find);
        _ui.BindPath("search-folder", nameof(MainViewModel.SearchInFolderCommand));

        // Visualizza
        _ui.BindToggle("word-wrap", nameof(MainViewModel.WordWrap));
        _ui.BindToggle("line-numbers", nameof(MainViewModel.ShowLineNumbers));
        _ui.BindToggle("markdown-preview", "SelectedDocument.ShowPreview");
        _ui.BindPath("zoom-in", "SelectedDocument.ZoomInCommand");
        _ui.BindPath("zoom-out", "SelectedDocument.ZoomOutCommand");
        _ui.BindAction("customize-ribbon", CustomizeRibbon);
        _ui.BindAction("reset-ribbon", ResetRibbon);

        // Pagine
        _ui.BindPath("rotate-left", "SelectedDocument.RotateLeftCommand");
        _ui.BindPath("rotate-right", "SelectedDocument.RotateRightCommand");
        _ui.BindPath("page-up", "SelectedDocument.MoveUpCommand");
        _ui.BindPath("page-down", "SelectedDocument.MoveDownCommand");
        _ui.BindPath("page-delete", "SelectedDocument.DeleteSelectedCommand");
        _ui.BindPath("page-extract", "SelectedDocument.ExtractSelectedCommand");
        _ui.BindPath("merge", nameof(MainViewModel.MergePdfsCommand));
        _ui.BindPath("images-to-pdf", nameof(MainViewModel.ImagesToPdfCommand));

        // Converti
        _ui.BindPath("export-pdf", nameof(MainViewModel.ExportPdfCommand));
        _ui.BindPath("pdfa", "SelectedDocument.ConvertToPdfACommand");
        _ui.BindPath("pdfua", "SelectedDocument.CheckAccessibilityCommand");
        _ui.BindPath("export-html", nameof(MainViewModel.ExportHtmlCommand));
        _ui.BindPath("export-images", "SelectedDocument.ExportImagesCommand");
        _ui.BindPath("export-text", "SelectedDocument.ExportTextCommand");
        _ui.BindPath("ocr", "SelectedDocument.RunOcrCommand");
        _ui.BindPath("compress", "SelectedDocument.CompressCommand");

        // Sicurezza
        _ui.BindPath("redact", "SelectedDocument.RedactCommand");
        _ui.BindPath("protect", "SelectedDocument.ProtectCommand");
        _ui.BindPath("signatures", "SelectedDocument.ShowSignaturesCommand");
        _ui.BindAction("profile", () => ProfileWindow.ShowEditor());

        // Strumenti
        _ui.BindPath("compare", nameof(MainViewModel.CompareDocumentsCommand));
        _ui.BindPath("decorate", "SelectedDocument.DecorateCommand");
        _ui.BindPath("batch", nameof(MainViewModel.OpenBatchCommand));
        _ui.BindAction("settings", SettingsWindow.ShowEditor);

        // ?
        _ui.BindCommand("help", ApplicationCommands.Help);
        _ui.BindAction("about", () => new AboutWindow { Owner = this }.ShowDialog());

        Debug.Assert(_ui.UnboundCommands().Count == 0,
            "Comandi senza collegamento: " + string.Join(", ", _ui.UnboundCommands()));
    }

    /// <summary>Le voci del menu che non sono comandi del catalogo:
    /// i file aperti di recente e Esci.</summary>
    private void DecorateMenu(string menu, ItemCollection items)
    {
        if (menu != CommandCatalog.MenuFile)
            return;

        var recent = new MenuItem { Header = "Apri recenti" };
        recent.SetBinding(ItemsControl.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(MainViewModel.RecentFiles)));
        recent.ItemContainerStyle = (Style)Resources["RecentFileItem"];
        items.Insert(2, recent);

        items.Add(new Separator());
        var exit = new MenuItem { Header = "Esci" };
        exit.Click += (_, _) => Close();
        items.Add(exit);
    }

    private void CustomizeRibbon()
    {
        var chosen = CustomizeRibbonWindow.Show(this, _ribbonLayout);
        if (chosen is null)
            return;

        _ribbonLayout = chosen;
        _ui.BuildRibbon(RibbonBar, _ribbonLayout);
    }

    private void ResetRibbon()
    {
        var answer = MessageBox.Show(this,
            "Rimetto la barra multifunzione come era all'installazione?",
            "Ripristina la barra predefinita", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        _ribbonLayout = RibbonLayoutStore.Reset();
        _ui.BuildRibbon(RibbonBar, _ribbonLayout);
    }

    private void Help_Executed(object sender, ExecutedRoutedEventArgs e) =>
        new HelpWindow { Owner = this }.Show();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
                _viewModel.OpenPath(path);
        }
    }
}
