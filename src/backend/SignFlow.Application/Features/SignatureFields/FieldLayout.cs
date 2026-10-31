using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.SignatureFields;

/// <summary>
/// Proposes the fields of a contract to its owner (R-M3-7), from the stored file. Nothing is saved: the owner accepts or
/// drops each proposal in the designer. Open to the owner, not only to template managers.
/// </summary>
public record DetectContractFieldsQuery(Guid DocumentId) : IQuery<DetectedFormFields>;

public class DetectContractFieldsQueryHandler(
    IApplicationDbContext db,
    IStorageService storage,
    ILegalCorpusClient harvey,
    ICurrentUserService currentUser) : IQueryHandler<DetectContractFieldsQuery, DetectedFormFields>
{
    public async Task<DetectedFormFields> HandleAsync(DetectContractFieldsQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();
        if (document.StoragePath.StartsWith("html-templates/", StringComparison.Ordinal))
            throw new ValidationException("A contract made from a text template has no PDF to read fields from.");

        await using var pdf = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        try
        {
            return await harvey.DetectFormFieldsAsync(pdf, document.FileName, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServiceUnavailableException("detect.unavailable", "Harvey cannot be reached: fields cannot be proposed now.");
        }
    }
}

/// <summary>A field whose box is not inside its page (after the move to points, R-M3-8).</summary>
public record FieldOutsidePageDto(Guid DocumentId, string DocumentTitle, Guid FieldId, FieldType FieldType, int Page,
    float X, float Y, float Width, float Height, double PageWidth, double PageHeight);

/// <summary>Administrators: every draft or in-progress contract's fields that fall outside their page, to fix by hand.</summary>
public record GetFieldLayoutReportQuery : IQuery<List<FieldOutsidePageDto>>;

public class GetFieldLayoutReportQueryHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    ICurrentUserService currentUser) : IQueryHandler<GetFieldLayoutReportQuery, List<FieldOutsidePageDto>>
{
    public async Task<List<FieldOutsidePageDto>> HandleAsync(GetFieldLayoutReportQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageAllContracts))
            throw new ForbiddenAccessException();

        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Draft || d.Status == DocumentStatus.InProgress)
            .Where(d => !d.StoragePath.StartsWith("html-templates/"))
            .Select(d => new { d.Id, d.Title, d.StoragePath })
            .ToListAsync(cancellationToken);

        var report = new List<FieldOutsidePageDto>();
        foreach (var d in documents)
        {
            var fields = await db.SignatureFields.AsNoTracking().Where(f => f.DocumentId == d.Id).ToListAsync(cancellationToken);
            if (fields.Count == 0) continue;

            IReadOnlyList<PageSize> pages;
            try
            {
                await using var pdf = await storage.DownloadAsync(d.StoragePath, cancellationToken);
                using var buffer = new MemoryStream();
                await pdf.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                pages = await pdfService.GetPageSizesAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            foreach (var f in fields)
            {
                var page = f.Page >= 1 && f.Page <= pages.Count ? pages[f.Page - 1] : new PageSize(0, 0);
                if (f.X < 0 || f.Y < 0 || f.X + f.Width > page.Width + 0.5 || f.Y + f.Height > page.Height + 0.5)
                    report.Add(new FieldOutsidePageDto(d.Id, d.Title, f.Id, f.FieldType, f.Page, f.X, f.Y, f.Width, f.Height, page.Width, page.Height));
            }
        }
        return report;
    }
}
