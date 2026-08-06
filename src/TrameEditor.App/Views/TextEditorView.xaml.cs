using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Web.WebView2.Core;
using TrameEditor.App.ViewModels;
using TrameEditor.Core.Markdown;

namespace TrameEditor.App.Views;

public partial class TextEditorView : UserControl
{
    private static readonly IHighlightingDefinition? MarkdownHighlighting = LoadMarkdownHighlighting();

    private readonly DispatcherTimer _previewTimer;
    private Task? _webViewInit;
    private bool _previewReady;
    private TextDocumentViewModel? _vm;

    public TextEditorView()
    {
        InitializeComponent();
        SearchPanel.Install(Editor.TextArea);
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            _ = RefreshPreviewAsync();
        };
        Editor.TextChanged += (_, _) =>
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        };
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncPreviewScroll();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Editor.Focus();
        Unloaded += (_, _) =>
        {
            _previewTimer.Stop();
            _previewReady = false;
            Preview.Dispose();
        };
    }

    private static IHighlightingDefinition? LoadMarkdownHighlighting()
    {
        try
        {
            using var stream = typeof(TextEditorView).Assembly
                .GetManifestResourceStream("TrameEditor.App.Assets.Markdown.xshd");
            if (stream is null)
                return null;
            using var reader = XmlReader.Create(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            return null; // senza highlighting l'editor resta comunque usabile
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.PrintRequested -= OnPrintRequested;
        }
        _vm = DataContext as TextDocumentViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.PrintRequested += OnPrintRequested;
        }
        ApplyHighlighting();
        UpdatePreviewLayout();
    }

    /// <summary>Stampa: per il Markdown con anteprima attiva usa la resa HTML
    /// (dialogo di stampa di WebView2); altrimenti stampa il testo impaginato.</summary>
    private void OnPrintRequested(object? sender, EventArgs e)
    {
        if (_vm is null)
            return;

        if (_vm is { IsMarkdown: true, PreviewVisible: true } && _previewReady)
        {
            Preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
            return;
        }

        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
            return;
        var flowDocument = new System.Windows.Documents.FlowDocument(
            new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(Editor.Text)))
        {
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            PagePadding = new Thickness(60),
            ColumnWidth = double.PositiveInfinity,
        };
        var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)flowDocument).DocumentPaginator;
        paginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        dialog.PrintDocument(paginator, _vm.FileName);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextDocumentViewModel.FilePath))
            ApplyHighlighting();
        else if (e.PropertyName is nameof(TextDocumentViewModel.PreviewVisible))
            UpdatePreviewLayout();
    }

    private void ApplyHighlighting() =>
        Editor.SyntaxHighlighting = _vm?.IsMarkdown == true ? MarkdownHighlighting : null;

    private void UpdatePreviewLayout()
    {
        var visible = _vm?.PreviewVisible == true;
        Splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SplitterColumn.Width = visible ? GridLength.Auto : new GridLength(0);
        PreviewColumn.Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (visible)
            _ = RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_vm is not { PreviewVisible: true } vm)
            return;

        try
        {
            _webViewInit ??= InitWebViewAsync();
            await _webViewInit;
        }
        catch (Exception ex)
        {
            vm.ShowPreview = false;
            MessageBox.Show(
                "Anteprima non disponibile: il runtime WebView2 non è utilizzabile.\n" + ex.Message,
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_previewReady)
            Preview.NavigateToString(MarkdownRenderService.RenderDocument(Editor.Text, vm.FileName));
    }

    private async Task InitWebViewAsync()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrameEditor", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        await Preview.EnsureCoreWebView2Async(environment);
        Preview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Preview.NavigationCompleted += (_, _) => SyncPreviewScroll();
        _previewReady = true;
    }

    private void SyncPreviewScroll()
    {
        if (!_previewReady || _vm?.PreviewVisible != true)
            return;
        var scrollable = Editor.ExtentHeight - Editor.ViewportHeight;
        if (scrollable <= 0)
            return;
        var ratio = Math.Clamp(Editor.VerticalOffset / scrollable, 0, 1)
            .ToString(CultureInfo.InvariantCulture);
        _ = Preview.ExecuteScriptAsync(
            $"window.scrollTo(0, (document.documentElement.scrollHeight - window.innerHeight) * {ratio});");
    }

    // ----- Toolbar Markdown -----

    private void Bold_Click(object sender, RoutedEventArgs e) => WrapSelection("**");

    private void Italic_Click(object sender, RoutedEventArgs e) => WrapSelection("*");

    private void Code_Click(object sender, RoutedEventArgs e) => WrapSelection("`");

    private void H1_Click(object sender, RoutedEventArgs e) => SetHeading(1);

    private void H2_Click(object sender, RoutedEventArgs e) => SetHeading(2);

    private void H3_Click(object sender, RoutedEventArgs e) => SetHeading(3);

    private void Bullet_Click(object sender, RoutedEventArgs e) => TogglePrefix("- ");

    private void Quote_Click(object sender, RoutedEventArgs e) => TogglePrefix("> ");

    private void Numbered_Click(object sender, RoutedEventArgs e)
    {
        ForEachSelectedLine((line, indexFromTop) =>
        {
            var text = Editor.Document.GetText(line);
            var stripped = StripListPrefix(text);
            Editor.Document.Replace(line.Offset, line.Length, $"{indexFromTop + 1}. {stripped}");
        });
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        if (length == 0)
        {
            Editor.Document.Insert(start, "[testo](https://)");
            Editor.Select(start + 1, 5); // seleziona "testo"
        }
        else
        {
            var text = Editor.SelectedText;
            Editor.Document.Replace(start, length, $"[{text}](https://)");
            Editor.Select(start + length + 3, 8); // seleziona "https://"
        }
        Editor.Focus();
    }

    private void Table_Click(object sender, RoutedEventArgs e)
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var prefix = line.Length > 0 ? Environment.NewLine + Environment.NewLine : string.Empty;
        Editor.Document.Insert(line.EndOffset,
            prefix +
            "| Colonna 1 | Colonna 2 |" + Environment.NewLine +
            "|-----------|-----------|" + Environment.NewLine +
            "|           |           |");
        Editor.Focus();
    }

    private void WrapSelection(string marker)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        if (length == 0)
        {
            Editor.Document.Insert(start, marker + marker);
            Editor.CaretOffset = start + marker.Length;
        }
        else
        {
            var text = Editor.SelectedText;
            Editor.Document.Replace(start, length, marker + text + marker);
            Editor.Select(start, length + 2 * marker.Length);
        }
        Editor.Focus();
    }

    private void SetHeading(int level)
    {
        ForEachSelectedLine((line, _) =>
        {
            var text = Editor.Document.GetText(line);
            var stripped = text.TrimStart('#').TrimStart();
            Editor.Document.Replace(line.Offset, line.Length, new string('#', level) + " " + stripped);
        });
    }

    private void TogglePrefix(string prefix)
    {
        ForEachSelectedLine((line, _) =>
        {
            var text = Editor.Document.GetText(line);
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                Editor.Document.Remove(line.Offset, prefix.Length);
            else
                Editor.Document.Insert(line.Offset, prefix);
        });
    }

    private static string StripListPrefix(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            return trimmed[2..];
        var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && trimmed[..dot].All(char.IsAsciiDigit))
            return trimmed[(dot + 2)..];
        return trimmed;
    }

    /// <summary>Applica un'azione a ogni riga selezionata iterando dall'ultima alla prima
    /// (gli offset delle righe precedenti restano validi durante le modifiche);
    /// il secondo argomento è l'indice della riga contando dall'alto, da 0.</summary>
    private void ForEachSelectedLine(Action<DocumentLine, int> action)
    {
        var document = Editor.Document;
        var startLine = document.GetLineByOffset(Editor.SelectionStart).LineNumber;
        var endLine = document.GetLineByOffset(Editor.SelectionStart + Editor.SelectionLength).LineNumber;
        using (document.RunUpdate())
        {
            for (var lineNumber = endLine; lineNumber >= startLine; lineNumber--)
                action(document.GetLineByNumber(lineNumber), lineNumber - startLine);
        }
        Editor.Focus();
    }
}
