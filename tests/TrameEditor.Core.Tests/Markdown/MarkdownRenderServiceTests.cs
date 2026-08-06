using TrameEditor.Core.Markdown;

namespace TrameEditor.Core.Tests.Markdown;

public class MarkdownRenderServiceTests
{
    [Fact]
    public void RenderFragment_BasicElements()
    {
        var html = MarkdownRenderService.RenderFragment("# Titolo\n\n**grassetto** e *corsivo* e `codice`");

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>grassetto</strong>", html);
        Assert.Contains("<em>corsivo</em>", html);
        Assert.Contains("<code>codice</code>", html);
    }

    [Fact]
    public void RenderFragment_PipeTables_ViaAdvancedExtensions()
    {
        var html = MarkdownRenderService.RenderFragment("| a | b |\n|---|---|\n| 1 | 2 |");
        Assert.Contains("<table>", html);
    }

    [Fact]
    public void RenderDocument_IsSelfContainedHtmlPage()
    {
        var html = MarkdownRenderService.RenderDocument("ciao *mondo*", "Prova & titolo");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>Prova &amp; titolo</title>", html);
        Assert.Contains("<em>mondo</em>", html);
        Assert.Contains("<style>", html);
    }

    [Fact]
    public void RenderFragment_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderService.RenderFragment(""));
    }
}
