using Markdig;
using Microsoft.AspNetCore.Html;

namespace RestaurantReservation.Infrastructure;

public static class MarkdownExtensions
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static HtmlString ToHtml(this string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new HtmlString(string.Empty);
        }

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new HtmlString(html);
    }
}