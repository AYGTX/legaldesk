using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Persistence;

/// <summary>One per request or job (scoped lifetime). Scopes nest: disposing one restores the one before it.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganisationId { get; private set; }

    public IDisposable BeginScope(Guid organisationId)
    {
        if (organisationId == Guid.Empty)
            throw new ArgumentException("An organisation id is required.", nameof(organisationId));

        var previous = OrganisationId;
        OrganisationId = organisationId;
        return new Restore(this, previous);
    }

    private sealed class Restore(TenantContext context, Guid? previous) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            context.OrganisationId = previous;
            _done = true;
        }
    }
}
