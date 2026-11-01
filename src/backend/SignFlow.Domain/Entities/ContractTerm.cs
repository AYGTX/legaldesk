using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// The term of a signed contract (R-M10-1): when it started, when it ends now, how it renews, and how much notice ends it.
/// The current end date moves when an automatic renewal rolls the term forward (R-M10-2); the notice deadline is computed
/// from it, never stored. A recorded decision stops the alert ladder (R-M10-4).
/// </summary>
public class ContractTerm : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public DateTime? StartDate { get; private set; }
    public DateTime? InitialEndDate { get; private set; }
    public DateTime? CurrentEndDate { get; private set; }
    public RenewalMode RenewalMode { get; private set; } = RenewalMode.None;
    /// <summary>Length of one renewal, in months.</summary>
    public int? RenewalMonths { get; private set; }
    public int? NoticeAmount { get; private set; }
    public NoticeUnit NoticeUnit { get; private set; } = NoticeUnit.Days;
    public NoticeForm? NoticeForm { get; private set; }

    /// <summary>Who checked the dates against the contract. An unconfirmed term raises no alert.</summary>
    public Guid? ConfirmedById { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public bool IsConfirmed => ConfirmedAt is not null;

    // The ladder: the date it counts towards, and the last rung sent for that date.
    public DateTime? LastAlertAnchor { get; private set; }
    public int? LastAlertOffset { get; private set; }
    public bool EndAlertSent { get; private set; }

    public TermDecision Decision { get; private set; } = TermDecision.None;
    public DateTime? DecisionAt { get; private set; }
    public Guid? DecisionById { get; private set; }
    public DateTime? NoticeSentAt { get; private set; }
    public string? NoticeProofPath { get; private set; }

    private ContractTerm() { }

    public static ContractTerm For(Guid documentId) => new() { DocumentId = documentId };

    public void Set(DateTime? start, DateTime? end, RenewalMode renewal, int? renewalMonths, int? noticeAmount, NoticeUnit noticeUnit,
        NoticeForm? noticeForm, Guid confirmedBy)
    {
        StartDate = start?.Date;
        InitialEndDate = end?.Date;
        CurrentEndDate = end?.Date;
        RenewalMode = renewal;
        RenewalMonths = renewal == RenewalMode.None ? null : renewalMonths;
        NoticeAmount = noticeAmount is > 0 ? noticeAmount : null;
        NoticeUnit = noticeUnit;
        NoticeForm = noticeForm;
        ConfirmedById = confirmedBy;
        ConfirmedAt = DateTime.UtcNow;
        ResetLadder();
        Decision = TermDecision.None;
        DecisionAt = null;
        DecisionById = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The last day notice can be given for the current end date, or null without a notice period.</summary>
    public DateTime? NoticeDeadline => CurrentEndDate is { } end && NoticeAmount is { } n
        ? (NoticeUnit == NoticeUnit.Months ? end.AddMonths(-n) : end.AddDays(-n))
        : null;

    /// <summary>The date the ladder counts towards (R-M10-3): the notice deadline when there is one, else the end date.</summary>
    public DateTime? Anchor => NoticeDeadline ?? CurrentEndDate;

    /// <summary>Recording a decision stops the ladder (R-M10-4).</summary>
    public void Decide(TermDecision decision, Guid by, DateTime? noticeSentAt = null, string? proofPath = null)
    {
        Decision = decision;
        DecisionAt = DateTime.UtcNow;
        DecisionById = by;
        NoticeSentAt = decision == TermDecision.Terminate ? noticeSentAt : null;
        NoticeProofPath = decision == TermDecision.Terminate ? proofPath : null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Automatic renewal (R-M10-2): once the end date has passed with nobody having ended the contract, the term moves
    /// forward by its renewal period — as many times as needed — and the ladder starts again. Returns the end dates
    /// rolled over, oldest first.
    /// </summary>
    public IReadOnlyList<DateTime> RollForward(DateTime today)
    {
        var rolled = new List<DateTime>();
        if (!IsConfirmed || RenewalMode != RenewalMode.Automatic || RenewalMonths is not > 0 || CurrentEndDate is null) return rolled;
        if (Decision is TermDecision.Terminate or TermDecision.LetExpire) return rolled;

        while (CurrentEndDate.Value < today.Date)
        {
            rolled.Add(CurrentEndDate.Value);
            CurrentEndDate = CurrentEndDate.Value.AddMonths(RenewalMonths.Value);
        }
        if (rolled.Count > 0)
        {
            ResetLadder();
            // A decision about the term that just ended does not carry over to the new one.
            Decision = TermDecision.None;
            DecisionAt = null;
            DecisionById = null;
            UpdatedAt = DateTime.UtcNow;
        }
        return rolled;
    }

    public void MarkAlertSent(DateTime anchor, int offset)
    {
        LastAlertAnchor = anchor.Date;
        LastAlertOffset = offset;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkEndAlertSent()
    {
        EndAlertSent = true;
        UpdatedAt = DateTime.UtcNow;
    }

    private void ResetLadder()
    {
        LastAlertAnchor = null;
        LastAlertOffset = null;
        EndAlertSent = false;
    }

    /// <summary>For contracts whose term was recorded before terms had their own record (the migration and the seed).</summary>
    public void Import(DateTime? start, DateTime? end, bool autoRenews, int? noticeDays, bool confirmed, Guid? by)
    {
        StartDate = start?.Date;
        InitialEndDate = end?.Date;
        CurrentEndDate = end?.Date;
        RenewalMode = autoRenews ? RenewalMode.Automatic : RenewalMode.None;
        RenewalMonths = autoRenews ? 12 : null;
        NoticeAmount = noticeDays is > 0 ? noticeDays : null;
        NoticeUnit = NoticeUnit.Days;
        if (confirmed)
        {
            ConfirmedById = by;
            ConfirmedAt = DateTime.UtcNow;
        }
    }
}

/// <summary>
/// Something the contract obliges a party to do, at a date or on a recurrence (R-M10-5). Never invented (R-M10-6): a date
/// suggested from the text carries its quote, and only a confirmed obligation is reminded.
/// </summary>
public class Obligation : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public ObligationKind Kind { get; private set; }
    /// <summary>The next date it is due (for a recurring one, the next occurrence).</summary>
    public DateTime? DueDate { get; private set; }
    public ObligationRecurrence Recurrence { get; private set; } = ObligationRecurrence.None;
    public Guid? OwnerId { get; private set; }
    /// <summary>The contract's words the obligation comes from.</summary>
    public string? Quote { get; private set; }
    public ObligationSource Source { get; private set; } = ObligationSource.Person;
    public ObligationStatus Status { get; private set; } = ObligationStatus.Upcoming;
    public bool Confirmed { get; private set; }
    public DateTime? LastReminderFor { get; private set; }
    public int? LastReminderOffset { get; private set; }

    private Obligation() { }

    public static Obligation Create(Guid documentId, string title, ObligationKind kind, DateTime? dueDate, ObligationRecurrence recurrence,
        Guid? ownerId, string? quote, ObligationSource source, bool confirmed)
    {
        if (source == ObligationSource.Harvey && string.IsNullOrWhiteSpace(quote))
            throw new InvalidOperationException("An obligation suggested from the text carries the words it comes from.");
        return new Obligation
        {
            DocumentId = documentId, Title = title.Trim(), Kind = kind, DueDate = dueDate?.Date, Recurrence = recurrence,
            OwnerId = ownerId, Quote = quote, Source = source, Confirmed = confirmed
        };
    }

    public void Update(string title, ObligationKind kind, DateTime? dueDate, ObligationRecurrence recurrence, Guid? ownerId, string? quote)
    {
        if (dueDate?.Date != DueDate) { LastReminderFor = null; LastReminderOffset = null; }
        Title = title.Trim();
        Kind = kind;
        DueDate = dueDate?.Date;
        Recurrence = recurrence;
        OwnerId = ownerId;
        Quote = quote;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Confirm()
    {
        Confirmed = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Done: a recurring obligation moves to its next occurrence; a one-off one is closed.</summary>
    public void MarkDone()
    {
        if (Recurrence != ObligationRecurrence.None && DueDate is { } due)
        {
            DueDate = Next(due, Recurrence);
            Status = ObligationStatus.Upcoming;
            LastReminderFor = null;
            LastReminderOffset = null;
        }
        else
        {
            Status = ObligationStatus.Done;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Waive()
    {
        Status = ObligationStatus.Waived;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkMissed()
    {
        Status = ObligationStatus.Missed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkReminded(DateTime dueDate, int offset)
    {
        LastReminderFor = dueDate.Date;
        LastReminderOffset = offset;
    }

    public static DateTime Next(DateTime date, ObligationRecurrence recurrence) => recurrence switch
    {
        ObligationRecurrence.Monthly => date.AddMonths(1),
        ObligationRecurrence.Quarterly => date.AddMonths(3),
        ObligationRecurrence.Yearly => date.AddYears(1),
        _ => date
    };
}

/// <summary>What happened to a contract's term (R-M10-2, R-M10-4): kept as a history next to the audit trail.</summary>
public class ContractEvent : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public ContractEventKind Kind { get; private set; }
    public DateTime At { get; private set; }
    public Guid? ById { get; private set; }
    public string? Details { get; private set; }

    private ContractEvent() { }

    public static ContractEvent Record(Guid documentId, ContractEventKind kind, Guid? by, string? details) =>
        new() { DocumentId = documentId, Kind = kind, At = DateTime.UtcNow, ById = by, Details = details };
}
