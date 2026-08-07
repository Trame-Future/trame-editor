using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App;

public partial class CompareWindow : Window
{
    public sealed class DisplayEntry
    {
        public required string Kind { get; init; }     // Added | Removed | Unchanged | Gap
        public required string Display { get; init; }
        public string Pages { get; init; } = string.Empty;
    }

    private readonly string _leftPath;
    private readonly string _rightPath;
    private PdfCompareResult? _result;

    public CompareWindow(string leftPath, string rightPath)
    {
        InitializeComponent();
        _leftPath = leftPath;
        _rightPath = rightPath;
        HeaderText.Text = $"− {Path.GetFileName(leftPath)}   →   + {Path.GetFileName(rightPath)}";
        SummaryText.Text = "confronto in corso…";
        Loaded += async (_, _) => await RunCompareAsync();
    }

    private async Task RunCompareAsync()
    {
        try
        {
            _result = await Task.Run(() => PdfComparer.Compare(_leftPath, _rightPath));
            DiffList.ItemsSource = BuildDisplayList(_result.Entries);
            SummaryText.Text = _result.AreIdentical
                ? "I due documenti hanno lo stesso testo."
                : $"{_result.AddedCount} righe aggiunte, {_result.RemovedCount} rimosse " +
                  "(confronto sul testo, non sulla grafica)";
            ReportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            SummaryText.Text = "confronto non riuscito";
            MessageBox.Show($"Confronto non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Comprimi le lunghe sequenze identiche: 2 righe di contesto
    /// attorno alle differenze, il resto diventa un separatore.</summary>
    private static List<DisplayEntry> BuildDisplayList(IReadOnlyList<PdfDiffEntry> entries)
    {
        const int context = 2;
        var keep = new bool[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind == DiffKind.Unchanged)
                continue;
            for (var j = Math.Max(0, i - context); j <= Math.Min(entries.Count - 1, i + context); j++)
                keep[j] = true;
        }

        var display = new List<DisplayEntry>();
        var hidden = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!keep[i])
            {
                hidden++;
                continue;
            }
            if (hidden > 0)
            {
                display.Add(new DisplayEntry { Kind = "Gap", Display = $"    ⋯ {hidden} righe identiche ⋯" });
                hidden = 0;
            }
            var entry = entries[i];
            var marker = entry.Kind switch
            {
                DiffKind.Added => "+ ",
                DiffKind.Removed => "− ",
                _ => "  ",
            };
            var pages = entry.Kind switch
            {
                DiffKind.Added => $"pag. {entry.RightPage}",
                DiffKind.Removed => $"pag. {entry.LeftPage}",
                _ => $"pag. {entry.LeftPage}",
            };
            display.Add(new DisplayEntry
            {
                Kind = entry.Kind.ToString(),
                Display = marker + entry.Text,
                Pages = pages,
            });
        }
        if (hidden > 0)
            display.Add(new DisplayEntry { Kind = "Gap", Display = $"    ⋯ {hidden} righe identiche ⋯" });
        if (display.Count == 0)
            display.Add(new DisplayEntry { Kind = "Gap", Display = "(documenti senza testo confrontabile)" });
        return display;
    }

    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null)
            return;
        var dialog = new SaveFileDialog
        {
            Filter = "Pagina HTML (*.html)|*.html",
            FileName = "confronto.html",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, BuildHtmlReport());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Salvataggio non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildHtmlReport()
    {
        var body = new StringBuilder();
        body.AppendLine($"<h1>Confronto PDF</h1><p><b>−</b> {WebUtility.HtmlEncode(Path.GetFileName(_leftPath))} " +
            $"&nbsp;→&nbsp; <b>+</b> {WebUtility.HtmlEncode(Path.GetFileName(_rightPath))}</p>");
        body.AppendLine($"<p>{_result!.AddedCount} righe aggiunte, {_result.RemovedCount} rimosse — " +
            $"rapporto generato da TrameEditor (Trame Future)</p><div class=\"diff\">");
        foreach (var entry in _result.Entries)
        {
            var cls = entry.Kind.ToString().ToLowerInvariant();
            var page = entry.Kind == DiffKind.Added ? entry.RightPage : entry.LeftPage;
            body.AppendLine($"<div class=\"{cls}\"><span class=\"pg\">pag. {page}</span>" +
                WebUtility.HtmlEncode(entry.Text) + "</div>");
        }
        body.AppendLine("</div>");
        return "<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>Confronto PDF</title><style>" +
            "body{font-family:'Segoe UI',sans-serif;max-width:60rem;margin:0 auto;padding:1.5rem}" +
            ".diff div{font-family:Consolas,monospace;font-size:13px;padding:1px 6px;white-space:pre-wrap}" +
            ".added{background:#e7f6ea;color:#1b7a2f}.removed{background:#fbeae8;color:#b02a22}" +
            ".unchanged{color:#555}.pg{float:right;color:#999;font-size:10px;margin-left:8px}" +
            "</style></head><body>" + body + "</body></html>";
    }
}
