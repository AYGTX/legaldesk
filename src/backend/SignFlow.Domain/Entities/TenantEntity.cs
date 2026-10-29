namespace SignFlow.Domain.Entities;

/// <summary>A row that belongs to one organisation (R-M0-2).</summary>
public interface ITenantOwned
{
    Guid OrganisationId { get; }

    /// <summary>Set once, when the row is first saved; moving a row to another organisation is not allowed.</summary>
    void AssignOrganisation(Guid organisationId);
}

/// <summary>
/// Base class of every business entity. The organisation is stamped by the DbContext on insert from the
/// current tenant, so feature code never sets it by hand.
/// </summary>
public abstract class TenantEntity : BaseEntity, ITenantOwned
{
    public Guid OrganisationId { get; private set; }

    public void AssignOrganisation(Guid organisationId)
    {
        if (OrganisationId != Guid.Empty && OrganisationId != organisationId)
            throw new InvalidOperationException("A row cannot move to another organisation.");
        OrganisationId = organisationId;
    }
}
