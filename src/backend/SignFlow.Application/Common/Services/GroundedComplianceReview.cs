using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

public interface IGroundedComplianceReview
{
    Task<ComplianceAnalysis> ReviewAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> regulations, CancellationToken ct = default);
}

/// <summary>
/// The compliance review with the law attached. Each clause of the contract is matched to verified articles from the
/// legal corpus by search alone, so the "relevant law" map exists with or without a language model. When a model
/// runs, it is given the best of those articles, and every article it cites is checked word for word against the
/// corpus before the result is kept. Without the corpus (disabled or down) the review runs as before and says so.
/// </summary>
public class GroundedComplianceReview(
    IAiService aiService,
    ILegalCorpusClient corpus,
    ILegalPlaybookService playbook,
    ILogger<GroundedComplianceReview> logger) : IGroundedComplianceReview
{
    public const int MaxArticles = 12;
    public const int MaxClauses = 40;
    public const int ArticlesPerClause = 3;
    // Below this the match is mostly shared filler words; showing it would put unrelated law next to a clause.
    public const double MinScore = 0.15;
    private const int MinClauseChars = 60;

    public async Task<ComplianceAnalysis> ReviewAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> regulations, CancellationToken ct = default)
    {
        List<ClauseLawMatch> relevantLaw = [];
        var corpusDown = false;

        if (corpus.IsEnabled)
        {
            try
            {
                relevantLaw = await MapClausesAsync(text, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Legal corpus search failed — reviewing without article text");
                corpusDown = true;
            }
        }

        var result = await aiService.ReviewComplianceAsync(text, title, regulations, BestArticles(relevantLaw), ct);

        var checks = LegalCitationGuard.PendingChecks(result);
        if (checks.Count > 0)
        {
            try
            {
                var outcomes = await corpus.CheckCitationsAsync([.. checks.Select(c => c.Request)], ct);
                result = LegalCitationGuard.Apply(result, checks, outcomes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Legal corpus citation check failed — citations left unconfirmed");
                result = LegalCitationGuard.MarkUnchecked(result);
            }
        }

        if (corpusDown)
        {
            var limitations = result.Limitations?.ToList() ?? [];
            limitations.Add("The legal corpus was unavailable, so the contract was not matched to the text of the law.");
            result = result with { Limitations = limitations };
        }

        result = await playbook.AttachToComplianceAsync(result, regulations, ct);

        return result with { RelevantLaw = relevantLaw.Count > 0 ? relevantLaw : null };
    }

    /// <summary>One search per substantive clause; clauses with no article above <see cref="MinScore"/> are left out.</summary>
    private async Task<List<ClauseLawMatch>> MapClausesAsync(string text, CancellationToken ct)
    {
        var matches = new List<ClauseLawMatch>();

        foreach (var clause in ContractRuleEngine.SplitClauses(text).Where(c => c.Length >= MinClauseChars).Take(MaxClauses))
        {
            var articles = (await corpus.SearchAsync(clause, ArticlesPerClause, ct))
                .Where(a => a.Score >= MinScore)
                .ToList();

            if (articles.Count > 0)
                matches.Add(new ClauseLawMatch(clause, articles));
        }

        return matches;
    }

    /// <summary>The strongest distinct articles across all clauses: the law the model is allowed to cite.</summary>
    public static IReadOnlyList<LegalArticleContext> BestArticles(IEnumerable<ClauseLawMatch> matches) =>
        matches.SelectMany(m => m.Articles)
            .GroupBy(a => a.ArticleId)
            .Select(g => g.MaxBy(a => a.Score)!)
            .OrderByDescending(a => a.Score)
            .Take(MaxArticles)
            .ToList();
}
