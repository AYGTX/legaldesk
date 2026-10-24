using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Turns a template snapshot plus the values the initiator filled in into the final contract body.
///
/// This is the <b>single</b> renderer for template-based contracts. It exists because the signing
/// screen and the generated PDF used to run their own copies of the substitution logic: any drift
/// between them meant the signed PDF could say something the signer never saw, which in an
/// e-signature product is a legal problem rather than a cosmetic one. Both paths now call this.
///
/// Like the readiness evaluator, this is a deterministic rule set with no I/O — the same inputs
/// always produce the same bytes, and every decision can be explained line by line.
/// </summary>
public static class HtmlContractRenderer
{
    // {{#if cond}} body {{#else}} alternative {{/if}} — Singleline so a block can span newlines,
    // lazy so consecutive blocks don't swallow each other.
    private static readonly Regex ConditionalBlock = new(
        @"\{\{#if\s+([^}]+?)\}\}(.*?)(?:\{\{#else\}\}(.*?))?\{\{/if\}\}",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    // Anything conditional the block matcher could not pair up (a stray {{/if}}, a malformed
    // condition). Stripped at the very end so a broken marker never reaches the reader.
    private static readonly Regex StrayConditionalMarker = new(
        @"\{\{#if[^}]*\}\}|\{\{#else\}\}|\{\{/if\}\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Condition = new(
        @"^(\w+)\s*(==|!=|>=|<=|>|<)\s*(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Renders <paramref name="html"/> (the document's immutable snapshot) against
    /// <paramref name="filledValues"/>.
    ///
    /// <paramref name="fieldTypes"/> maps a field key to how its value should be compared. It is
    /// optional: when a template has been deleted since the request was raised, every field is
    /// treated as text, which keeps old contracts rendering rather than failing.
    ///
    /// <paramref name="signatureSlots"/> maps a signature placeholder key to the HTML that stands in for the signer's
    /// signature at that spot; without an entry the placeholder leaves no trace, as before.
    ///
    /// Order matters. Conditions resolve first, so the surviving branch still holds live
    /// placeholders; substitution runs second; leftovers are stripped last.
    /// </summary>
    public static string Render(
        string? html,
        IReadOnlyDictionary<string, string>? filledValues,
        IReadOnlyDictionary<string, HtmlTemplateFieldType>? fieldTypes = null,
        IReadOnlyDictionary<string, string>? signatureSlots = null)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var values = filledValues ?? new Dictionary<string, string>();
        var types = fieldTypes ?? new Dictionary<string, HtmlTemplateFieldType>();

        var result = ResolveConditionals(html, values, types);

        // An answer wins over a signature slot of the same name; a signature placeholder becomes the HTML the caller
        // supplied for it (already safe — it is built from ids and encoded names, never from template input).
        result = Placeholder.Replace(result, m =>
        {
            var key = m.Groups[1].Value;
            if (values.TryGetValue(key, out var v)) return WebUtility.HtmlEncode(v);
            if (signatureSlots is not null && signatureSlots.TryGetValue(key, out var slot)) return slot;
            return m.Value;
        });

        // Remaining {{placeholders}} are approver signature fields and auto-fill keys that were
        // never filled — they are structure, not content, so they leave no trace.
        result = Placeholder.Replace(result, string.Empty);

        return StrayConditionalMarker.Replace(result, string.Empty);
    }

    /// <summary>
    /// The template as one reader of every possible contract would see it, for review while the template is still being
    /// written: no answers exist yet, so every conditional branch is kept (a clause hidden behind a condition is still a
    /// clause to review) and each placeholder shows as «its label», which reads as a gap to fill instead of vanishing.
    /// Signature placeholders leave no trace, as in <see cref="Render"/>. Never used to build a real contract.
    /// </summary>
    public static string RenderForReview(string? html, IReadOnlyDictionary<string, string> labels)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var result = ConditionalBlock.Replace(html, m =>
            m.Groups[3].Success ? $"{m.Groups[2].Value}\n{m.Groups[3].Value}" : m.Groups[2].Value);

        result = Placeholder.Replace(result, m =>
            labels.TryGetValue(m.Groups[1].Value, out var label) ? $"«{WebUtility.HtmlEncode(label)}»" : string.Empty);

        return StrayConditionalMarker.Replace(result, string.Empty);
    }

    /// <summary>
    /// Keeps or drops each conditional block. A malformed or unsatisfiable condition keeps the
    /// <c>{{#else}}</c> branch (or drops the block when there is none) — see <see cref="Evaluate"/>
    /// for why failing to the negative branch is the deliberate choice.
    /// </summary>
    private static string ResolveConditionals(
        string html,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, HtmlTemplateFieldType> types)
        => ConditionalBlock.Replace(html, m =>
        {
            var kept = Evaluate(m.Groups[1].Value.Trim(), values, types)
                ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value : string.Empty;
            return kept;
        });

    /// <summary>
    /// Evaluates one condition against the filled values.
    ///
    /// Deliberately tiny: a single comparison, no <c>AND</c>/<c>OR</c>, no nesting, no arithmetic.
    /// A legal reviewer has to be able to read a condition and know what it does; a template that
    /// seems to need a bigger language needs another field instead.
    ///
    /// Anything that cannot be evaluated with confidence — unknown key, empty value, a number that
    /// will not parse — returns <c>false</c> rather than guessing. The verdict stays reproducible,
    /// and a clause is never included on the strength of a value nobody understood.
    /// </summary>
    internal static bool Evaluate(
        string condition,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, HtmlTemplateFieldType> types)
    {
        var match = Condition.Match(condition);
        var key = match.Success ? match.Groups[1].Value : condition;

        if (!values.TryGetValue(key, out var actual) || actual is null)
            return false;

        var type = types.TryGetValue(key, out var t) ? t : HtmlTemplateFieldType.Text;

        // Bare {{#if key}} — "is this filled in / ticked?".
        if (!match.Success)
            return type == HtmlTemplateFieldType.Checkbox
                ? IsTrue(actual)
                : !string.IsNullOrWhiteSpace(actual);

        var op = match.Groups[2].Value;
        var expected = Unquote(match.Groups[3].Value.Trim());

        return type switch
        {
            HtmlTemplateFieldType.Number => CompareNumbers(actual, expected, op),
            HtmlTemplateFieldType.Date => CompareDates(actual, expected, op),
            HtmlTemplateFieldType.Checkbox => CompareEquality(IsTrue(actual) == IsTrue(expected), op),
            // Ordering two pieces of prose has no meaning a lawyer would recognise, so text
            // supports equality only; a relational operator on text is treated as unsatisfiable.
            HtmlTemplateFieldType.Text or HtmlTemplateFieldType.Textarea or HtmlTemplateFieldType.Dropdown =>
                CompareEquality(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase), op),
            // A signature is not a condition input.
            _ => false
        };
    }

    private static bool CompareNumbers(string actual, string expected, string op)
        => decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
           && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var b)
           && Compare(a.CompareTo(b), op);

    private static bool CompareDates(string actual, string expected, string op)
        => DateTime.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
           && DateTime.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)
           && Compare(a.CompareTo(b), op);

    private static bool Compare(int comparison, string op) => op switch
    {
        "==" => comparison == 0,
        "!=" => comparison != 0,
        ">" => comparison > 0,
        ">=" => comparison >= 0,
        "<" => comparison < 0,
        "<=" => comparison <= 0,
        _ => false
    };

    private static bool CompareEquality(bool areEqual, string op) => op switch
    {
        "==" => areEqual,
        "!=" => !areEqual,
        _ => false
    };

    private static bool IsTrue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
           || value == "1";

    private static string Unquote(string value)
        => value.Length >= 2
           && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
