namespace SignFlow.Domain.Entities;

public class Comment : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public ApplicationUser? User { get; private set; }
    public string? RecipientEmail { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public Guid? ParentCommentId { get; private set; }
    public Comment? ParentComment { get; private set; }
    public int? Page { get; private set; }
    public float? X { get; private set; }
    public float? Y { get; private set; }
    public bool IsResolved { get; private set; }
    public string? AuthorName { get; private set; }

    public ICollection<Comment> Replies { get; private set; } = [];

    private Comment() { }

    public static Comment Create(Guid documentId, string content, Guid? userId = null, string? recipientEmail = null, Guid? parentId = null, string? authorName = null)
        => new()
        {
            DocumentId = documentId,
            Content = content,
            UserId = userId,
            RecipientEmail = recipientEmail,
            ParentCommentId = parentId,
            AuthorName = authorName
        };

    public void Resolve() => IsResolved = true;
    public void Unresolve() => IsResolved = false;
    public void SetPosition(int page, float x, float y) { Page = page; X = x; Y = y; }
}
