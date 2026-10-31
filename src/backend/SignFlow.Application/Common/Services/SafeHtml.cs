using Ganss.Xss;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// The one allow-list for HTML that people write and others read (R-M6-3, R-M9-7): template bodies and clause bodies
/// are cleaned when they are saved, so nothing stored can run in a reader's browser or make the PDF renderer fetch a URL.
/// Kept: headings, paragraphs, lists, tables, bold, italic, underline, line breaks, spans and divs with a class, and the
/// {{placeholders}} and {{#if}} markers, which are plain text. Gone: scripts, event handlers, iframes, forms, images
/// (an external image is a fetch), links, and every style property but a few layout ones (url() in a style is a fetch).
/// </summary>
public static class SafeHtml
{
    private static readonly HashSet<string> CodeElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "textarea", "select", "button", "form"
    };

    private static readonly HtmlSanitizer Sanitizer = Build();

    private static HtmlSanitizer Build()
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "h1", "h2", "h3", "h4", "h5", "h6", "p", "br", "hr", "div", "span", "blockquote",
                "ul", "ol", "li", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
                "b", "strong", "i", "em", "u", "s", "sub", "sup", "small"
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // class for the contract theme; colspan/rowspan for tables; data-clause-* marks a locked library clause.
                "class", "style", "colspan", "rowspan", "data-clause-id", "data-clause-version", "data-locked"
            },
            // Layout only: none of these can carry a url().
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "text-align", "font-weight", "font-style", "text-decoration", "text-indent", "margin-left", "padding-left"
            },
            AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
        var sanitizer = new HtmlSanitizer(options);
        sanitizer.AllowedAtRules.Clear();
        // Removed elements take their content with them only when the content is code; a stray <font> keeps its words.
        sanitizer.KeepChildNodes = true;
        sanitizer.RemovingTag += (_, e) =>
        {
            if (CodeElements.Contains(e.Tag.LocalName)) e.Tag.TextContent = string.Empty;
        };
        return sanitizer;
    }

    /// <summary>The cleaned HTML; null and blank stay as they are.</summary>
    public static string Clean(string? html) =>
        string.IsNullOrWhiteSpace(html) ? html ?? string.Empty : Sanitizer.Sanitize(html);
}
