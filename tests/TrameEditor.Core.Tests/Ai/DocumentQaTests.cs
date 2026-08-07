using TrameEditor.Core.Ai;

namespace TrameEditor.Core.Tests.Ai;

public class DocumentQaTests
{
    [Fact]
    public void Chunker_OneChunkPerShortPage_SplitsLongPages()
    {
        var longText = string.Join("\n", Enumerable.Repeat("riga di testo abbastanza lunga per contare", 60));
        var chunks = DocumentChunker.Chunk([(1, "pagina breve"), (2, longText), (3, "")]);

        Assert.Equal(1, chunks.Count(c => c.Page == 1));
        Assert.True(chunks.Count(c => c.Page == 2) >= 2, "la pagina lunga va divisa");
        Assert.All(chunks, c => Assert.True(c.Text.Length <= DocumentChunker.MaxChunkChars + 100));
        Assert.DoesNotContain(chunks, c => c.Page == 3); // pagina vuota: nessun blocco
    }

    [Fact]
    public void LexicalRetriever_RanksRelevantChunkFirst()
    {
        var chunks = new List<DocChunk>
        {
            new(1, "Premessa generale del contratto e definizioni delle parti"),
            new(2, "Il canone mensile è di 150 euro. Il pagamento del canone avviene a 60 giorni."),
            new(3, "Foro competente e legge applicabile"),
        };

        var top = LexicalRetriever.TopK(chunks, "quanto costa il canone mensile?", 2);

        Assert.Equal(2, top[0].Page);
    }

    [Fact]
    public void LexicalRetriever_NoMatches_FallsBackToFirstChunks()
    {
        var chunks = new List<DocChunk> { new(1, "alfa"), new(2, "beta"), new(3, "gamma") };
        var top = LexicalRetriever.TopK(chunks, "zzz qqq www", 2);
        Assert.Equal(2, top.Count);
        Assert.Equal(1, top[0].Page);
    }

    [Fact]
    public void Cosine_BasicProperties()
    {
        float[] a = [1, 0, 0];
        Assert.Equal(1.0, EmbeddingMath.Cosine(a, [1, 0, 0]), 3);
        Assert.Equal(0.0, EmbeddingMath.Cosine(a, [0, 1, 0]), 3);
        Assert.True(EmbeddingMath.Cosine(a, [1, 1, 0]) is > 0.6 and < 0.8);
    }

    [Fact]
    public void PromptBuilder_ContainsContextPagesAndRules()
    {
        var user = QaPromptBuilder.BuildUserPrompt("quanto devo pagare?",
            [new DocChunk(2, "canone 150 euro"), new DocChunk(5, "pagamento a 60 giorni")]);
        Assert.Contains("[pag. 2]", user);
        Assert.Contains("[pag. 5]", user);
        Assert.Contains("quanto devo pagare?", user);

        var system = QaPromptBuilder.BuildSystemPrompt();
        Assert.Contains("SOLO", system);
        Assert.Contains("Non lo trovo nel documento", system);
    }

    [Fact]
    public void ModelPicker_PrefersKnownChatModels_AndFindsEmbeddings()
    {
        var models = new[] { "nomic-embed-text:latest", "llama3.2:3b", "qwen2.5:3b-instruct" };
        Assert.Equal("qwen2.5:3b-instruct", OllamaModels.PickChatModel(models));
        Assert.Equal("nomic-embed-text:latest", OllamaModels.PickEmbeddingModel(models));

        Assert.Null(OllamaModels.PickChatModel(["nomic-embed-text:latest"]));
        Assert.Null(OllamaModels.PickEmbeddingModel(["llama3.2:3b"]));
        Assert.Equal("modello-esotico:7b", OllamaModels.PickChatModel(["modello-esotico:7b"]));
    }
}
