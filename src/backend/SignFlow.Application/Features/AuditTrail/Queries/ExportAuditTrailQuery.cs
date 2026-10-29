using SignFlow.Application.Common.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.AuditTrail.Queries;

public record ExportAuditTrailQuery(Guid DocumentId) : IQuery<byte[]>;

public class ExportAuditTrailQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<ExportAuditTrailQuery, byte[]>
{
    public async Task<byte[]> HandleAsync(ExportAuditTrailQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        if (document.OwnerId != currentUser.UserId && !currentUser.Can(Permissions.ManageAllContracts))
            throw new ForbiddenAccessException();

        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.DocumentId == query.DocumentId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Action,Description,Actor Name,Actor Email,IP Address,Location,User Agent");
        foreach (var log in logs)
        {
            sb.AppendLine(string.Join(",",
                Csv(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
                Csv(log.Action.ToString()),
                Csv(log.Description),
                Csv(log.ActorName),
                Csv(log.ActorEmail),
                Csv(log.IpAddress),
                Csv(log.Location),
                Csv(log.UserAgent)));
        }

        // UTF-8 BOM so Excel opens accented characters correctly
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuoting ? $"\"{escaped}\"" : escaped;
    }
}
