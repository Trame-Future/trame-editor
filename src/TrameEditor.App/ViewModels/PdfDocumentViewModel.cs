using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TrameEditor.App.Services;
using TrameEditor.Core.Pdf;

namespace TrameEditor.App.ViewModels;

public sealed record PdfSearchMatch(PdfPageViewModel Page, string Snippet)
{
    public string Display => $"Pag. {Page.PageNumber} — {Snippet}";
}

public enum PdfAnnotationTool
{
    None,
    Highlight,
    Note,
    Stamp,
}

public partial class PdfDocumentViewModel : DocumentTabViewModel
{
    private const string PdfFilter = "PDF (*.pdf)|*.pdf";
    private PdfRenderService _renderer;
    private PdfTextInspector? _inspector;
    private string _workingPath;
    private readonly List<string> _tempFiles = [];
    private int _regionsVersion;

    public ObservableCollection<PdfPageViewModel> Pages { get; } = [];
    public ObservableCollection<PdfSearchMatch> SearchMatches { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomDisplay))]
    private double _zoom = 1.0;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private PdfSearchMatch? _selectedMatch;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _editMode;

    [ObservableProperty]
    private PdfAnnotationTool _annotationTool = PdfAnnotationTool.None;

    private string? _stampImagePath;

    /// <summary>Le regioni cliccabili servono sia alla modifica testo sia all'evidenziazione.</summary>
    public bool ShowRegions => EditMode || AnnotationTool == PdfAnnotationTool.Highlight;

    [ObservableProperty]
    private PdfTextRegionViewModel? _activeRegion;

    [ObservableProperty]
    private string _editText = string.Empty;

    [ObservableProperty]
    private string _planDescription = string.Empty;

    public string ZoomDisplay => $"{Zoom:P0}";

    private PdfDocumentViewModel(string fullPath, PdfRenderService renderer)
    {
        _renderer = renderer;
        _workingPath = fullPath;
        FilePath = fullPath;
        for (var i = 0; i < renderer.PageCount; i++)
            Pages.Add(new PdfPageViewModel(renderer, i));
    }

    public static PdfDocumentViewModel CreateFromFile(string path) =>
        new(Path.GetFullPath(path), new PdfRenderService(path));

    private List<PdfPageViewModel> SelectedPages() => [.. Pages.Where(p => p.IsSelected)];

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(3.0, Math.Round(Zoom + 0.25, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.5, Math.Round(Zoom - 0.25, 2));

    [RelayCommand]
    private void RotateRight() => RotateSelected(90);

    [RelayCommand]
    private void RotateLeft() => RotateSelected(-90);

    private void RotateSelected(int delta)
    {
        var targets = SelectedPages();
        if (targets.Count == 0)
        {
            ShowInfo("Seleziona una o più pagine dalle miniature a sinistra.");
            return;
        }
        foreach (var page in targets)
            page.Rotate(delta);
        IsDirty = true;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var targets = SelectedPages();
        if (targets.Count == 0)
        {
            ShowInfo("Seleziona una o più pagine dalle miniature a sinistra.");
            return;
        }
        if (targets.Count == Pages.Count)
        {
            ShowInfo("Un PDF deve contenere almeno una pagina.");
            return;
        }
        foreach (var page in targets)
            Pages.Remove(page);
        IsDirty = true;
    }

    [RelayCommand]
    private void MoveUp()
    {
        var indices = SelectedIndices();
        if (indices.Count == 0 || indices[0] == 0)
            return;
        foreach (var index in indices)
            Pages.Move(index, index - 1);
        IsDirty = true;
    }

    [RelayCommand]
    private void MoveDown()
    {
        var indices = SelectedIndices();
        if (indices.Count == 0 || indices[^1] == Pages.Count - 1)
            return;
        for (var i = indices.Count - 1; i >= 0; i--)
            Pages.Move(indices[i], indices[i] + 1);
        IsDirty = true;
    }

    private List<int> SelectedIndices() =>
        [.. Enumerable.Range(0, Pages.Count).Where(i => Pages[i].IsSelected)];

    [RelayCommand]
    private void ExtractSelected()
    {
        var targets = SelectedPages();
        if (targets.Count == 0)
        {
            ShowInfo("Seleziona le pagine da estrarre dalle miniature a sinistra.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = PdfFilter,
            FileName = Path.GetFileNameWithoutExtension(FileName) + " - estratto.pdf",
        };
        if (dialog.ShowDialog() != true)
            return;

        if (TryBuild(targets, dialog.FileName))
            ShowInfo($"Estratte {targets.Count} pagine in \"{Path.GetFileName(dialog.FileName)}\".");
    }

    /// <summary>Applica le modifiche (ordine, rotazioni, eliminazioni) salvando un PDF.</summary>
    public bool SaveAs()
    {
        var dialog = new SaveFileDialog { Filter = PdfFilter, FileName = FileName };
        if (dialog.ShowDialog() != true)
            return false;

        if (!TryBuild([.. Pages], dialog.FileName))
            return false;
        IsDirty = false;
        return true;
    }

    private bool TryBuild(IReadOnlyList<PdfPageViewModel> pages, string targetPath)
    {
        try
        {
            PdfPageOperations.Build(
                _workingPath,
                [.. pages.Select(p => new PdfPageEdit(p.OriginalIndex, p.RotationDelta))],
                targetPath);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile scrivere \"{targetPath}\":\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchMatches.Clear();
        var query = SearchQuery.Trim();
        if (query.Length < 2 || IsSearching)
            return;

        IsSearching = true;
        try
        {
            foreach (var page in Pages.ToList())
            {
                var text = await _renderer.GetPageTextAsync(page.OriginalIndex);
                var start = 0;
                while ((start = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    SearchMatches.Add(new PdfSearchMatch(page, Snippet(text, start, query.Length)));
                    start += query.Length;
                    if (SearchMatches.Count >= 500)
                        return;
                }
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static string Snippet(string text, int matchStart, int matchLength)
    {
        const int context = 28;
        var from = Math.Max(0, matchStart - context);
        var to = Math.Min(text.Length, matchStart + matchLength + context);
        var snippet = text[from..to].ReplaceLineEndings(" ").Trim();
        return (from > 0 ? "…" : "") + snippet + (to < text.Length ? "…" : "");
    }

    // ----- Modalità "Modifica testo" (M3) -----

    partial void OnEditModeChanged(bool value)
    {
        if (value)
        {
            AnnotationTool = PdfAnnotationTool.None;
            if (!EnsureRegionsLoaded())
            {
                EditMode = false;
                return;
            }
        }
        else
        {
            ActiveRegion = null;
        }
        OnPropertyChanged(nameof(ShowRegions));
    }

    partial void OnAnnotationToolChanged(PdfAnnotationTool value)
    {
        if (value != PdfAnnotationTool.None)
        {
            EditMode = false;
            ActiveRegion = null;
        }

        if (value == PdfAnnotationTool.Highlight && !EnsureRegionsLoaded())
        {
            AnnotationTool = PdfAnnotationTool.None;
            return;
        }

        if (value == PdfAnnotationTool.Stamp)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Scegli l'immagine da usare come timbro",
                Filter = "Immagini (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            };
            if (dialog.ShowDialog() == true)
            {
                _stampImagePath = dialog.FileName;
            }
            else
            {
                AnnotationTool = PdfAnnotationTool.None;
                return;
            }
        }

        OnPropertyChanged(nameof(ShowRegions));
    }

    /// <summary>Prepara l'ispettore del testo e avvia il caricamento delle regioni.</summary>
    private bool EnsureRegionsLoaded()
    {
        try
        {
            _inspector ??= new PdfTextInspector(_workingPath);
        }
        catch (Exception ex)
        {
            ShowInfo($"Impossibile analizzare il testo del PDF:\n{ex.Message}");
            return false;
        }
        _ = LoadAllRegionsAsync();
        return true;
    }

    private async Task LoadAllRegionsAsync()
    {
        var version = ++_regionsVersion;
        var inspector = _inspector;
        if (inspector is null)
            return;

        foreach (var page in Pages.ToList())
        {
            if (version != _regionsVersion || !ShowRegions)
                return;
            page.EditRegions.Clear();
            if (page.RotationDelta != 0)
                continue; // limite documentato: applicare prima le rotazioni salvando
            var pageNumber = page.OriginalIndex + 1;
            try
            {
                var (lines, size) = await Task.Run(() =>
                    (inspector.GetLines(pageNumber), inspector.GetPageSize(pageNumber)));
                if (version != _regionsVersion)
                    return;
                foreach (var line in lines)
                    page.EditRegions.Add(new PdfTextRegionViewModel(line, size.Height));
            }
            catch
            {
                // pagina senza testo analizzabile: nessuna regione
            }
        }
    }

    [RelayCommand]
    private void BeginEdit(PdfTextRegionViewModel region)
    {
        if (!EditMode)
            return;
        if (!region.IsEditable)
        {
            ShowInfo(region.Line.NotEditableReason ?? "Questa riga non è modificabile.");
            return;
        }
        ActiveRegion = region;
        EditText = region.Line.Text;
        PlanDescription = "analisi del font in corso…";
        _ = UpdatePlanAsync(region);
    }

    private async Task UpdatePlanAsync(PdfTextRegionViewModel region)
    {
        try
        {
            var text = EditText;
            var plan = await Task.Run(() => PdfTextReplacer.PlanFor(_workingPath, region.Line, text));
            if (ActiveRegion == region)
                PlanDescription = plan.Description;
        }
        catch
        {
            if (ActiveRegion == region)
                PlanDescription = "analisi del font non riuscita";
        }
    }

    [RelayCommand]
    private async Task ApplyEditAsync()
    {
        if (ActiveRegion is not { } region)
            return;
        var newText = EditText;
        if (newText == region.Line.Text)
        {
            ActiveRegion = null;
            return;
        }

        try
        {
            var plan = await Task.Run(() => PdfTextReplacer.PlanFor(_workingPath, region.Line, newText));
            if (plan.Strategy != PdfFontStrategy.ReuseEmbedded)
            {
                // Onestà prima di applicare: il risultato non userà il font originale.
                var answer = MessageBox.Show(
                    $"Il font originale non può essere riutilizzato per questo testo.\n" +
                    $"Verrà usato: {plan.Description}.\n\nApplicare comunque?",
                    "TrameEditor", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var newWorking = NewTempPath();
            await Task.Run(() => PdfTextReplacer.Replace(_workingPath, newWorking, region.Line, newText, plan));
            SwapWorkingFile(newWorking);
            IsDirty = true;
            ActiveRegion = null;
        }
        catch (PdfTextEditException ex)
        {
            ShowInfo(ex.Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Modifica non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelEdit() => ActiveRegion = null;

    private string NewTempPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrameEditor");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>Sostituisce il file di lavoro dopo una modifica al testo:
    /// ricrea renderer e pagine preservando ordine, rotazioni ed eliminazioni.</summary>
    private void SwapWorkingFile(string newWorkingPath)
    {
        _renderer.Dispose();
        _inspector?.Dispose();
        _inspector = null;
        _workingPath = newWorkingPath;
        _renderer = new PdfRenderService(newWorkingPath);

        var specs = Pages.Select(p => (p.OriginalIndex, p.RotationDelta)).ToList();
        Pages.Clear();
        foreach (var (index, rotation) in specs)
            Pages.Add(new PdfPageViewModel(_renderer, index) { RotationDelta = rotation });
        SearchMatches.Clear();
        SelectedMatch = null;

        if (ShowRegions)
        {
            _inspector = new PdfTextInspector(newWorkingPath);
            _ = LoadAllRegionsAsync();
        }
    }

    // ----- Annotazioni (M4): evidenzia, nota, timbro immagine -----

    /// <summary>Click su una regione di testo con lo strumento Evidenzia attivo.</summary>
    [RelayCommand]
    private Task HighlightRegionAsync(PdfTextRegionViewModel region)
    {
        var line = region.Line;
        return ApplyPdfOperationAsync((source, target) =>
            PdfAnnotationService.HighlightArea(source, target,
                line.PageNumber, line.Left, line.Bottom, line.Width, line.Height));
    }

    /// <summary>Click sulla pagina con Nota o Timbro attivi; coordinate in unità
    /// display a zoom 100% (equivalenti ai punti PDF), origine in alto a sinistra.</summary>
    public async Task HandlePageClickAsync(PdfPageViewModel page, double x, double y)
    {
        if (page.RotationDelta != 0)
        {
            ShowInfo("La pagina ha una rotazione in sospeso: salva prima il PDF, poi annota.");
            return;
        }

        var pageNumber = page.OriginalIndex + 1;
        var pdfX = x;
        var pdfY = page.BaseHeight - y;

        switch (AnnotationTool)
        {
            case PdfAnnotationTool.Note:
                var text = NoteDialog.Prompt();
                if (text is null)
                    return;
                await ApplyPdfOperationAsync((source, target) =>
                    PdfAnnotationService.AddNote(source, target, pageNumber, pdfX, pdfY, text));
                break;

            case PdfAnnotationTool.Stamp when _stampImagePath is not null:
                var imagePath = _stampImagePath;
                await ApplyPdfOperationAsync((source, target) =>
                    PdfAnnotationService.StampImage(source, target, pageNumber, pdfX, pdfY, imagePath, 120));
                break;
        }
    }

    /// <summary>Applica un'operazione PDF alla working copy e ricarica il documento.</summary>
    private async Task ApplyPdfOperationAsync(Action<string, string> operation)
    {
        try
        {
            var newWorking = NewTempPath();
            await Task.Run(() => operation(_workingPath, newWorking));
            SwapWorkingFile(newWorking);
            IsDirty = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Operazione non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ShowInfo(string message) =>
        MessageBox.Show(message, "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);

    public override void Dispose()
    {
        _renderer.Dispose();
        _inspector?.Dispose();
        foreach (var temp in _tempFiles)
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // file temporaneo ancora in uso: verrà ripulito al prossimo avvio del SO
            }
        }
    }
}
