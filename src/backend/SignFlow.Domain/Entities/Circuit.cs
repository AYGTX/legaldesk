using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One pass of a contract through its circuit (R-M7-2): opened when the contract is first sent or revised, closed when
/// it completes, is declined, or a revision replaces it. Each round keeps its own steps, decisions and signatures;
/// nothing of an earlier round is deleted.
/// </summary>
public class ContractRound : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public int Number { get; private set; }
    /// <summary>The contract version this round asks people to act on.</summary>
    public int VersionNumber { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public RoundOutcome Outcome { get; private set; } = RoundOutcome.Open;

    private ContractRound() { }

    public static ContractRound Open(Guid documentId, int number, int versionNumber) => new()
    {
        DocumentId = documentId,
        Number = number,
        VersionNumber = versionNumber,
        OpenedAt = DateTime.UtcNow
    };

    public bool IsOpen => Outcome == RoundOutcome.Open;

    public void Close(RoundOutcome outcome)
    {
        if (!IsOpen) return;
        Outcome = outcome;
        ClosedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>A named group of people of the organisation (R-M4-4): presets and pool steps name a team, not a free-text
/// department.</summary>
public class Team : TenantEntity
{
    [MaxLength(120)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; private set; }

    public ICollection<TeamMember> Members { get; private set; } = [];

    private Team() { }

    public static Team Create(string name, string? description = null) => new() { Name = name.Trim(), Description = description?.Trim() };

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}

public class TeamMember : TenantEntity
{
    public Guid TeamId { get; private set; }
    public Team Team { get; private set; } = null!;
    public Guid UserId { get; private set; }

    private TeamMember() { }

    public static TeamMember Of(Guid teamId, Guid userId) => new() { TeamId = teamId, UserId = userId };
}

/// <summary>A day off in the organisation's calendar (R-M4-10): no due date falls on it.</summary>
public class OrganisationHoliday : TenantEntity
{
    public DateOnly Date { get; private set; }

    [MaxLength(120)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>A fixed-date holiday (Independence Day): <see cref="Date"/>'s year is ignored.</summary>
    public bool EveryYear { get; private set; }

    private OrganisationHoliday() { }

    public static OrganisationHoliday On(DateOnly date, string name, bool everyYear = false) =>
        new() { Date = date, Name = name.Trim(), EveryYear = everyYear };

    public bool Covers(DateOnly day) => EveryYear ? day.Month == Date.Month && day.Day == Date.Day : day == Date;

    /// <summary>
    /// Tunisia's religious public holidays for 2026–2028, from the expected Hijri calendar. Each depends on the sighting
    /// of the moon and may move by a day: the name says "estimated", and an administrator corrects the date once the
    /// official announcement is out (Teams > working calendar).
    /// </summary>
    public static IReadOnlyList<OrganisationHoliday> TunisianReligious2026To2028() =>
    [
        On(new DateOnly(2026, 3, 20), "Eid al-Fitr / Aïd el-Fitr (estimated)"),
        On(new DateOnly(2026, 3, 21), "Eid al-Fitr, 2nd day / Aïd el-Fitr, 2e jour (estimated)"),
        On(new DateOnly(2026, 5, 27), "Eid al-Adha / Aïd el-Idha (estimated)"),
        On(new DateOnly(2026, 5, 28), "Eid al-Adha, 2nd day / Aïd el-Idha, 2e jour (estimated)"),
        On(new DateOnly(2026, 6, 16), "Islamic New Year / Ras el-Am el-Hijri (estimated)"),
        On(new DateOnly(2026, 8, 25), "Mawlid / Mouled (estimated)"),
        On(new DateOnly(2027, 3, 10), "Eid al-Fitr / Aïd el-Fitr (estimated)"),
        On(new DateOnly(2027, 3, 11), "Eid al-Fitr, 2nd day / Aïd el-Fitr, 2e jour (estimated)"),
        On(new DateOnly(2027, 5, 16), "Eid al-Adha / Aïd el-Idha (estimated)"),
        On(new DateOnly(2027, 5, 17), "Eid al-Adha, 2nd day / Aïd el-Idha, 2e jour (estimated)"),
        On(new DateOnly(2027, 6, 6), "Islamic New Year / Ras el-Am el-Hijri (estimated)"),
        On(new DateOnly(2027, 8, 15), "Mawlid / Mouled (estimated)"),
        On(new DateOnly(2028, 2, 27), "Eid al-Fitr / Aïd el-Fitr (estimated)"),
        On(new DateOnly(2028, 2, 28), "Eid al-Fitr, 2nd day / Aïd el-Fitr, 2e jour (estimated)"),
        On(new DateOnly(2028, 5, 5), "Eid al-Adha / Aïd el-Idha (estimated)"),
        On(new DateOnly(2028, 5, 6), "Eid al-Adha, 2nd day / Aïd el-Idha, 2e jour (estimated)"),
        On(new DateOnly(2028, 5, 25), "Islamic New Year / Ras el-Am el-Hijri (estimated)"),
        On(new DateOnly(2028, 8, 3), "Mawlid / Mouled (estimated)")
    ];

    /// <summary>Tunisia's fixed-date public holidays. Religious holidays move every year: see <see cref="TunisianReligious2026To2028"/>.</summary>
    public static IReadOnlyList<OrganisationHoliday> TunisianFixed() =>
    [
        On(new DateOnly(2000, 1, 1), "New Year's Day / Jour de l'an", true),
        On(new DateOnly(2000, 3, 20), "Independence Day / Fête de l'indépendance", true),
        On(new DateOnly(2000, 4, 9), "Martyrs' Day / Fête des martyrs", true),
        On(new DateOnly(2000, 5, 1), "Labour Day / Fête du travail", true),
        On(new DateOnly(2000, 7, 25), "Republic Day / Fête de la République", true),
        On(new DateOnly(2000, 8, 13), "Women's Day / Fête de la femme", true),
        On(new DateOnly(2000, 10, 15), "Evacuation Day / Fête de l'évacuation", true),
        On(new DateOnly(2000, 12, 17), "Revolution Day / Fête de la révolution", true)
    ];
}
