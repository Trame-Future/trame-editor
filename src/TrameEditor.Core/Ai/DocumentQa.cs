using TrameEditor.Core.Pdf;

namespace TrameEditor.Core.Ai;

/// <summary>Un blocco di testo del documento, con la pagina di provenienza.</summary>
public sealed record DocChunk(int Page, string Text);

/// <summary>Spezza il testo del documento in blocchi per pagina; le pagine
/// lunghe vengono divise ai confini di riga.</summary>
public static class DocumentChunker
{
    public const int MaxChunkChars = 1500;

    public static List<DocChunk> Chunk(IReadOnlyList<(int Page, string Text)> pageTexts)
    {
        var chunks = new List<DocChunk>();
        foreach (var (page, text) in pageTexts)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.Length <= MaxChunkChars)
            {
                chunks.Add(new DocChunk(page, trimmed));
                continue;
            }
            var current = new System.Text.StringBuilder();
            foreach (var line in trimmed.Split('\n'))
            {
                if (current.Length > 0 && current.Length + line.Length + 1 > MaxChunkChars)
                {
                    chunks.Add(new DocChunk(page, current.ToString().Trim()));
                    current.Clear();
                }
                current.AppendLine(line);
            }
            if (current.Length > 0)
                chunks.Add(new DocChunk(page, current.ToString().Trim()));
        }
        return chunks;
    }
}

/// <summary>Recupero lessicale (fallback quando manca un modello di embedding):
/// punteggio = occorrenze dei termini della domanda nel blocco.</summary>
public static class LexicalRetriever
{
    public static IReadOnlyList<DocChunk> TopK(IReadOnlyList<DocChunk> chunks, string question, int k)
    {
        var terms = question.ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '?', '!', '\'', '"', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .Distinct()
            .ToList();

        var scored = chunks
            .Select(chunk =>
            {
                var haystack = chunk.Text.ToLowerInvariant();
                var score = terms.Sum(term => CountOccurrences(haystack, term));
                return (Chunk: chunk, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(k)
            .Select(x => x.Chunk)
            .ToList();

        return scored.Count > 0 ? scored : chunks.Take(k).ToList();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}

public static class EmbeddingMath
{
    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}

public static class QaPromptBuilder
{
    public static string BuildSystemPrompt() =>
        "Sei l'assistente documenti di TrameEditor. Rispondi in italiano, in modo breve e preciso, " +
        "usando SOLO le informazioni presenti nel CONTESTO fornito. " +
        "Cita sempre la pagina da cui prendi ogni informazione nel formato [pag. N]. " +
        "Se la risposta non è nel contesto, di' chiaramente: \"Non lo trovo nel documento.\" " +
        "Non inventare mai dati, importi o date.";

    public static string BuildUserPrompt(string question, IReadOnlyList<DocChunk> context)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("CONTESTO:");
        foreach (var chunk in context)
        {
            builder.AppendLine($"[pag. {chunk.Page}]");
            builder.AppendLine(chunk.Text);
            builder.AppendLine();
        }
        builder.AppendLine($"DOMANDA: {question}");
        return builder.ToString();
    }
}

/// <summary>
/// Sessione di domande su un documento: indicizza il testo (embedding locali se
/// disponibili, altrimenti recupero lessicale) e risponde in streaming citando
/// le pagine. Tutto passa da Ollama in locale: nessun dato lascia il computer.
/// </summary>
public sealed class QaSession
{
    private const int TopK = 5;
    private const int SmallDocumentChars = 6000;

    private readonly OllamaClient _client;
    private readonly string _chatModel;
    private readonly string? _embeddingModel;
    private List<DocChunk> _chunks = [];
    private List<float[]>? _vectors;
    private int _totalChars;

    public QaSession(OllamaClient client, string chatModel, string? embeddingModel)
    {
        _client = client;
        _chatModel = chatModel;
        _embeddingModel = embeddingModel;
    }

    public async Task InitializeAsync(string pdfPath, IProgress<string>? status,
        CancellationToken cancellationToken = default)
    {
        var pageTexts = new List<(int, string)>();
        using (var inspector = new PdfTextInspector(pdfPath))
        {
            for (var page = 1; page <= inspector.PageCount; page++)
            {
                var text = string.Join("\n", inspector.GetLines(page).Select(l => l.Text));
                pageTexts.Add((page, text));
            }
        }
        _chunks = DocumentChunker.Chunk(pageTexts);
        _totalChars = _chunks.Sum(c => c.Text.Length);
        if (_chunks.Count == 0)
            throw new InvalidOperationException(
                "Il documento non contiene testo leggibile (se è una scansione, esegui prima l'OCR).");

        _vectors = null;
        if (_embeddingModel is not null && _totalChars > SmallDocumentChars)
        {
            try
            {
                var vectors = new List<float[]>();
                for (var i = 0; i < _chunks.Count; i++)
                {
                    status?.Report($"indicizzo il documento… {i + 1}/{_chunks.Count}");
                    vectors.Add(await _client.EmbedAsync(_embeddingModel, _chunks[i].Text, cancellationToken));
                }
                _vectors = vectors;
            }
            catch
            {
                _vectors = null; // embedding non disponibili: si usa il recupero lessicale
            }
        }
    }

    public async Task<(IReadOnlyList<DocChunk> Context, IReadOnlyList<int> Pages)> SelectContextAsync(
        string question, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DocChunk> context;
        if (_totalChars <= SmallDocumentChars)
        {
            context = _chunks;
        }
        else if (_vectors is not null)
        {
            var questionVector = await _client.EmbedAsync(_embeddingModel!, question, cancellationToken);
            context = _chunks
                .Select((chunk, i) => (Chunk: chunk, Score: EmbeddingMath.Cosine(_vectors[i], questionVector)))
                .OrderByDescending(x => x.Score)
                .Take(TopK)
                .OrderBy(x => x.Chunk.Page)
                .Select(x => x.Chunk)
                .ToList();
        }
        else
        {
            context = LexicalRetriever.TopK(_chunks, question, TopK);
        }
        return (context, context.Select(c => c.Page).Distinct().OrderBy(p => p).ToList());
    }

    public IAsyncEnumerable<string> AskStreamAsync(string question, IReadOnlyList<DocChunk> context,
        CancellationToken cancellationToken = default) =>
        _client.ChatStreamAsync(_chatModel, QaPromptBuilder.BuildSystemPrompt(),
            QaPromptBuilder.BuildUserPrompt(question, context), cancellationToken);
}
