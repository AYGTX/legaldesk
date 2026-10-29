using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Documents.Queries;

/// <summary>
/// Resolves where a specific version of a contract is stored, so a superseded draft can be read
/// back. Every version's file is retained by <c>UploadRevisionCommand</c>; until now nothing could
/// open one, which made "automatic versioning" a list you could look at but not consult.
/// </summary>
public record GetDocumentVersionFileQuery(Guid DocumentId, int VersionNumber) : IQuery<DocumentVersionFileDto>;

public record DocumentVersionFileDto(string StoragePath, string FileName, int VersionNumber);

public class GetDocumentVersionFileQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDocumentVersionFileQuery, DocumentVersionFileDto>
{
    public async Task<DocumentVersionFileDto> HandleAsync(
        GetDocumentVersionFileQuery query, CancellationToken cancellationToken = default)
    {
        var document = await DocumentVersionAccess.LoadAsync(db, currentUser, query.DocumentId, cancellationToken);
        var path = DocumentVersionAccess.ResolveStoragePath(document, query.VersionNumber);

        return new DocumentVersionFileDto(
            path,
            DocumentVersionAccess.VersionedFileName(document.FileName, query.VersionNumber),
            query.VersionNumber);
    }
}

/// <summary>
/// Shared loading, authorisation and version→file resolution for the version endpoints.
/// </summary>
/// <remarks>
/// The current version is <b>not</b> a <see cref="DocumentVersion"/> row — those hold only
/// superseded files, and the live pointer stays on the document itself. Every caller that maps a
/// version number to a file has to know that, so it is written down once here rather than repeated
/// at each call site where one of them would eventually get it wrong.
/// </remarks>
internal static class DocumentVersionAccess
{
    internal static async Task<Document> LoadAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Versions)
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), documentId);

        // Same audience as the detail page: a reviewer who was asked to approve the revision needs
        // to see what changed at least as much as the owner does.
        var isAdmin = currentUser.Can(Permissions.ManageAllContracts);
        if (!isAdmin && document.OwnerId != currentUser.UserId
            && !document.Recipients.Any(r => r.Email == currentUser.UserEmail))
            throw new ForbiddenAccessException();

        return document;
    }

    internal static string ResolveStoragePath(Document document, int versionNumber)
    {
        if (versionNumber == document.Version)
            return document.StoragePath;

        var version = document.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber)
            ?? throw new NotFoundException(nameof(DocumentVersion), versionNumber);

        return version.StoragePath;
    }

    /// <summary>
    /// Suffixes the version into the download filename. Two versions of the same contract landing in
    /// a reviewer's downloads folder under one name is how the wrong draft gets circulated.
    /// </summary>
    internal static string VersionedFileName(string fileName, int versionNumber)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return $"{stem}-v{versionNumber}{extension}";
    }
}
