using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class DataSubjectRequest : BaseEntity
{
    [MaxLength(256)]
    public string SubjectEmail { get; private set; } = string.Empty;
    [MaxLength(256)]
    public string? SubjectName { get; private set; }
    public DsarType RequestType { get; private set; }
    public DsarStatus Status { get; private set; } = DsarStatus.Submitted;
    [MaxLength(2000)]
    public string? Details { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    [MaxLength(450)]
    public string? HandledBy { get; private set; }
    [MaxLength(2000)]
    public string? HandlerNotes { get; private set; }
    [MaxLength(500)]
    public string? ExportPath { get; private set; }

    private DataSubjectRequest() { }

    public static DataSubjectRequest Create(
        string email,
        string? name,
        DsarType type,
        string? details = null)
    {
        return new DataSubjectRequest
        {
            SubjectEmail = email,
            SubjectName = name,
            RequestType = type,
            Details = details,
            DueDate = DateTime.UtcNow.AddDays(30) // Art. 12(3): 30-day SLA
        };
    }

    public void StartReview(string handlerUserId)
    {
        Status = DsarStatus.UnderReview;
        HandledBy = handlerUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete(string notes, string? exportPath = null)
    {
        Status = DsarStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        HandlerNotes = notes;
        ExportPath = exportPath;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        Status = DsarStatus.Rejected;
        HandlerNotes = reason;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
