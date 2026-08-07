using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace TrameEditor.Core.Ai;

/// <summary>
/// Client minimale per Ollama (LLM locali, http://localhost:11434): elenco
/// modelli, embedding, chat in streaming. Tutte le chiamate restano sulla
/// macchina dell'utente.
/// </summary>
public sealed class OllamaClient
{
    public const string DefaultBaseUrl = "http://localhost:11434";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _baseUrl;

    public OllamaClient(string baseUrl = DefaultBaseUrl) => _baseUrl = baseUrl.TrimEnd('/');

    /// <summary>Nomi dei modelli installati; lancia se Ollama non risponde (3 s).</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await Http.GetAsync($"{_baseUrl}/api/tags", timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var models = new List<string>();
        foreach (var model in json.RootElement.GetProperty("models").EnumerateArray())
            models.Add(model.GetProperty("name").GetString() ?? string.Empty);
        return models;
    }

    public async Task<float[]> EmbedAsync(string model, string text,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var payload = JsonSerializer.Serialize(new { model, prompt = text });
        using var response = await Http.PostAsync($"{_baseUrl}/api/embeddings",
            new StringContent(payload, Encoding.UTF8, "application/json"), timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return json.RootElement.GetProperty("embedding").EnumerateArray()
            .Select(v => (float)v.GetDouble()).ToArray();
    }

    /// <summary>Chat in streaming: restituisce i frammenti di risposta man mano.</summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(string model,
        string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            options = new { temperature = 0.1 },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await Http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                var delta = content.GetString();
                if (!string.IsNullOrEmpty(delta))
                    yield return delta;
            }
            if (json.RootElement.TryGetProperty("done", out var done) && done.GetBoolean())
                yield break;
        }
    }
}

/// <summary>Scelta dei modelli tra quelli installati, per preferenza.</summary>
public static class OllamaModels
{
    private static readonly string[] ChatPreference =
        ["qwen3", "qwen2.5", "qwen", "llama3.2", "llama3.1", "llama3", "gemma3", "gemma2", "gemma", "mistral", "phi"];

    private static readonly string[] EmbeddingMarkers = ["embed", "minilm", "bge", "e5"];

    public static string? PickChatModel(IReadOnlyList<string> models)
    {
        var candidates = models.Where(m => !IsEmbeddingModel(m)).ToList();
        foreach (var preferred in ChatPreference)
        {
            var match = candidates.FirstOrDefault(m =>
                m.StartsWith(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return candidates.FirstOrDefault();
    }

    public static string? PickEmbeddingModel(IReadOnlyList<string> models) =>
        models.FirstOrDefault(IsEmbeddingModel);

    private static bool IsEmbeddingModel(string name) =>
        EmbeddingMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
