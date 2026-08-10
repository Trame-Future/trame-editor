using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrameEditor.Core.Ai;
using TrameEditor.Core.Documents;

namespace TrameEditor.App.ViewModels;

/// <summary>
/// Il pannello "Chiedi al documento", indipendente dal tipo di documento: il
/// PDF gli passa le sue pagine, l'editor di testo le sue righe, e il resto —
/// ricerca di Ollama, indicizzazione, chat in streaming, citazioni — è lo stesso.
/// Chi lo ospita decide solo <see cref="ReadContent"/> e cosa fare quando si
/// clicca una citazione.
/// </summary>
public partial class DocumentQaViewModel : ObservableObject
{
    private readonly Func<DocumentContent> _readContent;
    private QaSession? _session;

    /// <summary>Invocato quando l'utente clicca una citazione: il PDF va alla
    /// pagina, l'editor va alla riga.</summary>
    public event Action<int>? ReferenceRequested;

    public DocumentQaViewModel(Func<DocumentContent> readContent)
    {
        _readContent = readContent;
    }

    /// <summary>Solo per il designer XAML.</summary>
    public DocumentQaViewModel() : this(() => new DocumentContent(DocumentUnit.Pagina, []))
    {
    }

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private bool _ready;

    [ObservableProperty]
    private bool _unavailable;

    /// <summary>"pag." oppure "riga": le citazioni devono dire la verità.</summary>
    [ObservableProperty]
    private string _referenceLabel = DocumentUnit.Pagina.ShortLabel();

    public ObservableCollection<QaMessageViewModel> Messages { get; } = [];

    public void RequestReference(int reference) => ReferenceRequested?.Invoke(reference);

    /// <summary>Il documento è cambiato: la sessione va ricostruita, altrimenti
    /// l'assistente risponderebbe su un testo che non esiste più.</summary>
    public void Invalidate(bool reinitializeNow)
    {
        _session = null;
        Ready = false;
        if (reinitializeNow)
            _ = InitializeAsync();
    }

    [RelayCommand]
    public async Task RetryAsync() => await InitializeAsync();

    public async Task InitializeAsync()
    {
        if (Busy)
            return;
        Unavailable = false;
        Ready = false;
        Busy = true;
        try
        {
            var endpoint = TrameEditor.Core.Session.AppSettings.Load().OllamaEndpoint;
            Status = $"cerco Ollama su {endpoint}…";
            var client = new OllamaClient(endpoint);
            IReadOnlyList<string> models;
            try
            {
                models = await client.ListModelsAsync();
            }
            catch
            {
                Unavailable = true;
                Status = $"Ollama non trovato su {endpoint}.\n\n" +
                    "Apri \"Impostazioni\" qui sotto: verifica se il tuo PC ha i requisiti " +
                    "e installa tutto automaticamente con un click. Poi premi Riprova.";
                return;
            }

            var chatModel = OllamaModels.PickChatModel(models);
            if (chatModel is null)
            {
                Unavailable = true;
                Status = "Ollama è attivo ma manca un modello di chat. " +
                    "Apri \"Impostazioni\" qui sotto e usa l'installazione automatica. Poi premi Riprova.";
                return;
            }

            var progress = new Progress<string>(s => Status = s);
            var session = new QaSession(new OllamaClient(endpoint), chatModel,
                OllamaModels.PickEmbeddingModel(models));
            var content = _readContent();
            await Task.Run(() => session.InitializeAsync(content, progress));
            _session = session;
            ReferenceLabel = session.Unit.ShortLabel();
            Ready = true;
            Status = $"pronto — modello locale: {chatModel}";
        }
        catch (Exception ex)
        {
            Unavailable = true;
            Status = $"Assistente non disponibile: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        var question = Input.Trim();
        if (question.Length == 0 || !Ready || Busy || _session is null)
            return;

        Input = string.Empty;
        Messages.Add(new QaMessageViewModel { IsUser = true, Text = question });
        var answer = new QaMessageViewModel { IsUser = false, Text = string.Empty };
        Messages.Add(answer);
        Busy = true;
        try
        {
            var session = _session;
            var (context, references) = await session.SelectContextAsync(question);
            foreach (var reference in references)
                answer.SourcePages.Add(reference);
            await foreach (var delta in session.AskStreamAsync(question, context))
                answer.Text += delta;
            if (answer.Text.Length == 0)
                answer.Text = "(nessuna risposta dal modello)";
        }
        catch (Exception ex)
        {
            answer.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }
}
