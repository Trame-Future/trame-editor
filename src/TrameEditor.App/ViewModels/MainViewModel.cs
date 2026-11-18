using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Invoices;
using TrameEditor.Core.Pdf;
using TrameEditor.Core.Tables;
using TrameEditor.Core.Signatures;
using TrameEditor.Core.Session;

namespace TrameEditor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string OpenFilter =
        "Tutti i file supportati (*.pdf;*.txt;*.md;*.p7m;*.xml)|" +
            "*.pdf;*.txt;*.md;*.markdown;*.p7m;*.xml|" +
        "PDF (*.pdf)|*.pdf|" +
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|" +
        "Documenti firmati (*.p7m)|*.p7m|" +
        "Fatture elettroniche (*.xml;*.p7m)|*.xml;*.p7m|" +
        "Tutti i file (*.*)|*.*";
    private const string TextSaveFilter =
        "File di testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|Tutti i file (*.*)|*.*";

    private readonly SessionStore _sessionStore = SessionStore.CreateDefault();
    private readonly DraftStore _draftStore = DraftStore.CreateDefault();
    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer;

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = [];

    public ObservableCollection<string> RecentFiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private DocumentTabViewModel? _selectedDocument;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    public MainViewModel()
    {
        foreach (var recent in _sessionStore.Load().RecentFiles)
            RecentFiles.Add(recent);
        NewDocument();
        _autosaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _autosaveTimer.Tick += (_, _) => SaveDrafts();
        _autosaveTimer.Start();
    }

    /// <summary>Autosalvataggio: bozze dei documenti di testo con modifiche non salvate.</summary>
    private void SaveDrafts()
    {
        try
        {
            var dirty = Documents.OfType<TextDocumentViewModel>().Where(d => d.IsDirty).ToList();
            foreach (var document in dirty)
            {
                _draftStore.Save(new DocumentDraft
                {
                    Id = document.DraftId,
                    OriginalPath = document.FilePath,
                    DisplayName = document.FileName,
                    Content = document.EditorDocument.Text,
                    SavedAtUtc = DateTime.UtcNow,
                });
            }
            var activeIds = dirty.Select(d => d.DraftId).ToHashSet();
            foreach (var stale in _draftStore.LoadAll().Where(d => !activeIds.Contains(d.Id)))
                _draftStore.Delete(stale.Id);
        }
        catch
        {
            // l'autosalvataggio non deve mai disturbare il lavoro
        }
    }

    /// <summary>All'avvio: se ci sono bozze la sessione precedente è stata interrotta.
    /// Restituisce true se l'utente le ha ripristinate.</summary>
    public bool TryRestoreDrafts()
    {
        var drafts = _draftStore.LoadAll();
        if (drafts.Count == 0)
            return false;

        var answer = MessageBox.Show(
            $"La sessione precedente si è interrotta con {drafts.Count} " +
            (drafts.Count == 1 ? "documento non salvato" : "documenti non salvati") +
            ".\nVuoi ripristinare le bozze?",
            "TrameEditor", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        _draftStore.Clear();
        if (answer != MessageBoxResult.Yes)
            return false;

        foreach (var draft in drafts)
        {
            var document = TextDocumentViewModel.CreateFromDraft(draft);
            if (Documents is [TextDocumentViewModel { IsPristineUntitled: true } blank])
                Documents.Remove(blank);
            Documents.Add(document);
            SelectedDocument = document;
        }
        return true;
    }

    [RelayCommand]
    private void Print()
    {
        switch (SelectedDocument)
        {
            case PdfDocumentViewModel pdf:
                pdf.PrintCommand.Execute(null);
                break;
            case TextDocumentViewModel text:
                text.RequestPrint();
                break;
        }
    }

    /// <summary>Riapre i file dell'ultima sessione (chiamato all'avvio senza argomenti).</summary>
    public void RestoreSession()
    {
        foreach (var path in _sessionStore.Load().OpenFiles.Where(File.Exists))
            OpenPath(path);
    }

    /// <summary>Salva sessione e recenti (chiamato alla chiusura confermata).
    /// La chiusura è pulita: le bozze di autosalvataggio non servono più.</summary>
    public void SaveSession()
    {
        try
        {
            _autosaveTimer.Stop();
            _draftStore.Clear();
            _sessionStore.Save(new SessionState
            {
                OpenFiles = [.. Documents.Select(d => d.FilePath).OfType<string>()],
                RecentFiles = [.. RecentFiles],
            });
        }
        catch
        {
            // la sessione è un comfort: mai bloccare la chiusura per questo
        }
    }

    private void RegisterRecent(string path)
    {
        var updated = SessionStore.PushRecent(RecentFiles, Path.GetFullPath(path));
        RecentFiles.Clear();
        foreach (var item in updated)
            RecentFiles.Add(item);
    }

    [RelayCommand]
    private void OpenRecent(string path)
    {
        if (File.Exists(path))
            OpenPath(path);
        else
        {
            ShowError($"Il file non esiste più:\n{path}");
            RecentFiles.Remove(path);
        }
    }

    public string WindowTitle => SelectedDocument is null
        ? "TrameEditor"
        : $"{SelectedDocument.DisplayName} — TrameEditor";

    partial void OnSelectedDocumentChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentTabViewModel.DisplayName))
            OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void NewDocument()
    {
        var document = TextDocumentViewModel.CreateUntitled();
        Documents.Add(document);
        SelectedDocument = document;
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog { Filter = OpenFilter, Multiselect = true };
        if (dialog.ShowDialog() == true)
            foreach (var path in dialog.FileNames)
                OpenPath(path);
    }

    public void OpenPath(string path)
    {
        var existing = Documents.FirstOrDefault(d => d.RepresentsFile(path));
        if (existing is not null)
        {
            SelectedDocument = existing;
            return;
        }

        if (P7mReader.IsP7m(path))
        {
            OpenSignedEnvelope(path);
            return;
        }

        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
            FatturaElettronicaReader.LooksLikeInvoice(path))
        {
            OpenElectronicInvoice(path);
            return;
        }

        try
        {
            DocumentTabViewModel? document;
            if (Path.GetExtension(path).ToLowerInvariant() == ".pdf")
            {
                document = PdfCryptoService.IsPasswordProtected(path)
                    ? OpenProtectedPdf(path)
                    : PdfDocumentViewModel.CreateFromFile(path);
                if (document is null)
                    return; // apertura annullata dall'utente
            }
            else
            {
                document = TextDocumentViewModel.CreateFromFile(path);
            }
            if (Documents is [TextDocumentViewModel { IsPristineUntitled: true } blank])
                Documents.Remove(blank);
            Documents.Add(document);
            SelectedDocument = document;
            RegisterRecent(path);
        }
        catch (Exception ex)
        {
            ShowError($"Impossibile aprire \"{path}\":\n{ex.Message}");
        }
    }

    /// <summary>
    /// Apre una fattura elettronica: al posto dell'XML illeggibile mostra una
    /// trascrizione in italiano, e apre in schede separate gli eventuali allegati
    /// PDF (la "copia di cortesia" che molti fornitori mettono dentro la fattura).
    /// </summary>
    private void OpenElectronicInvoice(string path)
    {
        ElectronicInvoice invoice;
        try
        {
            invoice = FatturaElettronicaReader.Read(path);
        }
        catch (InvoiceReadException ex)
        {
            // Non � leggibile come fattura: si apre comunque il file, come testo.
            ShowError($"{ex.Message}\n\nIl file viene aperto come documento di testo.");
            OpenAsText(path);
            return;
        }

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "TrameEditor", "fatture");
            Directory.CreateDirectory(directory);

            var baseName = Path.GetFileNameWithoutExtension(path);
            var readable = Path.Combine(directory, $"{baseName} - leggibile.md");
            File.WriteAllText(readable,
                FatturaRenderer.ToMarkdown(invoice, Path.GetFileName(path)));
            OpenPath(readable);
            RegisterRecent(path);
            var readableDocument = SelectedDocument;

            foreach (var attachment in invoice.Documenti.SelectMany(d => d.Allegati))
            {
                var target = Path.Combine(directory, attachment.Nome);
                File.WriteAllBytes(target, attachment.Data);
                if (Path.GetExtension(target).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    OpenPath(target);
            }

            // Gli allegati si aprono in schede a parte, ma davanti resta la
            // fattura: è quella che l'utente ha chiesto di vedere.
            if (readableDocument is not null)
                SelectedDocument = readableDocument;
        }
        catch (Exception ex)
        {
            ShowError($"Fattura letta ma non apribile:\n{ex.Message}");
        }
    }

    private void OpenAsText(string path)
    {
        try
        {
            var document = TextDocumentViewModel.CreateFromFile(path);
            Documents.Add(document);
            SelectedDocument = document;
            RegisterRecent(path);
        }
        catch (Exception ex)
        {
            ShowError($"Impossibile aprire \"{path}\":\n{ex.Message}");
        }
    }

    /// <summary>
    /// Apre una busta firmata <c>.p7m</c>: tira fuori il documento che contiene,
    /// lo apre come un file normale e mostra chi l'ha firmato. È il formato che
    /// arriva di continuo dalla pubblica amministrazione e che nessun programma
    /// comune sa aprire.
    /// </summary>
    private void OpenSignedEnvelope(string path)
    {
        P7mContent content;
        try
        {
            content = P7mReader.Read(path);
        }
        catch (SignatureReadException ex)
        {
            ShowError(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            ShowError($"Impossibile aprire \"{Path.GetFileName(path)}\":\n{ex.Message}");
            return;
        }

        string extracted;
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "TrameEditor", "firmati");
            Directory.CreateDirectory(directory);
            extracted = Path.Combine(directory, content.SuggestedFileName);
            File.WriteAllBytes(extracted, content.Data);
        }
        catch (Exception ex)
        {
            ShowError($"Documento estratto ma non salvabile:\n{ex.Message}");
            return;
        }

        OpenPath(extracted);
        RegisterRecent(path);

        // Il riquadro delle firme si apre quando la finestra principale � pronta:
        // un dialogo modale aperto mentre l'applicazione sta ancora nascendo
        // (apertura da riga di comando o da trascinamento all'avvio) resta vuoto.
        var fileName = Path.GetFileName(path);
        var signers = content.Signers;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => SignaturesWindow.ShowFor(fileName, signers)),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Chiede la password e apre il PDF su una copia decifrata temporanea.
    /// Null se l'utente annulla.</summary>
    private PdfDocumentViewModel? OpenProtectedPdf(string path)
    {
        while (true)
        {
            var password = PasswordDialog.Ask(Path.GetFileName(path));
            if (password is null)
                return null;

            var workingDirectory = Path.Combine(Path.GetTempPath(), "TrameEditor");
            Directory.CreateDirectory(workingDirectory);
            var workingPath = Path.Combine(workingDirectory, $"{Guid.NewGuid():N}.pdf");
            try
            {
                PdfCryptoService.Decrypt(path, workingPath, password);
                return PdfDocumentViewModel.CreateFromFile(path, workingPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                ShowError("Password errata: riprova.");
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedDocument is not null)
            SaveDocument(SelectedDocument);
    }

    [RelayCommand]
    private void SaveAs()
    {
        if (SelectedDocument is TextDocumentViewModel text)
            SaveTextAs(text);
        else if (SelectedDocument is PdfDocumentViewModel pdf)
            pdf.SaveAs();
    }

    [RelayCommand]
    private void CloseActiveDocument()
    {
        if (SelectedDocument is not null)
            CloseDocument(SelectedDocument);
    }

    [RelayCommand]
    private void CloseDocument(DocumentTabViewModel document)
    {
        if (!ConfirmClose(document))
            return;
        Documents.Remove(document);
        if (document is TextDocumentViewModel text)
            _draftStore.Delete(text.DraftId);
        document.Dispose();
    }

    [RelayCommand]
    private void ExportHtml()
    {
        if (SelectedDocument is not TextDocumentViewModel { IsMarkdown: true } document)
        {
            ShowError("L'export HTML è disponibile solo per i file Markdown (.md).");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Pagina HTML (*.html)|*.html",
            FileName = Path.ChangeExtension(document.FileName, ".html"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName,
                MarkdownRenderService.RenderDocument(document.EditorDocument.Text, document.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Impossibile salvare \"{dialog.FileName}\":\n{ex.Message}");
        }
    }

    /// <summary>Salva in PDF il documento di testo aperto: il Markdown impaginato
    /// come nell'anteprima, un .txt come testo semplice a spaziatura fissa.</summary>
    [RelayCommand]
    private void ExportPdf()
    {
        if (SelectedDocument is not TextDocumentViewModel document)
        {
            ShowError("L'export PDF riguarda i documenti di testo e Markdown. " +
                "Un PDF già aperto si salva con \"Salva con nome\", oppure si archivia " +
                "con \"Converti in PDF/A\".");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.ChangeExtension(document.FileName, ".pdf"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            document.WritePdf(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Export PDF non riuscito:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ImagesToPdf()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "Immagini (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = true,
            Title = "Scegli le immagini da unire in un PDF (nell'ordine di selezione)",
        };
        if (openDialog.ShowDialog() != true || openDialog.FileNames.Length == 0)
            return;

        var saveDialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "immagini.pdf" };
        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            ImagesToPdfConverter.Convert(openDialog.FileNames, saveDialog.FileName);
            OpenPath(saveDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Conversione non riuscita:\n{ex.Message}");
        }
    }

    /// <summary>Confronta due documenti: PDF, testo o Markdown, anche misti —
    /// il confronto guarda il testo, non il formato.</summary>
    [RelayCommand]
    private void CompareDocuments()
    {
        const string filter = "Documenti confrontabili (*.pdf;*.txt;*.md)|*.pdf;*.txt;*.md;*.markdown;*.log;*.csv" +
            "|PDF (*.pdf)|*.pdf|Testo e Markdown (*.txt;*.md)|*.txt;*.md;*.markdown|Tutti i file|*.*";

        var first = new OpenFileDialog
        {
            Filter = filter,
            Title = "Scegli il PRIMO documento (la versione vecchia)",
            FileName = SelectedDocument?.FilePath ?? string.Empty,
        };
        if (first.ShowDialog() != true)
            return;
        var second = new OpenFileDialog
        {
            Filter = filter,
            Title = "Scegli il SECONDO documento (la versione nuova)",
        };
        if (second.ShowDialog() != true)
            return;
        new CompareWindow(first.FileName, second.FileName)
        {
            Owner = Application.Current.MainWindow,
        }.Show();
    }

    /// <summary>Cerca una parola dentro tutti i PDF di una cartella; dai risultati
    /// si apre il documento alla pagina giusta.</summary>
    [RelayCommand]
    private void SearchInFolder() => ShowFolderSearch(null);

    /// <summary>Con <paramref name="folder"/> valorizzato la cartella è già
    /// scelta: è la voce "Cerca nei PDF di questa cartella" di Esplora risorse.</summary>
    public void ShowFolderSearch(string? folder)
    {
        var window = new FolderSearchWindow { Owner = Application.Current.MainWindow };
        window.OpenRequested += (path, page) =>
        {
            OpenPath(path);
            if (SelectedDocument is PdfDocumentViewModel pdf)
                pdf.GoToPage(page);
        };
        if (folder is not null)
            window.Preset(folder);
        window.Show();
    }

    [RelayCommand]
    private void OpenBatch() => ShowBatch(null);

    /// <summary>Con <paramref name="signedFolder"/> valorizzato si parte
    /// dall'estrazione dai file firmati di quella cartella.</summary>
    public void ShowBatch(string? signedFolder)
    {
        var window = new BatchWindow { Owner = Application.Current.MainWindow };
        if (signedFolder is not null)
            window.PresetSignedFolder(signedFolder);
        window.Show();
    }

    [RelayCommand]
    private void MergePdfs()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Scegli i PDF da unire (nell'ordine di selezione)",
        };
        if (openDialog.ShowDialog() != true)
            return;
        if (openDialog.FileNames.Length < 2)
        {
            ShowError("Per unire servono almeno due PDF.");
            return;
        }

        var saveDialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "unito.pdf" };
        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            PdfPageOperations.Merge(openDialog.FileNames, saveDialog.FileName);
            OpenPath(saveDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Unione non riuscita:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Unisce più PDF aggiungendo segnalibri e una pagina di indice cliccabile: su venti
    /// allegati, unire e basta produce un malloppo in cui nessuno ritrova più niente.
    /// </summary>
    [RelayCommand]
    private void BuildDossier()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Scegli i documenti del fascicolo (nell'ordine in cui andranno)",
        };
        if (openDialog.ShowDialog() != true)
            return;
        if (openDialog.FileNames.Length < 2)
        {
            ShowError("Per fare un fascicolo servono almeno due PDF.");
            return;
        }

        var saveDialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "fascicolo.pdf" };
        if (saveDialog.ShowDialog() != true)
            return;

        var withIndex = MessageBox.Show(
            "Vuoi una pagina di indice all'inizio, con i titoli cliccabili?\n\n" +
            "I segnalibri vengono aggiunti in ogni caso.",
            "Fascicolo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        try
        {
            var result = PdfDossierBuilder.Build(openDialog.FileNames, saveDialog.FileName, withIndex);
            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n\n", result.Warnings), "Fascicolo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenPath(saveDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError($"Fascicolo non riuscito:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Compila un modulo PDF una volta per ogni riga di una tabella. Alla fine dice cosa non
    /// si è abbinato: un modulo compilato a metà senza dirlo è peggio di uno non compilato.
    /// </summary>
    [RelayCommand]
    private void FillFormsFromTable()
    {
        var formDialog = new OpenFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Title = "Scegli il modulo PDF da compilare",
        };
        if (formDialog.ShowDialog() != true)
            return;

        var dataDialog = new OpenFileDialog
        {
            Filter = "Tabelle (*.csv;*.xlsx)|*.csv;*.xlsx|Tutti i file|*.*",
            Title = "Scegli la tabella dei dati (la prima riga sono i nomi delle colonne)",
        };
        if (dataDialog.ShowDialog() != true)
            return;

        var folderDialog = new OpenFolderDialog { Title = "Dove scrivere i documenti compilati" };
        if (folderDialog.ShowDialog() != true)
            return;

        try
        {
            var result = FormBatchFiller.Fill(formDialog.FileName, dataDialog.FileName,
                folderDialog.FolderName);

            var message = $"Prodotti {result.Documents.Count} documenti.";
            if (result.Warnings.Count > 0)
                message += "\n\n" + string.Join("\n\n", result.Warnings);
            MessageBox.Show(message, "Moduli in serie", MessageBoxButton.OK,
                result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Compilazione non riuscita:\n{ex.Message}");
        }
    }

    private System.Windows.Threading.DispatcherTimer? _watchTimer;
    private WatchedFolder? _watchedFolder;

    /// <summary>Vero mentre una cartella è sorvegliata: serve alla voce di menu per dire
    /// «ferma» invece di «sorveglia».</summary>
    [ObservableProperty]
    private string _watchStatus = string.Empty;

    /// <summary>
    /// Sorveglia una cartella: i PDF che arrivano vengono lavorati e spostati in «fatti» o
    /// «non riusciti».
    /// <para>
    /// Guarda <b>solo finché il programma è aperto</b>, e lo dice: far credere a una
    /// sorveglianza continua sarebbe la bugia più comoda e più dannosa, perché chi lascia i
    /// file la sera si aspetta di ritrovarli lavorati la mattina.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void WatchFolder()
    {
        if (_watchTimer is not null)
        {
            StopWatching();
            MessageBox.Show("Sorveglianza fermata.", "TrameEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var folderDialog = new OpenFolderDialog { Title = "Quale cartella sorvegliare" };
        if (folderDialog.ShowDialog() != true)
            return;

        var recipe = new BatchRecipe(RunOcr: false, Redact: false, Compress: true,
            ProtectPassword: null, ConvertToPdfA: false);

        var confirm = MessageBox.Show(
            $"I PDF che compaiono in:\n{folderDialog.FolderName}\n\n" +
            "verranno compressi e spostati in «fatti» (o in «non riusciti» se qualcosa va " +
            "storto), con un registro in registro.csv.\n\n" +
            "La cartella viene guardata soltanto finché TrameEditor resta aperto: " +
            "chiudendo il programma, la sorveglianza si ferma.\n\nProcedo?",
            "Cartella sorvegliata", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (confirm != MessageBoxResult.OK)
            return;

        var layout = WatchedFolderLayout.Under(folderDialog.FolderName);
        _watchedFolder = new WatchedFolder(layout, (source, outputFolder) =>
        {
            var result = BatchProcessor.ProcessFile(source, outputFolder, recipe);
            return new WatchedFileOutcome(Path.GetFileName(source), result.Success,
                result.Outcome, result.OutputPath, DateTime.UtcNow);
        });

        _watchTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _watchTimer.Tick += (_, _) => RunWatchPass(layout);
        _watchTimer.Start();
        WatchStatus = $"Sorveglio {Path.GetFileName(folderDialog.FolderName)}";
        RunWatchPass(layout);
    }

    private void RunWatchPass(WatchedFolderLayout layout)
    {
        if (_watchedFolder is null)
            return;
        try
        {
            var outcomes = _watchedFolder.ProcessReady();
            if (outcomes.Count > 0)
                WatchStatus = $"Sorveglio {Path.GetFileName(layout.Inbox)} — " +
                              $"ultimi {outcomes.Count} file lavorati";
        }
        catch (Exception ex)
        {
            StopWatching();
            ShowError($"Sorveglianza interrotta:\n{ex.Message}");
        }
    }

    private void StopWatching()
    {
        _watchTimer?.Stop();
        _watchTimer = null;
        _watchedFolder = null;
        WatchStatus = string.Empty;
    }

    /// <summary>
    /// Divide una stampa cumulativa dove cambia il valore letto in un'area della pagina.
    /// L'area si indica a mano in punti PDF: segnarla col mouse richiederebbe una finestra
    /// di disegno, che non c'è ancora.
    /// </summary>
    [RelayCommand]
    private void SplitBatchPrint()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Title = "Scegli la stampa cumulativa da dividere",
        };
        if (openDialog.ShowDialog() != true)
            return;

        var area = TextPromptDialog.Ask(Application.Current.MainWindow, "Dividi una stampa",
            "Area in cui leggere il valore su cui dividere (il numero del documento),\n" +
            "scritta come sinistra,base,larghezza,altezza in punti PDF\n" +
            "(origine in basso a sinistra, foglio A4 = 595 x 842):",
            "410,725,160,25");
        if (string.IsNullOrWhiteSpace(area))
            return;

        var parts = area.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts.All(p => double.TryParse(p,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            ShowError("L'area vuole quattro numeri separati da virgola, col punto come " +
                      "separatore decimale.");
            return;
        }

        var folderDialog = new OpenFolderDialog { Title = "Dove scrivere i documenti separati" };
        if (folderDialog.ShowDialog() != true)
            return;

        try
        {
            var numbers = parts.Select(p => double.Parse(p,
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var field = new TemplateField("valore", 1, numbers[0], numbers[1], numbers[2], numbers[3]);
            var result = PdfSplitter.Split(openDialog.FileName, field, folderDialog.FolderName);

            var message = $"Prodotti {result.Parts.Count} documenti.";
            if (result.Warnings.Count > 0)
                message += "\n\n" + string.Join("\n\n", result.Warnings);
            MessageBox.Show(message, "Dividi una stampa", MessageBoxButton.OK,
                result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Divisione non riuscita:\n{ex.Message}");
        }
    }

    private bool SaveDocument(DocumentTabViewModel document) => document switch
    {
        TextDocumentViewModel text => text.FilePath is null
            ? SaveTextAs(text)
            : TrySaveText(text, text.FilePath),
        PdfDocumentViewModel pdf => pdf.SaveAs(),
        _ => false,
    };

    private bool SaveTextAs(TextDocumentViewModel document)
    {
        var dialog = new SaveFileDialog
        {
            Filter = TextSaveFilter,
            FileName = document.FileName,
            DefaultExt = ".txt",
        };
        if (dialog.ShowDialog() != true || !TrySaveText(document, dialog.FileName))
            return false;
        RegisterRecent(dialog.FileName);
        return true;
    }

    private static bool TrySaveText(TextDocumentViewModel document, string path)
    {
        try
        {
            document.SaveTo(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Impossibile salvare \"{path}\":\n{ex.Message}");
            return false;
        }
    }

    private bool ConfirmClose(DocumentTabViewModel document)
    {
        if (!document.IsDirty)
            return true;

        SelectedDocument = document;
        var result = MessageBox.Show(
            $"Salvare le modifiche a \"{document.FileName}\"?",
            "TrameEditor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(document),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>Chiamato alla chiusura della finestra: false annulla la chiusura.</summary>
    public bool ConfirmCloseAll()
    {
        foreach (var document in Documents.ToList())
        {
            if (!ConfirmClose(document))
                return false;
        }
        return true;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Error);
}
