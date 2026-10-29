namespace SignFlow.Application.Common.Interfaces;

/// <summary>
/// Names of the global query filters. Feature code may lift <see cref="SoftDelete"/> to see deleted rows of its own
/// organisation; <see cref="Tenant"/> is lifted only inside IPlatformQueries (R-M0-5).
/// </summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
