using System.Text.Json;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The mapper stands between a language model's free-form reply and a compliance verdict that gates
/// whether a contract may be sent, so the failure modes it has to absorb — a skipped regulation, an
/// index that was never issued, a status nobody defined — are the specification rather than a smoke
/// check. Everything is anchored to a fixed timestamp so no case depends on the clock.
/// </summary>
public class ComplianceResponseMapperTests
{
    private static readonly DateTime AnalyzedAt = new(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly RegulatoryTextSummary Gdpr =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "GDPR", "personal data,processing", "EU 2016/679");

    private static readonly RegulatoryTextSummary DataProtection =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Loi organique 2004-63", "données personnelles", null);

    private static readonly RegulatoryTextSummary Consumer =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Code de la consommation", "consommateur", null);

    private static ComplianceAnalysis Map(string json, params RegulatoryTextSummary[] regulations)
    {
        using var doc = JsonDocument.Parse(json);
        return ComplianceResponseMapper.FromModelResponse(doc.RootElement, regulations, AnalyzedAt);
    }

    // ------------------------------------------------------------------ index mapping

    [Fact]
    public void Maps_the_one_based_index_back_to_the_regulation_it_was_issued_for()
    {
        var result = Map("""
            { "findings": [
                { "regulationIndex": 2, "status": "Addressed", "explanation": "Article 7 covers it." }
            ] }
            """, Gdpr, DataProtection);

        var mapped = result.Findings.Single(f => f.Status == "Addressed");
        Assert.Equal(DataProtection.Id, mapped.RegulationId);
        Assert.Equal("Loi organique 2004-63", mapped.RegulationTitle);
        Assert.Equal("Article 7 covers it.", mapped.Explanation);
    }

    [Fact]
    public void Accepts_the_index_as_a_string_because_models_are_inconsistent_about_it()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": "1", "status": "Addressed", "explanation": "Covered." }] }
            """, Gdpr);

        Assert.Equal("Addressed", result.Findings.Single().Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Drops_a_finding_whose_index_was_never_issued(int index)
    {
        var result = Map($$"""
            { "findings": [{ "regulationIndex": {{index}}, "status": "Addressed", "explanation": "Covered." }] }
            """, Gdpr, DataProtection);

        // Both regulations fall through to Unclear rather than one of them absorbing the stray finding.
        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.Equal("Unclear", f.Status));
    }

    [Fact]
    public void Keeps_the_first_answer_when_the_model_returns_an_index_twice()
    {
        var result = Map("""
            { "findings": [
                { "regulationIndex": 1, "status": "Addressed", "explanation": "First." },
                { "regulationIndex": 1, "status": "Gap", "explanation": "Second." }
            ] }
            """, Gdpr);

        Assert.Equal("First.", result.Findings.Single().Explanation);
    }

    // ------------------------------------------------------------------ completeness

    [Fact]
    public void Returns_every_regulation_sent_marking_the_skipped_ones_unclear()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": 1, "status": "Addressed", "explanation": "Covered." }] }
            """, Gdpr, DataProtection, Consumer);

        Assert.Equal(3, result.Findings.Count);
        Assert.Equal([Gdpr.Id, DataProtection.Id, Consumer.Id], result.Findings.Select(f => f.RegulationId));
        Assert.Equal("Addressed", result.Findings[0].Status);
        Assert.Equal("Unclear", result.Findings[1].Status);
        Assert.Equal("Unclear", result.Findings[2].Status);
    }

    [Fact]
    public void Treats_a_reply_with_no_findings_array_as_nothing_assessed()
    {
        var result = Map("""{ "limitations": [] }""", Gdpr, DataProtection);

        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.Equal("Unclear", f.Status));
    }

    // ------------------------------------------------------------------ status + severity

    [Theory]
    [InlineData("Addressed", "Addressed")]
    [InlineData("addressed", "Addressed")]
    [InlineData("  GAP  ", "Gap")]
    [InlineData("Unclear", "Unclear")]
    [InlineData("PartiallyAddressed", "Unclear")]
    [InlineData("", "Unclear")]
    public void Normalises_the_status_to_one_of_the_three_verdicts(string reported, string expected)
    {
        var result = Map($$"""
            { "findings": [{ "regulationIndex": 1, "status": "{{reported}}", "explanation": "x" }] }
            """, Gdpr);

        Assert.Equal(expected, result.Findings.Single().Status);
    }

    [Fact]
    public void Keeps_severity_on_a_gap()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": 1, "status": "Gap", "severity": "critical", "explanation": "x" }] }
            """, Gdpr);

        Assert.Equal("Critical", result.Findings.Single().Severity);
    }

    [Fact]
    public void Drops_severity_on_anything_that_is_not_a_gap()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": 1, "status": "Addressed", "severity": "High", "explanation": "x" }] }
            """, Gdpr);

        // A severity chip next to an addressed regulation would read as a problem in the UI.
        Assert.Null(result.Findings.Single().Severity);
    }

    [Fact]
    public void Drops_a_severity_that_is_not_on_the_scale()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": 1, "status": "Gap", "severity": "Catastrophic", "explanation": "x" }] }
            """, Gdpr);

        Assert.Null(result.Findings.Single().Severity);
    }

    [Fact]
    public void Carries_the_reasoning_fields_through()
    {
        var result = Map("""
            { "findings": [{
                "regulationIndex": 1, "status": "Gap", "severity": "High", "explanation": "No DPA.",
                "whyItMatters": "Exposes the business to sanction.",
                "recommendedAction": "Add a data-processing clause.",
                "proposedLanguage": "The Processor shall...",
                "contractReference": "Article 9"
            }] }
            """, Gdpr);

        var finding = result.Findings.Single();
        Assert.Equal("Exposes the business to sanction.", finding.WhyItMatters);
        Assert.Equal("Add a data-processing clause.", finding.RecommendedAction);
        Assert.Equal("The Processor shall...", finding.ProposedLanguage);
        Assert.Equal("Article 9", finding.ContractReference);
    }

    [Fact]
    public void Leaves_absent_and_blank_reasoning_fields_null()
    {
        var result = Map("""
            { "findings": [{ "regulationIndex": 1, "status": "Gap", "explanation": "x", "whyItMatters": "   " }] }
            """, Gdpr);

        var finding = result.Findings.Single();
        Assert.Null(finding.WhyItMatters);
        Assert.Null(finding.RecommendedAction);
        Assert.Null(finding.ProposedLanguage);
    }

    // ------------------------------------------------------------------ score

    [Fact]
    public void Scores_the_percentage_of_regulations_addressed()
    {
        var result = Map("""
            { "findings": [
                { "regulationIndex": 1, "status": "Addressed", "explanation": "x" },
                { "regulationIndex": 2, "status": "Addressed", "explanation": "x" },
                { "regulationIndex": 3, "status": "Gap", "explanation": "x" }
            ] }
            """, Gdpr, DataProtection, Consumer);

        Assert.Equal(67, result.ComplianceScore);
    }

    [Fact]
    public void Ignores_a_score_the_model_volunteered()
    {
        var result = Map("""
            { "complianceScore": 100,
              "findings": [{ "regulationIndex": 1, "status": "Gap", "explanation": "x" }] }
            """, Gdpr);

        // The score gates the readiness verdict, so it has to follow from the findings on screen.
        Assert.Equal(0, result.ComplianceScore);
    }

    [Fact]
    public void Counts_an_unclear_regulation_against_the_score()
    {
        var result = Map("""
            { "findings": [
                { "regulationIndex": 1, "status": "Addressed", "explanation": "x" },
                { "regulationIndex": 2, "status": "Unclear", "explanation": "x" }
            ] }
            """, Gdpr, DataProtection);

        Assert.Equal(50, result.ComplianceScore);
    }

    [Fact]
    public void Scores_a_contract_with_nothing_to_check_against_as_fully_compliant()
    {
        var result = Map("""{ "findings": [] }""");

        Assert.Empty(result.Findings);
        Assert.Equal(100, result.ComplianceScore);
    }

    // ------------------------------------------------------------------ limitations

    [Fact]
    public void Carries_limitations_through()
    {
        var result = Map("""
            { "limitations": ["Governing law is not stated."],
              "findings": [{ "regulationIndex": 1, "status": "Gap", "explanation": "x" }] }
            """, Gdpr);

        Assert.Equal(["Governing law is not stated."], result.Limitations);
    }

    [Fact]
    public void Leaves_limitations_null_when_absent_or_empty()
    {
        Assert.Null(Map("""{ "findings": [] }""", Gdpr).Limitations);
        Assert.Null(Map("""{ "limitations": ["", "  "], "findings": [] }""", Gdpr).Limitations);
    }

    // ------------------------------------------------------------------ prompt numbering

    [Fact]
    public void Numbers_regulations_from_one_so_the_reply_can_be_mapped_back()
    {
        var formatted = ComplianceResponseMapper.FormatRegulations([Gdpr, DataProtection]);

        Assert.Contains("1. GDPR", formatted);
        Assert.Contains("2. Loi organique 2004-63", formatted);
        Assert.Contains("Reference: EU 2016/679", formatted);
        Assert.Contains("Subject matter: personal data,processing", formatted);
    }

    [Fact]
    public void Omits_a_reference_line_for_a_regulation_that_has_none()
    {
        var formatted = ComplianceResponseMapper.FormatRegulations([DataProtection]);

        Assert.DoesNotContain("Reference:", formatted);
    }
}
