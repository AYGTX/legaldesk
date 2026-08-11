using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing;

/// <summary>
/// Radio-group-aware completion rules, shared by SignFieldCommand and ApproveSigningCommand so
/// the two never drift apart (same reasoning as the turn-gating duplication gotcha).
///
/// Radio fields sharing a GroupName form a single-choice group: the group is satisfied when ANY
/// member is signed, and it only gates completion if at least one member is marked required.
/// A radio without a GroupName degenerates to a standalone field (its own group of one).
/// </summary>
public static class RequiredFieldRules
{
    public static bool AllRequiredSatisfied(IReadOnlyCollection<SignatureField> fields)
    {
        var standaloneUnsigned = fields.Any(f =>
            f.IsRequired && !f.IsSigned &&
            (f.FieldType != FieldType.Radio || string.IsNullOrEmpty(f.GroupName)));
        if (standaloneUnsigned) return false;

        return fields
            .Where(f => f.FieldType == FieldType.Radio && !string.IsNullOrEmpty(f.GroupName))
            .GroupBy(f => f.GroupName)
            .All(g => !g.Any(f => f.IsRequired) || g.Any(f => f.IsSigned));
    }
}
