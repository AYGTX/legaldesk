using System.Text.Json;
using SignFlow.Application.Common.Exceptions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Rules about the answers to a template's questions that hold for every kind of template and every way of giving
/// answers — creating a request, and revising it afterwards.
/// </summary>
public static class TemplateAnswerRules
{
    /// <summary>
    /// A dropdown can only be answered with one of its options. The form offers nothing else, but the API is open to
    /// anyone who can craft a request, and an answer outside the list would end up printed in a signed contract.
    /// A blank answer is left to the "required" rule.
    /// </summary>
    public static void RequireListedOptions(
        IEnumerable<(string Key, string Label, string Type, string? OptionsJson)> fields,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, label, type, optionsJson) in fields)
        {
            if (!string.Equals(type, "Dropdown", StringComparison.OrdinalIgnoreCase)) continue;
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;

            var options = Options(optionsJson);
            if (!options.Contains(value, StringComparer.Ordinal))
                throw new ValidationException($"'{value}' is not one of the choices for {label}: {string.Join(", ", options)}.");
        }
    }

    public static List<string> Options(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(optionsJson) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// The value of a request-level auto-fill source, or null when <paramref name="source"/> is not one of them. Shared
    /// by HTML and PDF templates so both offer the same sources.
    /// </summary>
    public static string? ResolveRequestSource(string? source, string requestTitle) => source switch
    {
        "system.today" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
        "system.now" => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        "request.title" => requestTitle,
        _ => null
    };
}
