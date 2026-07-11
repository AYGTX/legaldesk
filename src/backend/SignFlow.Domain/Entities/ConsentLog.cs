using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class ConsentLog : BaseEntity
{
    [MaxLength(450)]
    public string UserId { get; private set; } = string.Empty;
    public ConsentTier Tier { get; private set; }
    public bool Granted { get; private set; }
    [MaxLength(64)]
    public string? IpAddress { get; private set; }
    [MaxLength(512)]
    public string? UserAgent { get; private set; }
    [MaxLength(128)]
    public string Source { get; private set; } = string.Empty;

    private ConsentLog() { }

    public static ConsentLog Record(
        string userId,
        ConsentTier tier,
        bool granted,
        string source,
        string? ipAddress = null,
        string? userAgent = null)
    {
        return new ConsentLog
        {
            UserId = userId,
            Tier = tier,
            Granted = granted,
            Source = source,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
    }
}
