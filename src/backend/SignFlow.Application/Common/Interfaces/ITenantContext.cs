namespace SignFlow.Application.Common.Interfaces;

/// <summary>
/// The organisation the current request or job acts for (R-M0-4). Set from the access token, from the signing
/// link's recipient, or explicitly by a job or seeder through <see cref="BeginScope"/>.
/// When no organisation is set, queries are not filtered by organisation and inserting a business row fails:
/// only sign-in, recovery and platform code run that way.
/// </summary>
public interface ITenantContext
{
    Guid? OrganisationId { get; }

    bool IsScoped => OrganisationId.HasValue;

    /// <summary>Acts for <paramref name="organisationId"/> until the returned handle is disposed.</summary>
    IDisposable BeginScope(Guid organisationId);
}

/// <summary>
/// The few reads that must cross organisations, kept in one reviewed place (R-M0-5). Nothing else may call
/// IgnoreQueryFilters for the tenant filter.
/// </summary>
public interface IPlatformQueries
{
    Task<Guid?> OrganisationOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The organisation of a signing link, by the token's hash.</summary>
    Task<Guid?> OrganisationOfSigningTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>A sealed contract by its public verification code (R-M5-9), whatever its organisation.</summary>
    Task<SealRecord?> FindSealAsync(string verificationCode, CancellationToken cancellationToken = default);

    /// <summary>The organisation of a signing session, by the session secret's hash.</summary>
    Task<Guid?> OrganisationOfSigningSessionAsync(string secretHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ActiveOrganisationIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>The only active organisation, or null when there are none or several. Used by the public forms
    /// that have no organisation in their address yet (self-registration, data requests).</summary>
    Task<Guid?> SoleOrganisationIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether any user of any organisation has this e-mail: people sign in by e-mail alone.</summary>
    Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>A sealed contract as the public verification page sees it: no title, no content.</summary>
public sealed record SealRecord(string Code, string OrganisationName, DateTime SealedAt, string Sha256);
