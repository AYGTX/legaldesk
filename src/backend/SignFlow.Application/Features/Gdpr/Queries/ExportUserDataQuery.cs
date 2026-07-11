using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Gdpr.Queries;

public record ExportUserDataQuery(string UserEmail) : IQuery<UserDataExportDto>;

public record UserDataExportDto(
    string Email,
    string? FirstName,
    string? LastName,
    DateTime AccountCreated,
    List<DocumentExportDto> DocumentsOwned,
    List<RecipientExportDto> DocumentsSigned,
    List<ConsentExportDto> ConsentHistory);

public record DocumentExportDto(Guid Id, string Title, DateTime CreatedAt, string Status);
public record RecipientExportDto(string DocumentTitle, string OwnerEmail, DateTime? SignedAt, string Status);
public record ConsentExportDto(string Tier, bool Granted, DateTime At, string Source);

public class ExportUserDataQueryHandler(IApplicationDbContext db)
    : IQueryHandler<ExportUserDataQuery, UserDataExportDto>
{
    public async Task<UserDataExportDto> HandleAsync(ExportUserDataQuery query, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == query.UserEmail, cancellationToken);

        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.Owner.Email == query.UserEmail && !d.IsDeleted)
            .Select(d => new DocumentExportDto(d.Id, d.Title, d.CreatedAt, d.Status.ToString()))
            .ToListAsync(cancellationToken);

        var signed = await db.Recipients.AsNoTracking()
            .Include(r => r.Document).ThenInclude(d => d.Owner)
            .Where(r => r.Email == query.UserEmail)
            .Select(r => new RecipientExportDto(
                r.Document.Title,
                r.Document.Owner.Email!,
                r.SignedAt,
                r.Status.ToString()))
            .ToListAsync(cancellationToken);

        var consents = await db.ConsentLogs.AsNoTracking()
            .Where(c => c.UserId == (user != null ? user.Id.ToString() : query.UserEmail))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ConsentExportDto(c.Tier.ToString(), c.Granted, c.CreatedAt, c.Source))
            .ToListAsync(cancellationToken);

        return new UserDataExportDto(
            query.UserEmail,
            user?.FirstName,
            user?.LastName,
            user?.CreatedAt ?? DateTime.UtcNow,
            documents,
            signed,
            consents);
    }
}
