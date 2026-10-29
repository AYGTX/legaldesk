namespace SignFlow.Domain.Entities;

/// <summary>
/// A single review/approval prerequisite the document owner attaches to a Review or Approval
/// recipient (e.g. "Confirm figures match the contract"). The recipient must check every item
/// off before they're allowed to approve — see the guard in ApproveSigningCommand.
/// </summary>
public class ChecklistItem : TenantEntity
{
    public Guid RecipientId { get; private set; }
    public Recipient Recipient { get; private set; } = null!;

    public string Text { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsChecked { get; private set; }
    public DateTime? CheckedAt { get; private set; }

    private ChecklistItem() { }

    public static ChecklistItem Create(Guid recipientId, string text, int order) => new()
    {
        Id = Guid.NewGuid(),
        RecipientId = recipientId,
        Text = text,
        Order = order
    };

    public void SetChecked(bool value)
    {
        IsChecked = value;
        CheckedAt = value ? DateTime.UtcNow : null;
    }
}
