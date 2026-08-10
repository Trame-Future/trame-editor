using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TrameEditor.App.Services;
using TrameEditor.Core.Ai;
using TrameEditor.Core.Documents;
using TrameEditor.Core.Ocr;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Signatures;

namespace TrameEditor.App.ViewModels;

public sealed record PdfSearchMatch(PdfPageViewModel Page, string Snippet)
{
    public string Display => $"Pag. {Page.PageNumber} â€” {Snippet}";
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
    private bool _formMode;

    public ObservableCollection<PdfFormFieldViewModel> FormFields { get; } = [];

    [ObservableProperty]
    private bool _flattenOnApply;

    [ObservableProperty]
    private string _formStatus = string.Empty;

    [ObservableProperty]
    private PdfTextRegionViewModel? _activeRegion;

    [ObservableProperty]
    private string _editText = string.Empty;

    [ObservableProperty]
    private string _planDescription = string.Empty;

    public string ZoomDisplay => $"{Zoom:P0}";

    private PdfDocumentViewModel(string fullPath, string workingPath, PdfRenderService renderer)
    {
        _renderer = renderer;
        _workingPath = workingPath;
        FilePath = fullPath;
        if (!string.Equals(workingPath, fullPath, StringComparison.OrdinalIgnoreCase))
            _tempFiles.Add(workingPath); // copia decifrata: da ripulire alla chiusura
        for (var i = 0; i < renderer.PageCount; i++)
            Pages.Add(new PdfPageViewModel(renderer, i));

        Qa = new DocumentQaViewModel(() => DocumentTextReader.Read(_workingPath));
        Qa.ReferenceRequested += page => PageRequested?.Invoke(this, page);
    }

    /// <summary>Chiesta una pagina (clic su una citazione dell'assistente): la
    /// vista scorre fin lÃ¬.</summary>
    public event EventHandler<int>? PageRequested;

    public static PdfDocumentViewModel CreateFromFile(string path) =>
        CreateFromFile(path, path);

    /// <summary>Apre mostrando <paramref name="path"/> come file del tab ma lavorando
    /// su <paramref name="workingPath"/> (es. copia decifrata di un PDF protetto).</summary>
    public static PdfDocumentViewModel CreateFromFile(string path, string workingPath) =>
        new(Path.GetFullPath(path), workingPath, new PdfRenderService(workingPath));

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
            ShowInfo("Seleziona una o piÃ¹ pagine dalle miniature a sinistra.");
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
            ShowInfo("Seleziona una o piÃ¹ pagine dalle miniature a sinistra.");
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

    /// <summary>Ricerca basata sulle righe (PdfPig): ogni risultato ha anche il
    /// rettangolo da evidenziare sulla pagina.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        ClearSearch();
        var query = SearchQuery.Trim();
        if (query.Length < 2 || IsSearching || !EnsureInspector())
            return;

        IsSearching = true;
        try
        {
            var inspector = _inspector!;
            foreach (var page in Pages.ToList())
            {
                if (page.RotationDelta != 0)
                    continue; // il rettangolo non sarebbe allineato alla rotazione in sospeso
                var pageNumber = page.OriginalIndex + 1;
                var (lines, size) = await Task.Run(() =>
                    (inspector.GetLines(pageNumber), inspector.GetPageSize(pageNumber)));
                foreach (var line in lines)
                {
                    if (!line.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                        continue;
                    SearchMatches.Add(new PdfSearchMatch(page, TrimSnippet(line.Text)));
                    page.SearchHighlights.Add(new PdfTextRegionViewModel(line, size.Height));
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

    private void ClearSearch()
    {
        SearchMatches.Clear();
        SelectedMatch = null;
        foreach (var page in Pages)
            page.SearchHighlights.Clear();
    }

    private static string TrimSnippet(string text) =>
        text.Length <= 70 ? text : text[..70] + "â€¦";

    // ----- ModalitÃ  "Modifica testo" (M3) -----

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

    private bool EnsureInspector()
    {
        try
        {
            _inspector ??= new PdfTextInspector(_workingPath);
            return true;
        }
        catch (Exception ex)
        {
            ShowInfo($"Impossibile analizzare il testo del PDF:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Prepara l'ispettore del testo e avvia il caricamento delle regioni.</summary>
    private bool EnsureRegionsLoaded()
    {
        if (!EnsureInspector())
            return false;
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
            ShowInfo(region.Line.NotEditableReason ?? "Questa riga non Ã¨ modificabile.");
            return;
        }
        ActiveRegion = region;
        EditText = region.Line.Text;
        PlanDescription = "analisi del font in corsoâ€¦";
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
                // OnestÃ  prima di applicare: il risultato non userÃ  il font originale.
                var answer = MessageBox.Show(
                    $"Il font originale non puÃ² essere riutilizzato per questo testo.\n" +
                    $"VerrÃ  usato: {plan.Description}.\n\nApplicare comunque?",
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
        if (FormMode)
            _ = LoadFormFieldsAsync();
        ResetQa(); // il testo Ã¨ cambiato: la sessione dell'assistente va ricostruita
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

    /// <summary>Click sulla pagina con Nota o Timbro attivi; coordinate in unitÃ 
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

    // ----- Modulo AcroForm (M5) -----

    partial void OnFormModeChanged(bool value)
    {
        if (value)
            _ = LoadFormFieldsAsync();
    }

    private async Task LoadFormFieldsAsync()
    {
        FormFields.Clear();
        FormStatus = "lettura del moduloâ€¦";
        try
        {
            var fields = await Task.Run(() => PdfFormService.GetFields(_workingPath));
            foreach (var field in fields)
                FormFields.Add(new PdfFormFieldViewModel(field));
            FormStatus = fields.Count == 0
                ? "Questo PDF non contiene campi modulo compilabili."
                : $"{fields.Count} campi trovati. Compila e premi \"Applica al PDF\".";
        }
        catch (Exception ex)
        {
            FormStatus = $"Modulo non leggibile: {ex.Message}";
        }
    }

    /// <summary>"Compila per me": riempie i campi del modulo con i dati del
    /// profilo locale cifrato, abbinandoli per etichetta. Riempie solo i campi
    /// vuoti; l'utente rivede e conferma con "Applica al PDF".</summary>
    [RelayCommand]
    private void AutoFillForm()
    {
        if (FormFields.Count == 0)
        {
            ShowInfo("Questo PDF non contiene campi modulo compilabili.");
            return;
        }

        var vault = TrameEditor.Core.Profile.PersonalDataVault.CreateDefault();
        var profile = vault.Load();
        if (profile.Count == 0 || profile.Values.All(string.IsNullOrWhiteSpace))
        {
            if (!ProfileWindow.ShowEditor())
                return;
            profile = vault.Load();
            if (profile.Count == 0)
                return;
        }

        var proposals = TrameEditor.Core.Profile.FormAutoFiller.Match(
            FormFields.Where(f => f.IsText).Select(f => f.Name), profile);

        var filled = 0;
        foreach (var proposal in proposals)
        {
            var field = FormFields.FirstOrDefault(f => f.Name == proposal.FieldName);
            if (field is null || !string.IsNullOrWhiteSpace(field.Value))
                continue; // mai sovrascrivere quello che c'Ã¨ giÃ 
            field.Value = proposal.Value;
            filled++;
        }

        FormStatus = filled == 0
            ? "Nessun campo abbinabile ai dati del profilo (o giÃ  compilati)."
            : $"Compilati {filled} campi dal profilo. Controlla i valori e premi \"Applica al PDF\".";
    }

    [RelayCommand]
    private async Task ApplyFormAsync()
    {
        if (FormFields.Count == 0)
            return;
        var values = FormFields.ToDictionary(f => f.Name, f => f.ResultValue);
        var flatten = FlattenOnApply;
        await ApplyPdfOperationAsync((source, target) =>
            PdfFormService.Fill(source, target, values, flatten));
        if (FormMode)
            await LoadFormFieldsAsync();
    }

    // ----- OCR (M5) -----

    [RelayCommand]
    private async Task RunOcrAsync()
    {
        var answer = MessageBox.Show(
            "Le pagine senza layer di testo (scansioni) verranno riconosciute con OCR " +
            "(italiano + inglese, tutto offline) e riceveranno un layer di testo invisibile: " +
            "il PDF diventerÃ  ricercabile senza cambiare aspetto.\n\nProcedere?",
            "TrameEditor", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            IsSearching = true;
            var newWorking = NewTempPath();
            var renderer = _renderer;
            var working = _workingPath;
            var result = await Task.Run(() => PdfOcrService.MakeSearchable(
                working, newWorking, TessdataPath(),
                pageNumber => renderer.RenderPagePngForOcr(pageNumber - 1),
                PdfRenderService.OcrScale));

            if (result.PagesProcessed == 0)
            {
                ShowInfo("Tutte le pagine hanno giÃ  un layer di testo: niente da riconoscere.");
                return;
            }
            SwapWorkingFile(newWorking);
            IsDirty = true;
            ShowInfo($"OCR completato: {result.PagesProcessed} pagine riconosciute, " +
                $"{result.WordsFound} parole. Ora il testo Ã¨ ricercabile.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"OCR non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static string TessdataPath() =>
        Path.Combine(AppContext.BaseDirectory, "tessdata");

    // ----- Compressione (M5) -----

    [RelayCommand]
    private async Task CompressAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = PdfFilter,
            FileName = Path.GetFileNameWithoutExtension(FileName) + " - compresso.pdf",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsSearching = true;
            var staged = NewTempPath();
            var pages = Pages.Select(p => new PdfPageEdit(p.OriginalIndex, p.RotationDelta)).ToList();
            var working = _workingPath;
            var target = dialog.FileName;
            var result = await Task.Run(() =>
            {
                PdfPageOperations.Build(working, pages, staged);
                return PdfCompressor.Compress(staged, target);
            });
            var detail = result.ImagesRecompressed == 0
                ? "nessuna immagine ricomprimibile: ridotta solo la struttura"
                : $"{result.ImagesRecompressed} immagini ricompresse";
            ShowInfo($"PDF salvato: da {result.BeforeBytes / 1048576.0:F2} MB " +
                $"a {result.AfterBytes / 1048576.0:F2} MB ({detail}).");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Compressione non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ----- Conversioni (M6) -----

    /// <summary>Esporta ogni pagina come PNG (render 2x, rotazioni in sospeso applicate).</summary>
    [RelayCommand]
    private async Task ExportImagesAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Scegli la cartella dove salvare le immagini" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsSearching = true;
            var baseName = Path.GetFileNameWithoutExtension(FileName);
            var index = 0;
            foreach (var page in Pages.ToList())
            {
                index++;
                var bitmap = await _renderer.RenderPageAsync(page.OriginalIndex);
                BitmapSource output = page.RotationDelta != 0
                    ? new TransformedBitmap(bitmap, new RotateTransform(page.RotationDelta))
                    : bitmap;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(output));
                var filePath = Path.Combine(dialog.FolderName, $"{baseName}-pag{index:D3}.png");
                using var stream = File.Create(filePath);
                encoder.Save(stream);
            }
            ShowInfo($"{index} immagini PNG salvate in \"{dialog.FolderName}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export immagini non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Esporta il testo del PDF (righe estratte con PdfPig) in un file .txt.</summary>
    [RelayCommand]
    private async Task ExportTextAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "File di testo (*.txt)|*.txt",
            FileName = Path.ChangeExtension(FileName, ".txt"),
        };
        if (dialog.ShowDialog() != true || !EnsureInspector())
            return;

        try
        {
            var inspector = _inspector!;
            var pages = Pages.Select(p => p.OriginalIndex + 1).ToList();
            await Task.Run(() =>
            {
                var builder = new System.Text.StringBuilder();
                foreach (var pageNumber in pages)
                {
                    foreach (var line in inspector.GetLines(pageNumber))
                        builder.AppendLine(line.Text);
                    builder.AppendLine();
                }
                File.WriteAllText(dialog.FileName, builder.ToString());
            });
            ShowInfo($"Testo esportato in \"{Path.GetFileName(dialog.FileName)}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export testo non riuscito:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- Chiedi al documento (AI locale, v2.0; pannello condiviso col testo dalla 2.4) -----

    [ObservableProperty]
    private bool _askMode;

    /// <summary>Il pannello dell'assistente legge le pagine della copia di lavoro:
    /// cosÃ¬ vede anche le modifiche non ancora salvate (OCR, testo, annotazioni).</summary>
    public DocumentQaViewModel Qa { get; }

    partial void OnAskModeChanged(bool value)
    {
        if (value && !Qa.Ready && !Qa.Busy)
            _ = Qa.InitializeAsync();
    }

    private void ResetQa() => Qa.Invalidate(reinitializeNow: AskMode);

    // ----- Anonimizzazione (M8) -----

    [RelayCommand]
    private async Task RedactAsync()
    {
        IReadOnlyList<SensitiveMatch> matches;
        try
        {
            IsSearching = true;
            var working = _workingPath;
            matches = await Task.Run(() => PdfRedactionService.Scan(working));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Analisi non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            IsSearching = false;
        }

        if (matches.Count == 0)
        {
            ShowInfo("Nessun dato sensibile riconosciuto " +
                "(codici fiscali, IBAN, email, telefoni, targhe).\n" +
                "Nota: le scansioni senza OCR non sono analizzabili.");
            return;
        }

        var choice = RedactionDialog.Show(matches);
        if (choice is null)
            return;
        var (selected, stripMetadata) = choice.Value;
        if (selected.Count == 0 && !stripMetadata)
            return;

        try
        {
            IsSearching = true;
            var working = _workingPath;
            var newWorking = NewTempPath();
            var result = await Task.Run(() =>
                PdfRedactionService.Apply(working, newWorking, selected, stripMetadata));
            SwapWorkingFile(newWorking);
            IsDirty = true;

            var message = $"Anonimizzazione completata: {result.ItemsRedacted} dati rimossi" +
                (stripMetadata ? ", metadati ripuliti." : ".") +
                "\nRicorda di salvare con \"Salva con nome\".";
            if (result.SkippedLines.Count > 0)
            {
                message += $"\n\nâš  ATTENZIONE: {result.SkippedLines.Count} righe non erano " +
                    "rimovibili (testo dentro moduli grafici): i dati lÃ¬ presenti NON sono stati tolti:\n" +
                    string.Join("\n", result.SkippedLines.Take(5).Select(l => "â€¢ " + l.Text));
            }
            MessageBox.Show(message, "TrameEditor", MessageBoxButton.OK,
                result.SkippedLines.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Anonimizzazione non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ----- Stampa e protezione (M7) -----

    [RelayCommand]
    private async Task PrintAsync()
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsSearching = true;
            var fixedDocument = new System.Windows.Documents.FixedDocument();
            var areaWidth = dialog.PrintableAreaWidth;
            var areaHeight = dialog.PrintableAreaHeight;
            foreach (var page in Pages.ToList())
            {
                var bitmap = await _renderer.RenderPageAsync(page.OriginalIndex);
                BitmapSource source = page.RotationDelta != 0
                    ? new TransformedBitmap(bitmap, new RotateTransform(page.RotationDelta))
                    : bitmap;
                var image = new System.Windows.Controls.Image
                {
                    Source = source,
                    Stretch = Stretch.Uniform,
                    Width = areaWidth,
                    Height = areaHeight,
                };
                var fixedPage = new System.Windows.Documents.FixedPage
                {
                    Width = areaWidth,
                    Height = areaHeight,
                };
                fixedPage.Children.Add(image);
                var content = new System.Windows.Documents.PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
                fixedDocument.Pages.Add(content);
            }
            dialog.PrintDocument(fixedDocument.DocumentPaginator, FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Stampa non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Salva una copia protetta da password (AES-256), con le modifiche
    /// alle pagine in sospeso applicate.</summary>
    [RelayCommand]
    private async Task ProtectAsync()
    {
        var password = PasswordDialog.CreateNew();
        if (password is null)
            return;
        var dialog = new SaveFileDialog
        {
            Filter = PdfFilter,
            FileName = Path.GetFileNameWithoutExtension(FileName) + " - protetto.pdf",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsSearching = true;
            var staged = NewTempPath();
            var pages = Pages.Select(p => new PdfPageEdit(p.OriginalIndex, p.RotationDelta)).ToList();
            var working = _workingPath;
            var target = dialog.FileName;
            await Task.Run(() =>
            {
                PdfPageOperations.Build(working, pages, staged);
                PdfCryptoService.Encrypt(staged, target, password);
            });
            ShowInfo($"PDF protetto salvato: \"{Path.GetFileName(target)}\".\nConserva la password: senza non sarÃ  apribile.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Protezione non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ----- Firme digitali (M12) -----

    /// <summary>Mostra le firme digitali del documento. Si guarda il file
    /// <b>originale</b>, non la copia di lavoro: le modifiche in sospeso
    /// invaliderebbero le firme e darebbero un esito fuorviante.</summary>
    [RelayCommand]
    private async Task ShowSignaturesAsync()
    {
        var path = FilePath ?? _workingPath;
        try
        {
            IsSearching = true;
            var signatures = await Task.Run(() => PdfSignatureInspector.Inspect(path));
            SignaturesWindow.ShowFor(Path.GetFileName(path), signatures);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lettura delle firme non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ----- Conversione in PDF/A (M11) -----

    /// <summary>
    /// Converte il documento in PDF/A-2 per l'archiviazione. Prima di toccare
    /// qualunque cosa mostra il rapporto di conformitÃ  e fa scegliere fra la
    /// conversione fedele e quella per immagine.
    /// </summary>
    [RelayCommand]
    private async Task ConvertToPdfAAsync()
    {
        try
        {
            IsSearching = true;
            // Si converte quello che vedi: le modifiche alle pagine in sospeso
            // vengono applicate su una copia, l'originale non si tocca.
            var staged = NewTempPath();
            var pages = Pages.Select(p => new PdfPageEdit(p.OriginalIndex, p.RotationDelta)).ToList();
            var working = _workingPath;
            await Task.Run(() => PdfPageOperations.Build(working, pages, staged));

            var renderer = _renderer;
            var outcome = await PdfAWorkflow.RunAsync(staged,
                Path.GetFileNameWithoutExtension(FileName) + " - PDFA.pdf",
                Path.GetFileNameWithoutExtension(FileName),
                pageNumber => renderer.RenderPagePngForOcr(pageNumber - 1),
                PdfRenderService.OcrScale,
                TessdataPath());

            if (outcome is not null)
                MessageBox.Show(outcome.Message, "TrameEditor", MessageBoxButton.OK, outcome.Icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Conversione in PDF/A non riuscita:\n{ex.Message}",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ----- Riordino con drag & drop delle miniature (M5) -----

    public void MovePage(PdfPageViewModel source, PdfPageViewModel? target)
    {
        var from = Pages.IndexOf(source);
        var to = target is null ? Pages.Count - 1 : Pages.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        Pages.Move(from, to);
        IsDirty = true;
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
                // file temporaneo ancora in uso: verrÃ  ripulito al prossimo avvio del SO
            }
        }
    }
}
