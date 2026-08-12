using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>Single round-trip to Groq's chat completions endpoint; returns the assistant message content.</summary>
    private async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model,
        int maxTokens,
        double temperature,
        string clientName,
        CancellationToken ct)
    {
        var body = new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.Value.ApiKey);

        using var client = httpFactory.CreateClient(clientName);
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

    /// <summary>Models sometimes wrap their JSON in a markdown fence despite being told not to.</summary>
    private static string StripCodeFences(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```")) content = content[(content.IndexOf('\n') + 1)..];
        if (content.EndsWith("```")) content = content[..content.LastIndexOf("```")];
        return content.Trim();
    }

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

        var content = StripCodeFences(
            await ChatAsync(systemPrompt, truncated, settings.Model, 2048, 0.4, "groq", ct));

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

        const string systemPrompt =
            "You are a document analyst. Summarize the following document in 3-5 concise sentences. " +
            "Focus on: what the signer is agreeing to, key parties involved, important dates or deadlines, " +
            "and any notable obligations or restrictions. Be neutral and factual. " +
            "Do not invent details not present in the text.";

        return await ChatAsync(
            systemPrompt,
            $"Document title: {title}\n\n{truncated}",
            settings.Model,
            settings.MaxTokens,
            0.3,
            "groq",
            ct);
    }

    // ---------------------------------------------------------------------------------------
    // Full legal review
    // ---------------------------------------------------------------------------------------

    private const string ContractAnalystSystemPrompt = """
        You are an expert legal contract analyst working inside a contract management platform.
        Your job is to find what is missing, incomplete, ambiguous, contradictory or legally
        risky in a contract, and to propose the contractual wording that addresses it.

        CORE PRINCIPLES
        - Analyse the contract as a legal professional would. Do NOT summarise it.
        - Look for what is ABSENT, not only for what is present.
        - Distinguish explicit facts in the contract, reasonable inferences, missing information,
          legal requirements and recommendations. Never present one as another.
        - Never invent facts, laws, regulations, case law, citations or contractual obligations.
          If you cannot point to it in the supplied text, it is not there.
        - Do not assume a clause is adequate merely because a related topic is mentioned.
        - Take account of contract type, parties, jurisdiction, subject matter, commercial
          context and applicable law.
        - If the jurisdiction or applicable law is not stated, record that in "limitations" and
          say that legal verification is required. Never guess it.

        PROCESS
        1. Identify the contract: type, parties and their roles, purpose, subject matter,
           jurisdiction, applicable law, duration, commercial context.
        2. Build the model of what a contract of this type would normally contain, considering:
           party identification, definitions, purpose and scope, services or deliverables,
           obligations of each party, responsibilities, deadlines, acceptance criteria, pricing,
           currency, payment terms, taxes, invoicing, late-payment consequences, term, renewal,
           termination for convenience, termination for breach, notice periods, consequences of
           termination, liability, limitation of liability, indemnification, insurance,
           confidentiality, intellectual property, ownership and licensing, data protection,
           security obligations, subcontracting, assignment, force majeure, warranties, service
           levels, support and maintenance, audit rights, compliance, dispute resolution,
           applicable law, jurisdiction, notices, amendment procedure, severability, entire
           agreement, waiver, survival, signature requirements.
           Judge which of these are actually relevant to THIS contract. Do not demand all of them.
        3. For each important clause determine: what it establishes, who is obligated, what
           triggers the obligation, the deadline, what happens if it is not fulfilled, whether
           exceptions are defined, whether the wording is precise, whether it is consistent with
           the other clauses, whether it creates unintended legal or commercial risk, and whether
           a related provision is missing.
        4. Detect gaps: missing clauses, definitions, obligations, conditions, deadlines,
           consequences, exceptions, protections, procedural mechanisms, evidence requirements,
           termination mechanisms, dispute mechanisms, or information needed for enforceability
           or practical execution. Do NOT report something as missing if another clause
           adequately addresses it.
        5. Detect weak or problematic clauses: ambiguous language, vague obligations, undefined
           terms, contradictory provisions, conflicting dates or obligations, unbalanced or
           one-sided provisions, unreasonable limitations, missing exceptions or enforcement
           mechanisms, undefined consequences, circular references, inconsistent party names,
           amounts, currencies or terminology, and references to clauses that do not exist.
        6. For every significant finding, consider how the counterparty could interpret or
           challenge the provision. Do not assume the reader's preferred interpretation is right.
        7. Reason internally as: Issue -> relevant rule or requirement -> evidence in the
           contract -> analysis -> risk -> recommendation. Output only the conclusion.
        8. Assign severity to each finding:
           "Critical" - potentially fundamental legal or commercial exposure
           "High"     - significant legal, financial, operational or enforceability risk
           "Medium"   - material weakness that should normally be addressed
           "Low"      - improvement or clarification recommended

        OUTPUT
        Respond with ONLY valid JSON in exactly this schema. No markdown, no commentary.
        {
          "profile": {
            "contractType": "e.g. Service agreement",
            "parties": "names and roles, as stated",
            "purpose": "one sentence",
            "jurisdiction": "as stated, else null",
            "governingLaw": "as stated, else null",
            "term": "duration/start/end as stated, else null"
          },
          "limitations": ["what the analysis could not establish and why it matters"],
          "riskScore": 0,
          "findings": [
            {
              "clauseType": "Limitation of liability",
              "findingType": "MissingClause",
              "severity": "High",
              "explanation": "what is missing or problematic",
              "whyItMatters": "the legal or commercial consequence",
              "recommendedAction": "what the reader should do",
              "proposedLanguage": "clause text to insert, or null",
              "contractReference": "clause or article this relates to, or null"
            }
          ],
          "obligation": {
            "dueDate": "YYYY-MM-DD or null",
            "obligationType": "Renewal | Expiry | Review | Payment | null",
            "rationale": "where in the contract this date comes from, or null"
          }
        }

        OUTPUT RULES
        - Valid "findingType" values: MissingClause, WeakClause, Ambiguity, Contradiction, Imbalance.
        - Use null for anything the contract does not state. Never fill a field with a guess.
        - Provide "proposedLanguage" only where drafting language is appropriate. It must be
          consistent with the rest of the contract and must not contradict existing provisions.
          Write it in the same language as the contract.
        - Never cite an article, statute, regulation or court decision that does not appear in
          the supplied text.
        - "riskScore" is 0-100 and reflects overall exposure; higher is riskier.
        - Order findings most severe first. Report the findings that matter — do not pad the list.
        - "obligation" is the next contractual deadline the owner must diary (renewal, expiry or
          review). Use null for dueDate if the contract states none.
        """;

    public async Task<ContractAnalysisResult> AnalyzeContractAsync(
        string text, string title, DateTime referenceDate, CancellationToken ct = default)
    {
        var settings = opts.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogWarning("Groq ApiKey is not configured — falling back to the rules-based contract scan");
            return await FallbackAnalysisAsync(text, title, referenceDate, ct);
        }

        try
        {
            var truncated = text.Length > settings.AnalysisMaxInputChars
                ? text[..settings.AnalysisMaxInputChars] + "…"
                : text;

            var content = StripCodeFences(await ChatAsync(
                ContractAnalystSystemPrompt,
                $"Contract title: {title}\nToday's date: {referenceDate:yyyy-MM-dd}\n\n{truncated}",
                settings.AnalysisModel,
                settings.AnalysisMaxTokens,
                0.2,
                "groq-analysis",
                ct));

            var result = ParseAnalysis(content, text.Length > settings.AnalysisMaxInputChars);

            if (result.Risk.Findings.Count == 0)
            {
                logger.LogWarning("Contract analysis returned no findings — falling back to the rules-based scan");
                return await FallbackAnalysisAsync(text, title, referenceDate, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Contract analysis failed — falling back to the rules-based scan");
            return await FallbackAnalysisAsync(text, title, referenceDate, ct);
        }
    }

    private static ContractAnalysisResult ParseAnalysis(string content, bool wasTruncated)
    {
        using var parsed = JsonDocument.Parse(content);
        var root = parsed.RootElement;

        ContractProfile? profile = null;
        if (root.TryGetProperty("profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.Object)
        {
            profile = new ContractProfile(
                ReadString(profileEl, "contractType"),
                ReadString(profileEl, "parties"),
                ReadString(profileEl, "purpose"),
                ReadString(profileEl, "jurisdiction"),
                ReadString(profileEl, "governingLaw"),
                ReadString(profileEl, "term"));
        }

        var limitations = new List<string>();
        if (root.TryGetProperty("limitations", out var limEl) && limEl.ValueKind == JsonValueKind.Array)
        {
            limitations.AddRange(limEl.EnumerateArray()
                .Select(l => l.GetString())
                .Where(l => !string.IsNullOrWhiteSpace(l))!);
        }

        if (wasTruncated)
            limitations.Add("The contract exceeded the analysis length limit and was truncated — the closing sections were not reviewed.");

        var findings = new List<RiskFinding>();
        if (root.TryGetProperty("findings", out var findingsEl) && findingsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in findingsEl.EnumerateArray())
            {
                var clauseType = ReadString(f, "clauseType");
                var explanation = ReadString(f, "explanation");
                if (clauseType is null || explanation is null) continue;

                findings.Add(new RiskFinding(
                    clauseType,
                    NormalizeSeverity(ReadString(f, "severity")),
                    explanation,
                    ReadString(f, "findingType"),
                    ReadString(f, "whyItMatters"),
                    ReadString(f, "recommendedAction"),
                    ReadString(f, "proposedLanguage"),
                    ReadString(f, "contractReference")));
            }
        }

        var score = root.TryGetProperty("riskScore", out var scoreEl) && scoreEl.TryGetInt32(out var s)
            ? Math.Clamp(s, 0, 100)
            : ScoreFromSeverities(findings);

        var obligation = new ObligationSuggestion(null, null, null);
        if (root.TryGetProperty("obligation", out var oblEl) && oblEl.ValueKind == JsonValueKind.Object)
        {
            var raw = ReadString(oblEl, "dueDate");
            DateTime? dueDate = raw is not null && DateTime.TryParse(
                raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDate)
                ? parsedDate
                : null;

            obligation = new ObligationSuggestion(dueDate, ReadString(oblEl, "obligationType"), ReadString(oblEl, "rationale"));
        }

        var risk = new ContractRiskAnalysis(score, findings, DateTime.UtcNow, profile,
            limitations.Count > 0 ? limitations : null);

        return new ContractAnalysisResult(risk, obligation);
    }

    /// <summary>Reads a string property, mapping JSON null, absent and blank all to null.</summary>
    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var s = value.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static string NormalizeSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => "Critical",
        "high" => "High",
        "medium" => "Medium",
        _ => "Low"
    };

    private static int ScoreFromSeverities(IEnumerable<RiskFinding> findings) =>
        Math.Min(100, findings.Sum(f => f.Severity switch
        {
            "Critical" => 40,
            "High" => 25,
            "Medium" => 12,
            _ => 0
        }));

    /// <summary>
    /// Keeps the analysis panel usable when the model is unreachable, unconfigured or returns
    /// something unparseable: the rules-based scan runs instead, and says so.
    /// </summary>
    private async Task<ContractAnalysisResult> FallbackAnalysisAsync(
        string text, string title, DateTime referenceDate, CancellationToken ct)
    {
        var risk = await AnalyzeContractRiskAsync(text, title, ct);
        var obligation = await SuggestObligationAsync(text, title, referenceDate, ct);

        var limitations = new List<string>
        {
            "The full legal analysis was unavailable — a rules-based keyword scan was used instead. It detects common risk patterns only and is not a substitute for legal review."
        };

        return new ContractAnalysisResult(risk with { Limitations = limitations }, obligation);
    }

    // ---------------------------------------------------------------------------------------
    // Rules-based scan — instant and offline. Runs at upload time so the risk card is populated
    // before anyone asks for the full review, and backs that review up when Groq is unavailable.
    // ---------------------------------------------------------------------------------------

    public Task<ContractRiskAnalysis> AnalyzeContractRiskAsync(string text, string title, CancellationToken ct = default)
    {
        var findings = new List<RiskFinding>();

        void FlagIfPresent(string[] triggers, string clauseType, string severity, string explanation)
        {
            if (triggers.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new RiskFinding(clauseType, severity, explanation));
        }

        FlagIfPresent(["unlimited liability", "sole discretion"], "Liability",
            "High", "Language suggesting unlimited liability or one-sided discretion was detected.");
        FlagIfPresent(["automatic renewal", "automatically renew", "tacit renewal"], "Auto-renewal",
            "Medium", "The contract appears to renew automatically unless actively cancelled.");
        FlagIfPresent(["indemnify", "hold harmless"], "Indemnification",
            "Medium", "An indemnification / hold-harmless clause was detected.");
        FlagIfPresent(["exclusiv", "non-compete"], "Exclusivity",
            "Medium", "An exclusivity or non-compete restriction was detected.");
        FlagIfPresent(["penalty", "liquidated damages"], "Penalty clause",
            "Medium", "A penalty or liquidated-damages clause was detected.");

        if (!text.Contains("terminat", StringComparison.OrdinalIgnoreCase))
            findings.Add(new RiskFinding("Termination rights", "High", "No clear termination clause found in an automated scan."));

        if (findings.Count == 0)
            findings.Add(new RiskFinding("General", "Low", "No high-risk patterns detected in an automated scan."));

        var score = Math.Min(100, findings.Sum(f => f.Severity == "High" ? 25 : f.Severity == "Medium" ? 12 : 0) + (findings.Count == 1 && findings[0].Severity == "Low" ? 15 : 0));

        return Task.FromResult(new ContractRiskAnalysis(score, findings, DateTime.UtcNow));
    }

    public Task<ComplianceAnalysis> AnalyzeContractComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, CancellationToken ct = default)
    {
        var findings = new List<ComplianceFinding>();

        // KEYWORD-BASED MATCH (no HTTP call, instant) — swap for an LLM call here
        foreach (var regulation in activeRegulations)
        {
            var keywords = regulation.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var addressed = keywords.Any(k => k.Length > 0 && text.Contains(k, StringComparison.OrdinalIgnoreCase));

            findings.Add(addressed
                ? new ComplianceFinding(regulation.Id, regulation.Title, "Addressed",
                    "Language matching this regulation's tracked keywords was found in an automated scan.")
                : new ComplianceFinding(regulation.Id, regulation.Title, "Gap",
                    "No language addressing this regulation's tracked keywords was found in an automated scan."));
        }

        var score = findings.Count == 0
            ? 100
            : (int)Math.Round(100.0 * findings.Count(f => f.Status == "Addressed") / findings.Count);

        return Task.FromResult(new ComplianceAnalysis(score, findings, DateTime.UtcNow));
    }

    private static readonly Regex DateLikePattern = new(
        @"\b(\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4}|(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default)
    {
        foreach (var (keyword, obligationType) in new[] { ("renew", "Renewal"), ("expir", "Expiry"), ("effective", "Effective date review") })
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var windowStart = Math.Max(0, idx - 100);
            var windowEnd = Math.Min(text.Length, idx + 100);
            var window = text[windowStart..windowEnd];

            var match = DateLikePattern.Match(window);
            if (match.Success && DateTime.TryParse(match.Value, out var parsedDate))
            {
                return Task.FromResult(new ObligationSuggestion(
                    parsedDate, obligationType,
                    $"Date extracted near a \"{keyword}\" mention in an automated scan."));
            }
        }

        return Task.FromResult(new ObligationSuggestion(
            referenceDate.AddYears(1), "Renewal review",
            "No explicit renewal/expiry date found in an automated scan — defaulting to a 1-year review based on standard contract term."));
    }
}
