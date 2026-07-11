using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Ai;

public class GroqAiService(
    IOptions<GroqSettings> opts,
    IHttpClientFactory httpFactory,
    ILogger<GroqAiService> logger) : IAiService
{
    private const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const int MaxInputChars = 12_000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default)
    {
        var settings = opts.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new GeneratedHtmlTemplate("<p>AI not configured.</p>", "Untitled Template", []);

        var truncated = userText.Length > MaxInputChars ? userText[..MaxInputChars] : userText;

        const string systemPrompt = """
            You are an expert HTML form designer for a digital signature platform.
            The user will describe or paste a form/document they want to create.

            Your job: generate a clean, well-structured HTML fragment (no <html>/<body> tags)
            that represents the document, plus a list of interactive fields.

            Rules for placeholders:
            - Use {{camelCaseKey}} for every field the initiator (sender) fills in before sending.
            - Use {{approverSignature}} (or similar) only for signature fields the approver places.
            - Field keys must be camelCase, no spaces, no special chars.
            - Initiator fields: text inputs (name, date, department, reason, etc.)
            - Approver fields: only signatures (type="Signature", role="Approver")

            HTML style rules:
            - Use semantic HTML: <h1>/<h2> for titles, <p> for body, <table> for grids.
            - Wrap each field label+placeholder in a <div class="form-row">.
            - Show the placeholder as styled text: <span class="field-value">{{key}}</span>
            - Keep it clean and printable — no form inputs, no buttons.

            You MUST respond with ONLY valid JSON in this exact schema (no markdown, no explanation):
            {
              "suggestedName": "Short descriptive name for this template",
              "htmlContent": "...the HTML string...",
              "fields": [
                {
                  "fieldKey": "employeeName",
                  "label": "Employee Name",
                  "type": "Text",
                  "role": "Initiator",
                  "isRequired": true,
                  "order": 1,
                  "autoFillFrom": "user.fullName"
                }
              ]
            }

            Valid type values: Text, Textarea, Date, Number, Checkbox, Signature
            Valid role values: Initiator, Approver
            Valid autoFillFrom values: user.fullName, user.firstName, user.lastName, user.email, user.company, null
            """;

        var body = new
        {
            model = settings.Model,
            max_tokens = 2048,
            temperature = 0.4,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = truncated }
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var client = httpFactory.CreateClient("groq");
        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Groq API error {Status}: {Body}", response.StatusCode, err);
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        // Strip markdown code fences if the model wraps its JSON
        content = content.Trim();
        if (content.StartsWith("```")) content = content[(content.IndexOf('\n') + 1)..];
        if (content.EndsWith("```")) content = content[..content.LastIndexOf("```")];
        content = content.Trim();

        try
        {
            using var parsed = JsonDocument.Parse(content);
            var root = parsed.RootElement;
            var htmlContent = root.GetProperty("htmlContent").GetString() ?? string.Empty;
            var suggestedName = root.TryGetProperty("suggestedName", out var n) ? n.GetString() ?? "Generated Template" : "Generated Template";

            var fields = new List<GeneratedHtmlTemplateField>();
            if (root.TryGetProperty("fields", out var fieldsEl))
            {
                int order = 1;
                foreach (var f in fieldsEl.EnumerateArray())
                {
                    fields.Add(new GeneratedHtmlTemplateField(
                        f.TryGetProperty("fieldKey", out var fk) ? fk.GetString() ?? "" : "",
                        f.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
                        f.TryGetProperty("type", out var tp) ? tp.GetString() ?? "Text" : "Text",
                        f.TryGetProperty("role", out var rl) ? rl.GetString() ?? "Initiator" : "Initiator",
                        f.TryGetProperty("isRequired", out var ir) && ir.GetBoolean(),
                        f.TryGetProperty("order", out var ord) ? ord.GetInt32() : order++,
                        f.TryGetProperty("autoFillFrom", out var af) && af.ValueKind != JsonValueKind.Null ? af.GetString() : null));
                }
            }

            return new GeneratedHtmlTemplate(htmlContent, suggestedName, fields);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse AI-generated HTML template JSON");
            return new GeneratedHtmlTemplate(content, "Generated Template", []);
        }
    }

    public async Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default)
    {
        var settings = opts.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogWarning("Groq ApiKey is not configured — returning empty summary");
            return string.Empty;
        }

        var truncated = text.Length > MaxInputChars ? text[..MaxInputChars] + "…" : text;

        var body = new
        {
            model = settings.Model,
            max_tokens = settings.MaxTokens,
            temperature = 0.3,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a document analyst. Summarize the following document in 3-5 concise sentences. Focus on: what the signer is agreeing to, key parties involved, important dates or deadlines, and any notable obligations or restrictions. Be neutral and factual. Do not invent details not present in the text."
                },
                new
                {
                    role = "user",
                    content = $"Document title: {title}\n\n{truncated}"
                }
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var client = httpFactory.CreateClient("groq");
        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Groq API error {Status}: {Body}", response.StatusCode, err);
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
