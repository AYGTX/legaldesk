using SignFlow.Application.Common.Exceptions;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// A participant who has a task — sign, approve, review, stamp — must first have looked at the document and, where they
/// are allowed to see it, the AI review of it. The signing screen asks for that confirmation; the commands that act
/// for the participant refuse to run without it, so the rule holds for anyone calling the API directly too.
/// Viewers have no task and are not asked. Rejecting or asking for changes is not gated: reading the contract and
/// finding it unacceptable is a legitimate outcome of the check.
/// </summary>
public static class ReviewGateRules
{
    public static bool Applies(Recipient recipient) =>
        recipient.ApprovalType is ApprovalType.Signature or ApprovalType.Approval or ApprovalType.Review or ApprovalType.Stamp;

    public static void Require(Recipient recipient)
    {
        if (Applies(recipient) && recipient.ReviewAcknowledgedAt is null)
            throw new ConflictException("Read the document and its AI review, and confirm it, before you act on this request.");
    }
}
