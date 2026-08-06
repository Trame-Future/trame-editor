using Markdig;

namespace TrameEditor.Core.Markdown;

public static class MarkdownRenderService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>Solo il corpo HTML, senza involucro di pagina.</summary>
    public static string RenderFragment(string markdown) =>
        Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

    /// <summary>Pagina HTML completa e auto-contenuta (CSS incorporato), per anteprima ed export.</summary>
    public static string RenderDocument(string markdown, string title)
    {
        var body = RenderFragment(markdown);
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        return $$"""
            <!DOCTYPE html>
            <html lang="it">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{safeTitle}}</title>
            <style>
            body { font-family: "Segoe UI", -apple-system, sans-serif; font-size: 16px; line-height: 1.6;
                   color: #1f2328; max-width: 52rem; margin: 0 auto; padding: 1.5rem 2rem 4rem; }
            h1, h2 { border-bottom: 1px solid #d8dee4; padding-bottom: .3em; }
            h1, h2, h3, h4, h5, h6 { margin-top: 1.4em; margin-bottom: .5em; line-height: 1.25; }
            a { color: #0b6bcb; }
            code { font-family: "Cascadia Mono", Consolas, monospace; font-size: .9em;
                   background: #f0f1f3; padding: .15em .35em; border-radius: 4px; }
            pre { background: #f6f8fa; padding: 1em; border-radius: 6px; overflow-x: auto; }
            pre code { background: none; padding: 0; }
            blockquote { margin: 0; padding: 0 1em; color: #59636e; border-left: .25em solid #d8dee4; }
            table { border-collapse: collapse; display: block; overflow-x: auto; }
            th, td { border: 1px solid #d8dee4; padding: .4em .8em; }
            th { background: #f6f8fa; }
            img { max-width: 100%; }
            hr { border: 0; border-top: 1px solid #d8dee4; margin: 1.5em 0; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
