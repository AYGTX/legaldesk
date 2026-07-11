using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Gdpr.Commands;

public record RecordConsentCommand(
    string UserId,
    ConsentTier Tier,
    bool Granted,
    string Source,
    string? IpAddress = null,
    string? UserAgent = null) : ICommand;

public class RecordConsentCommandHandler(IApplicationDbContext db)
    : ICommandHandler<RecordConsentCommand>
{
    public async Task HandleAsync(RecordConsentCommand command, CancellationToken cancellationToken = default)
    {
        var log = ConsentLog.Record(
            command.UserId,
            command.Tier,
            command.Granted,
            command.Source,
            command.IpAddress,
            command.UserAgent);

        db.ConsentLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
