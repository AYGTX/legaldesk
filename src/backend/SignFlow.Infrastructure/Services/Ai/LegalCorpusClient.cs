using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Ai;

public class LegalCorpusClient(IHttpClientFactory httpFactory, IOptions<LegalCorpusSettings> opts) : ILegalCorpusClient
{
    public const string HttpClientName = "legal-corpus";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<LegalArticleContext>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        // Only verified, published law: include_unverified stays false on purpose.
        var hits = await PostAsync<List<SearchHit>>("api/v1/search", new { query, top_k = topK }, ct);
        return hits.Select(h => new LegalArticleContext(h.ArticleId, h.Citation, h.ArticleText, h.Score)).ToList();
    }

    public async Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default)
    {
        var settings = opts.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/api/v1/playbook?include_unconfirmed=true");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Add("X-API-Key", settings.ApiKey);

        using var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PlaybookOut>(Json, ct)
                   ?? throw new InvalidOperationException("Empty playbook from legal corpus.");

        return new LegalPlaybook(
            body.Rules.ToDictionary(
                r => r.Key,
                r => (IReadOnlyList<PlaybookArticle>)r.Value.Articles
                    .Select(a => new PlaybookArticle(a.ArticleId, a.Citation, a.Text, a.Why, a.Explanations)).ToList()),
            body.Regulations,
            body.Rules
                .Where(r => r.Value.Phrases is { Count: > 0 })
                .ToDictionary(r => r.Key, r => new PlaybookWording(r.Value.Label ?? r.Key, r.Value.Phrases!)),
            body.Rules
                .Where(r => r.Value.ModelClause is { Fr.Length: > 0 })
                .ToDictionary(r => r.Key, r => new ModelClause(r.Value.ModelClause!.Fr, r.Value.ModelClause.En)));
    }

    public async Task<IReadOnlyList<CitationCheckResult>> CheckCitationsAsync(
        IReadOnlyList<CitationCheckRequest> citations, CancellationToken ct = default)
    {
        var body = new { citations = citations.Select(c => new { article_id = c.ArticleId, quote = c.Quote }) };
        var results = await PostAsync<List<CitationOut>>("api/v1/citations/check-batch", body, ct);
        return results.Select(r => new CitationCheckResult(r.Found, r.IsVerified, r.Citation, r.Reason)).ToList();
    }

    public async Task<ClauseClassification> ClassifyAsync(IReadOnlyList<ClauseText> clauses, CancellationToken ct = default)
    {
        var body = new { clauses = clauses.Select(c => new { reference = c.Reference, text = c.Text }) };
        var result = await PostAsync<ClassifyOut>("api/v1/classify", body, ct);
        return new ClauseClassification(result.Enabled, result.Matches
            .Select(m => new ClauseMatch(m.Reference, m.RuleKey, m.Label, m.Score, m.Sentence, m.Example)).ToList());
    }

    public async Task<IReadOnlyList<RiskFinding>> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, CancellationToken ct = default)
    {
        var body = new { clauses = clauses.Select(c => new { reference = c.Reference, text = c.Text }), language };
        var result = await PostAsync<ReviewOut>("api/v1/review", body, ct);
        return result.Findings.Select(f => new RiskFinding(f.ClauseType, f.Severity, f.Explanation,
            FindingType: f.FindingType, RecommendedAction: f.RecommendedAction,
            ContractReference: f.ContractReference, RuleKey: f.RuleKey)).ToList();
    }

    public async Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        var settings = opts.Value;
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(pdf), "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/api/v1/forms/detect")
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Add("X-API-Key", settings.ApiKey);

        // Detection runs a vision model on every page; give it more time than a search.
        using var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DetectionOut>(Json, ct)
                   ?? throw new InvalidOperationException("Empty response from legal corpus forms/detect.");
        return new DetectedFormFields(body.Pages, body.Model, body.AiUsed, body.Fields
            .Select(f => new DetectedFormField(f.FieldKey, f.Label, f.Type, f.FillRole, f.Page, f.X, f.Y, f.Width, f.Height,
                f.IsRequired, f.AutoFillFrom, f.Options ?? [], f.Source, f.Confidence)).ToList());
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var settings = opts.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/{path}")
        {
            Content = JsonContent.Create(body, options: Json)
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Add("X-API-Key", settings.ApiKey);

        using var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new InvalidOperationException($"Empty response from legal corpus {path}.");
    }

    private sealed record DetectionOut(int Pages, string? Model, bool AiUsed, List<DetectedFieldOut> Fields);

    private sealed record DetectedFieldOut(string FieldKey, string Label, string Type, string FillRole, int Page, float X,
        float Y, float Width, float Height, bool IsRequired, string? AutoFillFrom, List<string>? Options, string Source,
        string Confidence);

    private sealed record SearchHit(string ArticleId, string ArticleText, string Citation, double Score);

    private sealed record PlaybookOut(Dictionary<string, PlaybookRuleOut> Rules, Dictionary<string, string> Regulations);

    private sealed record PlaybookRuleOut(List<PlaybookArticleOut> Articles, string? Label = null, List<string>? Phrases = null,
        ModelClauseOut? ModelClause = null);

    private sealed record ModelClauseOut(string Fr, string? En);

    private sealed record PlaybookArticleOut(string ArticleId, string Citation, string Text, string? Why,
        Dictionary<string, string>? Explanations = null);

    private sealed record CitationOut(bool Found, bool IsVerified, string? Citation, string? Reason);

    private sealed record ReviewOut(string Language, string? Model, List<FindingOut> Findings);

    private sealed record FindingOut(string ClauseType, string Severity, string Explanation, string? RecommendedAction,
        string? ContractReference, string? RuleKey, string? FindingType);

    private sealed record ClassifyOut(bool Enabled, string? Model, List<ClauseMatchOut> Matches);

    private sealed record ClauseMatchOut(string Reference, string RuleKey, string Label, double Score, string Sentence, string Example);
}
