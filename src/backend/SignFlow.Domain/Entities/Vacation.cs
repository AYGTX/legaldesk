namespace SignFlow.Domain.Entities;

public class Vacation : TenantEntity
{
    public Guid UserId { get; private set; }
    public ApplicationUser User { get; private set; } = null!;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Who acts for this person while they are away (R-M1-8): their steps are handed over when they open.</summary>
    public Guid? DelegateUserId { get; private set; }

    public void SetDelegate(Guid? userId) => DelegateUserId = userId;

    private Vacation() { }

    public static Vacation Create(Guid userId, DateTime startDate, DateTime endDate, string? note = null)
        => new()
        {
            UserId = userId,
            // Normalize to inclusive day boundaries (UTC)
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            Note = note
        };

    public void Update(DateTime startDate, DateTime endDate, string? note)
    {
        StartDate = startDate.Date;
        EndDate = endDate.Date;
        Note = note;
    }

    /// <summary>True if the given instant falls within this vacation (inclusive of the end day).</summary>
    public bool Covers(DateTime instant)
    {
        var day = instant.Date;
        return day >= StartDate && day <= EndDate;
    }
}
