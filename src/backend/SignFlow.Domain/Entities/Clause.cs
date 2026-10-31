using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A clause of the organisation's library (M9): what stays the same across its wordings — title, category, family,
/// guidance, the Harvey rules it answers. The wording itself lives in <see cref="ClauseVersion"/>s, which never change
/// once written (R-M9-1): editing an approved wording drafts the next version, and the approved one stays in use until
/// that draft is approved.
/// </summary>
public class Clause : TenantEntity
{
    public string Title { get; private set; } = string.Empty;
    public ClauseCategory Category { get; private set; }

    /// <summary>Internal legal note: when to use this clause, negotiation stance, fallback positions.</summary>
    public string? Guidance { get; private set; }

    /// <summary>Legal exposure this clause carries if accepted as drafted (R-M9-5).</summary>
    public ClauseRiskLevel? RiskLevel { get; private set; }

    /// <summary>How many times this clause has been inserted into a contract template.</summary>
    public int UsageCount { get; private set; }

    /// <summary>Optional scope: when set, the clause is only suggested for this contract family.</summary>
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    public ICollection<ClauseVersion> Versions { get; private set; } = [];
    public ICollection<ClauseRuleLink> RuleLinks { get; private set; } = [];

    private Clause() { }

    /// <summary>A new clause with its first wording, a draft by <paramref name="createdById"/>.</summary>
    public static Clause Create(
        string title,
        ClauseCategory category,
        string body,
        Guid createdById,
        string language = "en",
        string? guidance = null,
        ClauseRiskLevel? riskLevel = null,
        Guid? requestTypeId = null)
    {
        var clause = new Clause
        {
            Title = title,
            Category = category,
            CreatedById = createdById,
            Guidance = guidance,
            RiskLevel = riskLevel,
            RequestTypeId = requestTypeId
        };
        clause.Versions.Add(ClauseVersion.Draft(clause.Id, 1, body, language, createdById));
        return clause;
    }

    /// <summary>What describes the clause; the wording is changed with <see cref="Redraft"/>.</summary>
    public void Describe(string title, ClauseCategory category, string? guidance, ClauseRiskLevel? riskLevel, Guid? requestTypeId)
    {
        Title = title;
        Category = category;
        Guidance = guidance;
        RiskLevel = riskLevel;
        RequestTypeId = requestTypeId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The wording in use: the approved version, if there is one.</summary>
    public ClauseVersion? Approved => Versions.FirstOrDefault(v => v.Status == ClauseStatus.Approved);

    public ClauseVersion Latest => Versions.OrderByDescending(v => v.Number).First();

    /// <summary>
    /// New wording. A draft nobody has approved yet is still being written, so it is rewritten in place (its author is
    /// whoever wrote it last); otherwise the next version is drafted and the approved one is left untouched (R-M9-1).
    /// </summary>
    public ClauseVersion Redraft(string body, string language, Guid authorId)
    {
        var latest = Latest;
        if (latest.Status == ClauseStatus.Draft)
        {
            latest.Rewrite(body, language, authorId);
            UpdatedAt = DateTime.UtcNow;
            return latest;
        }

        var next = ClauseVersion.Draft(Id, latest.Number + 1, body, language, authorId);
        Versions.Add(next);
        UpdatedAt = DateTime.UtcNow;
        return next;
    }

    /// <summary>
    /// Approves a draft (R-M9-2): never by its author. The wording it replaces is deprecated: still readable and cited by
    /// the contracts that used it, never offered again.
    /// </summary>
    public void Approve(int number, Guid approverId)
    {
        var version = Versions.FirstOrDefault(v => v.Number == number)
            ?? throw new InvalidOperationException($"Version {number} does not exist.");
        if (version.Status != ClauseStatus.Draft)
            throw new InvalidOperationException("Only a draft can be approved.");
        if (version.AuthorId == approverId)
            throw new InvalidOperationException("The author of a wording cannot approve it.");

        foreach (var previous in Versions.Where(v => v.Status == ClauseStatus.Approved))
            previous.Deprecate();
        version.Approve(approverId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// For wording already approved before the library kept versions (the seeded library, the migration): the first
    /// version is marked approved with no recorded approver. Never used for a wording written in the application.
    /// </summary>
    public void ImportApproved()
    {
        var first = Versions.OrderBy(v => v.Number).First();
        if (first.Status == ClauseStatus.Draft) first.MarkImportedApproved();
    }

    public void Deprecate(int number)
    {
        var version = Versions.FirstOrDefault(v => v.Number == number)
            ?? throw new InvalidOperationException($"Version {number} does not exist.");
        version.Deprecate();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The Harvey rules this clause answers (R-M9-3): a finding on one of them proposes this clause first.</summary>
    /// <returns>The links added.</returns>
    public IReadOnlyList<ClauseRuleLink> LinkRules(IEnumerable<string> ruleKeys)
    {
        var wanted = ruleKeys.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var gone in RuleLinks.Where(l => !wanted.Contains(l.RuleKey)).ToList())
            RuleLinks.Remove(gone);
        var added = wanted.Where(k => RuleLinks.All(l => l.RuleKey != k)).Select(k => new ClauseRuleLink(Id, k)).ToList();
        foreach (var link in added) RuleLinks.Add(link);
        UpdatedAt = DateTime.UtcNow;
        return added;
    }

    public void RecordUsage()
    {
        UsageCount++;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>One wording of a clause (R-M9-1). Once approved it never changes: contracts and templates cite it by number.</summary>
public class ClauseVersion : TenantEntity
{
    public Guid ClauseId { get; private set; }
    public int Number { get; private set; }

    /// <summary>Sanitised HTML (R-M9-7).</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>"en" or "fr".</summary>
    public string Language { get; private set; } = "en";
    public ClauseStatus Status { get; private set; } = ClauseStatus.Draft;
    public Guid AuthorId { get; private set; }
    public Guid? ApprovedById { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? DeprecatedAt { get; private set; }

    private ClauseVersion() { }

    internal static ClauseVersion Draft(Guid clauseId, int number, string body, string language, Guid authorId) => new()
    {
        ClauseId = clauseId,
        Number = number,
        Body = body,
        Language = language,
        AuthorId = authorId
    };

    internal void Rewrite(string body, string language, Guid authorId)
    {
        if (Status != ClauseStatus.Draft) throw new InvalidOperationException("An approved wording never changes.");
        Body = body;
        Language = language;
        AuthorId = authorId;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void Approve(Guid approverId)
    {
        Status = ClauseStatus.Approved;
        ApprovedById = approverId;
        ApprovedAt = DateTime.UtcNow;
    }

    internal void MarkImportedApproved()
    {
        Status = ClauseStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
    }

    internal void Deprecate()
    {
        if (Status == ClauseStatus.Deprecated) return;
        Status = ClauseStatus.Deprecated;
        DeprecatedAt = DateTime.UtcNow;
    }
}

/// <summary>A Harvey rule key a library clause answers (R-M9-3).</summary>
public class ClauseRuleLink : TenantEntity
{
    public Guid ClauseId { get; private set; }
    public string RuleKey { get; private set; } = string.Empty;

    private ClauseRuleLink() { }

    internal ClauseRuleLink(Guid clauseId, string ruleKey)
    {
        ClauseId = clauseId;
        RuleKey = ruleKey;
    }
}
