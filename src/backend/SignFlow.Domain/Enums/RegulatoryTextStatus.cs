namespace SignFlow.Domain.Enums;

/// <summary>
/// Publication state of a tracked regulatory text. Only <see cref="Published"/> texts are used
/// by compliance analysis and trigger regulatory alerts on contracts already in force.
/// </summary>
public enum RegulatoryTextStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}
