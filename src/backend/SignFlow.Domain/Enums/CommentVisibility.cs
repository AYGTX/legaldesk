namespace SignFlow.Domain.Enums;

/// <summary>Who reads a comment thread (R-M7-1).</summary>
public enum CommentVisibility
{
    /// <summary>The organisation's people only: an internal note.</summary>
    Internal = 0,

    /// <summary>Also the contract's participants.</summary>
    Participants = 1
}
