using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The guarantee under test: whatever model wrote the review, a finding only keeps a legal citation the corpus has
/// confirmed word for word. A made-up article index, a paraphrased quote, or a corpus that is down must never come
/// out looking like confirmed law.
/// </summary>
public class GroundedComplianceReviewTests
{
    private static readonly DateTime At = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static readonly RegulatoryTextSummary DataProtection =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Loi organique 2004-63", "données personnelles", null);

    private static readonly LegalArticleContext Art7 = new("a-7", "Loi organique n° 2004-63, art. 7", "Le traitement est soumis au consentement exprès.", 0.5);
    private static readonly LegalArticleContext Art9 = new("a-9", "Loi organique n° 2004-63, art. 9", "Le responsable prend les précautions nécessaires.", 0.4);

    private static ComplianceAnalysis Map(string json, params LegalArticleContext[] articles)
    {
        using var doc = JsonDocument.Parse(json);
        return ComplianceResponseMapper.FromModelResponse(doc.RootElement, [DataProtection], At, articles);
    }

    // ------------------------------------------------------------------ mapper

    [Fact]
    public void Maps_the_article_index_to_the_article_it_was_issued_for_and_marks_it_pending()
    {
        var result = Map("""
            { "findings": [ { "regulationIndex": 1, "status": "Gap", "severity": "High", "explanation": "No consent.",
                              "articleIndex": 2, "quote": "Le responsable prend les précautions nécessaires." } ] }
            """, Art7, Art9);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("a-9", finding.LegalArticleId);
        Assert.Equal(Art9.Citation, finding.LegalCitation);
        Assert.Equal(ComplianceResponseMapper.CitationPending, finding.CitationStatus);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("null")]
    public void An_article_index_that_was_never_issued_carries_no_citation(string index)
    {
        var result = Map($$"""
            { "findings": [ { "regulationIndex": 1, "status": "Gap", "explanation": "x", "articleIndex": {{index}}, "quote": "invented" } ] }
            """, Art7, Art9);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.LegalArticleId);
        Assert.Null(finding.LegalQuote);
        Assert.Null(finding.CitationStatus);
    }

    [Fact]
    public void Formats_articles_as_A_numbered_entries_on_one_line_each()
    {
        var formatted = ComplianceResponseMapper.FormatArticles([Art7, new LegalArticleContext("x", "Cit", "line one\nline two")]);

        Assert.Contains("A1. Loi organique n° 2004-63, art. 7", formatted);
        Assert.Contains("A2. Cit", formatted);
        Assert.Contains("line one line two", formatted);
    }

    // ------------------------------------------------------------------ end to end with fakes

    private sealed class FakeAi(string json) : IAiService
    {
        public Task<ContractAnswer> AnswerQuestionAsync(string text, string title, IReadOnlyList<ChatTurn> history, string question, string language, CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<LegalArticleContext>? ReceivedArticles { get; private set; }

        public Task<ComplianceAnalysis> ReviewComplianceAsync(string text, string title, IReadOnlyList<RegulatoryTextSummary> regs,
            IReadOnlyList<LegalArticleContext>? legalArticles = null, CancellationToken ct = default)
        {
            ReceivedArticles = legalArticles;
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(ComplianceResponseMapper.FromModelResponse(doc.RootElement, regs, At, legalArticles));
        }

        public Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ContractRiskAnalysis> AnalyzeContractRiskAsync(string text, string title, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime d, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ContractAnalysisResult> AnalyzeContractAsync(string text, string title, DateTime d, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ComplianceAnalysis> AnalyzeContractComplianceAsync(string text, string title, IReadOnlyList<RegulatoryTextSummary> r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCorpus(bool searchFails = false, bool checkFails = false) : ILegalCorpusClient
    {
        public Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default) => throw new NotSupportedException();
        public List<string> Queries { get; } = [];
        public List<CitationCheckRequest> Checked { get; } = [];
        public Task<IReadOnlyList<RiskFinding>> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LegalArticleContext>> SearchAsync(string query, int topK, CancellationToken ct = default)
        {
            Queries.Add(query);
            if (searchFails) throw new HttpRequestException("down");
            return Task.FromResult<IReadOnlyList<LegalArticleContext>>([Art7, Art9]);
        }

        public Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegalPlaybook(new Dictionary<string, IReadOnlyList<PlaybookArticle>>(), new Dictionary<string, string>()));

        public Task<ClauseClassification> ClassifyAsync(IReadOnlyList<ClauseText> clauses, CancellationToken ct = default) =>
            Task.FromResult(new ClauseClassification(false, []));

        public Task<IReadOnlyList<CitationCheckResult>> CheckCitationsAsync(IReadOnlyList<CitationCheckRequest> citations, CancellationToken ct = default)
        {
            if (checkFails) throw new HttpRequestException("down");
            Checked.AddRange(citations);
            var all = new[] { Art7, Art9 };
            return Task.FromResult<IReadOnlyList<CitationCheckResult>>(citations
                .Select(c =>
                {
                    var article = all.First(a => a.ArticleId == c.ArticleId);
                    var found = article.Text.Contains(c.Quote, StringComparison.Ordinal);
                    return new CitationCheckResult(found, found, found ? article.Citation : null, found ? null : "quote_not_in_article");
                })
                .ToList());
        }
    }

    private static GroundedComplianceReview Review(IAiService ai, ILegalCorpusClient corpus) =>
        new(ai, corpus, new LegalPlaybookService(corpus, NullLogger<LegalPlaybookService>.Instance), NullLogger<GroundedComplianceReview>.Instance);

    private const string Contract = """
        Article 1 - Objet. Le Prestataire fournit au Client une prestation de traitement de fichiers clients.

        Article 2 - Données personnelles. Le Prestataire traite les données personnelles pour le compte du Client.

        Signé.
        """;

    private const string ExactQuote = """
        { "findings": [ { "regulationIndex": 1, "status": "Gap", "severity": "High", "explanation": "No consent clause.",
                          "articleIndex": 1, "quote": "soumis au consentement exprès" } ] }
        """;

    private const string ParaphrasedQuote = """
        { "findings": [ { "regulationIndex": 1, "status": "Gap", "severity": "High", "explanation": "No consent clause.",
                          "articleIndex": 1, "quote": "requires the express consent of the person" } ] }
        """;

    [Fact]
    public async Task Gives_the_model_deduplicated_corpus_articles_and_confirms_an_exact_quote()
    {
        var ai = new FakeAi(ExactQuote);
        var corpus = new FakeCorpus();

        var result = await Review(ai, corpus).ReviewAsync(Contract, "Contrat", [DataProtection]);

        Assert.Equal(2, corpus.Queries.Count);
        Assert.Equal(2, result.RelevantLaw!.Count);
        Assert.Equal(["a-7", "a-9"], ai.ReceivedArticles!.Select(a => a.ArticleId));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(LegalCitationGuard.Verified, finding.CitationStatus);
        Assert.Equal("a-7", Assert.Single(corpus.Checked).ArticleId);
        Assert.Null(result.Limitations);
    }

    [Fact]
    public async Task Rejects_a_paraphrased_quote_and_says_so()
    {
        var result = await Review(new FakeAi(ParaphrasedQuote), new FakeCorpus()).ReviewAsync(Contract, "Contrat", [DataProtection]);

        Assert.Equal(LegalCitationGuard.Rejected, Assert.Single(result.Findings).CitationStatus);
        Assert.Contains(result.Limitations!, l => l.Contains("rejected"));
    }

    [Fact]
    public async Task A_corpus_that_is_down_for_the_check_leaves_citations_unconfirmed_never_verified()
    {
        var result = await Review(new FakeAi(ExactQuote), new FakeCorpus(checkFails: true)).ReviewAsync(Contract, "Contrat", [DataProtection]);

        Assert.Equal(LegalCitationGuard.Unverified, Assert.Single(result.Findings).CitationStatus);
        Assert.Contains(result.Limitations!, l => l.Contains("could not be reached"));
    }

    [Fact]
    public async Task A_corpus_that_is_down_for_search_still_runs_the_review_without_articles()
    {
        var ai = new FakeAi("""{ "findings": [ { "regulationIndex": 1, "status": "Addressed", "explanation": "ok" } ] }""");

        var result = await Review(ai, new FakeCorpus(searchFails: true)).ReviewAsync(Contract, "Contrat", [DataProtection]);

        Assert.Empty(ai.ReceivedArticles!);
        Assert.Equal("Addressed", Assert.Single(result.Findings).Status);
        Assert.Contains(result.Limitations!, l => l.Contains("unavailable"));
    }
}
