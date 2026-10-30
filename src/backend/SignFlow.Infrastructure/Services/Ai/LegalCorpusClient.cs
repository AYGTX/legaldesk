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

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync($"{opts.Value.BaseUrl.TrimEnd('/')}/health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default)
    {
        var settings = opts.Value;
        // Approved rule versions only: articles and model clauses a lawyer has not approved never reach a contract (E1).
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/api/v1/playbook");
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
            body.Rules
                .Where(r => r.Value.ModelClause is { Fr.Length: > 0 })
                .ToDictionary(r => r.Key, r => new ModelClause(r.Value.ModelClause!.Fr, r.Value.ModelClause.En)),
            body.Rules.ToDictionary(r => r.Key, r => new RuleApproval(r.Value.Version, r.Value.ApprovedBy, r.Value.ApprovedAt)));
    }

    public async Task<HarveyReview> ReviewAsync(
        IReadOnlyList<ClauseText> clauses,
        string? language,
        IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides,
        CancellationToken ct = default)
    {
        var body = new
        {
            clauses = clauses.Select(c => new { reference = c.Reference, text = c.Text }),
            language,
            disabled_rules = disabledRules,
            severity_overrides = severityOverrides
        };
        var result = await PostAsync<ReviewOut>("api/v1/review", body, ct);
        return new HarveyReview(result.Language, result.GridVersion, result.Findings
            .Select(f => new HarveyFinding(f.RuleKey, f.ClauseType, f.Severity, f.Explanation, f.RecommendedAction,
                f.ContractReference, f.FindingType, f.RuleVersion, f.Detection, f.Confidence, f.Quote))
            .ToList());
    }

    public async Task SendFeedbackAsync(ReviewFeedbackReport report, CancellationToken ct = default)
    {
        var body = new
        {
            source_ref = report.SourceRef,
            rule_key = report.RuleKey,
            rule_version = report.RuleVersion,
            clause_reference = report.ClauseReference,
            quote = report.Quote,
            reason = report.Reason
        };
        await PostAsync<JsonElement>("api/v1/review-feedback", body, ct);
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

    private sealed record PlaybookOut(Dictionary<string, PlaybookRuleOut> Rules);

    private sealed record PlaybookRuleOut(List<PlaybookArticleOut> Articles, string? Label = null, ModelClauseOut? ModelClause = null,
        int? Version = null, string? ApprovedBy = null, DateTime? ApprovedAt = null);

    private sealed record ModelClauseOut(string Fr, string? En);

    private sealed record PlaybookArticleOut(string ArticleId, string Citation, string Text, string? Why,
        Dictionary<string, string>? Explanations = null);

    private sealed record ReviewOut(string Language, string? Model, List<FindingOut> Findings, string? GridVersion = null);

    private sealed record FindingOut(string ClauseType, string Severity, string Explanation, string? RecommendedAction,
        string? ContractReference, string? RuleKey, string? FindingType, int? RuleVersion = null, string? Detection = null,
        double? Confidence = null, string? Quote = null);
}
