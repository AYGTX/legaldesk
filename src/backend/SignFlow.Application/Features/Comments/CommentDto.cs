using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Comments;

public record CommentDto(
    Guid Id,
    Guid DocumentId,
    Guid? UserId,
    string AuthorName,
    string? AuthorEmail,
    string? AuthorAvatarUrl,
    string Content,
    Guid? ParentCommentId,
    int? Page,
    float? X,
    float? Y,
    bool IsResolved,
    bool IsMine,
    bool IsAnonymized,
    DateTime CreatedAt,
    SignFlow.Domain.Enums.CommentVisibility Visibility = SignFlow.Domain.Enums.CommentVisibility.Participants,
    int? VersionNumber = null);

public static class CommentMapping
{
    /// <summary>
    /// Maps a comment for a specific viewer. When <paramref name="anonymize"/> is true
    /// (HideRecipientIdentities documents viewed by a non-privileged participant), all
    /// author-identifying fields are stripped and the frontend renders a generic label.
    /// </summary>
    public static CommentDto ToDto(Comment comment, string? authorAvatarUrl, bool isMine, bool anonymize) => new(
        comment.Id,
        comment.DocumentId,
        anonymize ? null : comment.UserId,
        anonymize ? string.Empty : comment.AuthorName ?? comment.RecipientEmail ?? "Unknown",
        anonymize ? null : comment.RecipientEmail,
        anonymize ? null : authorAvatarUrl,
        comment.Content,
        comment.ParentCommentId,
        comment.Page,
        comment.X,
        comment.Y,
        comment.IsResolved,
        isMine,
        anonymize,
        comment.CreatedAt,
        comment.Visibility,
        comment.VersionNumber);

    /// <summary>
    /// A comment is anonymized for a viewer when the document hides recipient identities,
    /// the viewer is neither privileged (owner/admin) nor the comment's author, and the
    /// comment was not written by the document owner (the requester is always visible).
    /// </summary>
    public static bool ShouldAnonymize(Comment comment, Guid documentOwnerId, bool viewerIsPrivileged, bool isMine, bool hideRecipientIdentities)
        => hideRecipientIdentities
           && !viewerIsPrivileged
           && !isMine
           && comment.UserId != documentOwnerId;
}
