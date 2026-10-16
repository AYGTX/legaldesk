using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Infrastructure.Services.Ai.Llm;

namespace SignFlow.Infrastructure.Services.Ai;

/// <summary>
/// The AI features, independent of which model runs them: <see cref="ILlmProvider"/> is the strategy (Groq, a
/// local model, or none). Every model-backed method falls back to its rules-based version when the provider is
/// not configured or fails.
/// </summary>
public class LlmAiService(
    ILlmProvider llm,
    ILogger<LlmAiService> logger) : IAiService
{
    private const int MaxInputChars = 12_000;

    private Task<string> ChatAsync(string systemPrompt, string userMessage, LlmTask task, double temperature, CancellationToken ct, int? maxTokens = null) =>
        llm.CompleteAsync(new LlmRequest(systemPrompt, userMessage, task, temperature, maxTokens), ct);

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
        if (!llm.IsConfigured)
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
            await ChatAsync(systemPrompt, truncated, LlmTask.Quick, 0.4, ct, maxTokens: 2048));

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
        if (!llm.IsConfigured)
        {
            logger.LogWarning("No language model configured ({Provider}) — returning empty summary", llm.Name);
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
            LlmTask.Quick,
            0.3,
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
        if (!llm.IsConfigured)
        {
            logger.LogWarning("No language model configured ({Provider}) — falling back to the rules-based contract scan", llm.Name);
            return await FallbackAnalysisAsync(text, title, referenceDate, ct);
        }

        try
        {
            var maxChars = llm.AnalysisMaxInputChars;
            var truncated = text.Length > maxChars ? text[..maxChars] + "…" : text;

            var content = StripCodeFences(await ChatAsync(
                ContractAnalystSystemPrompt,
                $"Contract title: {title}\nToday's date: {referenceDate:yyyy-MM-dd}\n\n{truncated}",
                LlmTask.Analysis,
                0.2,
                ct));

            var result = ParseAnalysis(content, text.Length > maxChars);

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
            ContractLanguage.Detect(text) == "fr"
                ? "L'analyse juridique complète n'était pas disponible : une analyse par mots-clés a été utilisée à la place. Elle ne détecte que les schémas de risque courants et ne remplace pas une revue juridique."
                : "The full legal analysis was unavailable — a rules-based keyword scan was used instead. It detects common risk patterns only and is not a substitute for legal review."
        };

        return new ContractAnalysisResult(risk with { Limitations = limitations }, obligation);
    }

    private const string ComplianceAnalystSystemPrompt = """
        You are an expert regulatory compliance analyst working inside a contract management
        platform. You are given a contract and a numbered list of regulatory texts that the legal
        team tracks. For each regulatory text, decide whether the contract substantively satisfies
        what that text requires.

        CORE PRINCIPLES
        - Judge substance, not vocabulary. A contract can satisfy a regulation without using its
          terminology, and can repeat its terminology while satisfying none of it.
        - The "subject matter" words supplied with each regulation describe what it is about. They
          are topic hints for you, NOT strings to search for. Never conclude "addressed" merely
          because one of those words appears in the contract.
        - Assess only against the regulatory texts supplied. Do not introduce other laws.
        - Never invent statutes, articles, regulations, case law or citations. If it is not in the
          supplied contract text or in the regulation entry, it does not exist.
        - Do not assume a requirement is met because a related topic is mentioned nearby.
        - "Unclear" is a legitimate and useful verdict. Use it when the contract touches the area
          but you cannot tell whether the requirement is met, or when the supplied extract does not
          contain enough to judge. An honest "unclear" is worth more than a confident guess.

        PROCESS
        For each numbered regulatory text:
        1. Establish what the text actually requires of a contract of this kind.
        2. Search the contract for provisions that would discharge that requirement.
        3. Decide:
           "Addressed" - the contract contains provisions that substantively meet the requirement.
           "Gap"       - the contract is silent, or its provisions fall short of the requirement.
           "Unclear"   - the contract is ambiguous, partial, or the supplied text is insufficient.
        4. For a gap, assign severity by the exposure it creates:
           "Critical" - unlawful, unenforceable, or exposing the business to sanction
           "High"     - significant regulatory or legal exposure
           "Medium"   - material shortfall that should normally be corrected
           "Low"      - improvement or clarification recommended

        OUTPUT
        Respond with ONLY valid JSON in exactly this schema. No markdown, no commentary.
        {
          "limitations": ["what this review could not establish and why it matters"],
          "findings": [
            {
              "regulationIndex": 1,
              "status": "Gap",
              "severity": "High",
              "explanation": "what the contract does or does not say about this requirement",
              "whyItMatters": "the regulatory or commercial consequence",
              "recommendedAction": "what the reader should do",
              "proposedLanguage": "clause text to insert, or null",
              "contractReference": "clause or article this relates to, or null",
              "articleIndex": 2,
              "quote": "the exact sentence of that legal article the finding relies on, or null"
            }
          ]
        }

        LEGAL ARTICLES
        You may also be given a list "LEGAL ARTICLES" numbered A1, A2, ... These are verified articles of
        law, word for word. They are the only law you may cite.
        - When a finding rests on one of them, set "articleIndex" to its number (2 for A2) and copy into
          "quote" the sentence you rely on, character for character, from that article. Do not translate,
          shorten, correct or paraphrase it. Every quote is checked against the article automatically and
          rejected if it does not match exactly.
        - If no supplied article supports the finding, set "articleIndex" and "quote" to null. Never cite an
          article from memory, and never cite one that was not supplied.

        OUTPUT RULES
        - "regulationIndex" is the number the regulation was listed under. Return one finding for
          EVERY numbered regulation, exactly once. Never invent an index that was not supplied.
        - Valid "status" values: Addressed, Gap, Unclear. Nothing else.
        - "severity" applies to a gap. Use null for "Addressed" and "Unclear".
        - Use null for anything you cannot determine. Never fill a field with a guess.
        - Provide "proposedLanguage" only where drafting language is appropriate. It must fit the
          rest of the contract and must not contradict its existing provisions. Write it in the same
          language as the contract.
        - Do not output a compliance score. The platform computes it from your findings.
        """;

    public async Task<ComplianceAnalysis> ReviewComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations,
        IReadOnlyList<LegalArticleContext>? legalArticles = null, CancellationToken ct = default)
    {
        // Nothing tracked applies to this contract — there is no question to put to the model.
        if (activeRegulations.Count == 0)
            return new ComplianceAnalysis(100, [], DateTime.UtcNow);

        if (!llm.IsConfigured)
        {
            logger.LogWarning("No language model configured ({Provider}) — falling back to the keyword compliance scan", llm.Name);
            return await FallbackComplianceAsync(text, title, activeRegulations, ct);
        }

        legalArticles ??= [];

        try
        {
            var maxChars = llm.AnalysisMaxInputChars;
            var truncated = text.Length > maxChars ? text[..maxChars] + "…" : text;

            var message = new StringBuilder()
                .AppendLine($"Contract title: {title}")
                .AppendLine()
                .AppendLine("REGULATORY TEXTS TO CHECK AGAINST:")
                .AppendLine(ComplianceResponseMapper.FormatRegulations(activeRegulations));

            if (legalArticles.Count > 0)
                message.AppendLine("LEGAL ARTICLES:").AppendLine(ComplianceResponseMapper.FormatArticles(legalArticles));

            var userMessage = message.AppendLine("CONTRACT:").AppendLine(truncated).ToString();

            var content = StripCodeFences(await ChatAsync(
                ComplianceAnalystSystemPrompt,
                userMessage,
                LlmTask.Analysis,
                0.2,
                ct));

            using var parsed = JsonDocument.Parse(content);
            var result = ComplianceResponseMapper.FromModelResponse(parsed.RootElement, activeRegulations, DateTime.UtcNow, legalArticles);

            // Every regulation coming back unassessed means the reply carried nothing usable —
            // a keyword scan at least says something about the contract.
            if (result.Findings.All(f => f.Status == ComplianceResponseMapper.StatusUnclear))
            {
                logger.LogWarning("Compliance review reached no verdict on any regulation — falling back to the keyword scan");
                return await FallbackComplianceAsync(text, title, activeRegulations, ct);
            }

            if (text.Length > maxChars)
            {
                var limitations = result.Limitations ?? [];
                limitations.Add("Only the first part of the contract was reviewed — it exceeds the length the analysis accepts. Provisions later in the document were not assessed.");
                result = result with { Limitations = limitations };
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compliance review failed — falling back to the keyword compliance scan");
            return await FallbackComplianceAsync(text, title, activeRegulations, ct);
        }
    }

    /// <summary>
    /// Keeps the compliance panel usable when the model is unreachable, unconfigured or returns
    /// something unparseable: the keyword scan runs instead, and says so.
    /// </summary>
    private async Task<ComplianceAnalysis> FallbackComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, CancellationToken ct)
    {
        var scan = await AnalyzeContractComplianceAsync(text, title, activeRegulations, ct);

        return scan with
        {
            Limitations =
            [
                ContractLanguage.Detect(text) == "fr"
                    ? "La revue de conformité complète n'était pas disponible : une analyse par mots-clés a été utilisée à la place. Elle vérifie seulement si les thèmes suivis pour chaque texte apparaissent dans le contrat, ce qui ne constitue pas un jugement de conformité."
                    : "The full compliance review was unavailable — a keyword scan was used instead. It only checks whether each regulation's tracked terms appear in the contract, which is not a judgement about whether the contract satisfies it."
            ]
        };
    }

    // ---------------------------------------------------------------------------------------
    // Rules-based scan — instant and offline. Runs at upload time so the risk card is populated
    // before anyone asks for the full review, and backs that review up when the model is unavailable.
    // ---------------------------------------------------------------------------------------

    public Task<ContractRiskAnalysis> AnalyzeContractRiskAsync(string text, string title, CancellationToken ct = default) =>
        Task.FromResult(ContractRuleEngine.Analyze(text));

    public Task<ComplianceAnalysis> AnalyzeContractComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, CancellationToken ct = default)
    {
        var findings = new List<ComplianceFinding>();
        var fr = ContractLanguage.Detect(text) == "fr";

        // Keyword match: no model, instant.
        foreach (var regulation in activeRegulations)
        {
            var keywords = regulation.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var matched = keywords.Where(k => k.Length > 0 && text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();

            findings.Add(matched.Count > 0
                ? new ComplianceFinding(regulation.Id, regulation.Title, "Addressed", fr
                    ? $"Le contrat aborde les thèmes suivis pour ce texte ({string.Join(", ", matched)}). Analyse automatique par mots-clés : cela ne prouve pas la conformité."
                    : $"The contract covers this text's tracked topics ({string.Join(", ", matched)}). Automated keyword scan: this does not prove compliance.")
                : new ComplianceFinding(regulation.Id, regulation.Title, "Gap", fr
                    ? $"Aucun des thèmes suivis pour ce texte n'apparaît dans le contrat ({string.Join(", ", keywords)}). Vérifiez si le contrat devrait les traiter."
                    : $"None of this text's tracked topics appear in the contract ({string.Join(", ", keywords)}). Check whether the contract should address them."));
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
