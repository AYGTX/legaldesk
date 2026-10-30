using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Reviews;

/// <summary>One rule of Harvey's grid in service, and what the organisation chose for it.</summary>
/// <param name="SeverityOverride">Null keeps the rule's own severity.</param>
public record ReviewRuleSettingDto(string RuleKey, int? Version, string? ApprovedBy, bool Enabled, string? SeverityOverride);

/// <summary>The rules in service and the organisation's settings (R-M0-7). Settings for rules no longer in service are
/// listed too, so an administrator can see and clear them.</summary>
public record GetReviewSettingsQuery : IQuery<List<ReviewRuleSettingDto>>;

public class GetReviewSettingsQueryHandler(
    IApplicationDbContext db,
    ILegalCorpusClient harvey,
    ICurrentUserService currentUser) : IQueryHandler<GetReviewSettingsQuery, List<ReviewRuleSettingDto>>
{
    public async Task<List<ReviewRuleSettingDto>> HandleAsync(GetReviewSettingsQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageReviewSettings))
            throw new ForbiddenAccessException();

        LegalPlaybook playbook;
        try
        {
            playbook = await harvey.GetPlaybookAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceUnavailableException(Domain.Entities.ReviewRun.Unavailable,
                "Harvey cannot be reached: the rules in service cannot be listed now.");
        }

        var settings = await db.OrganisationRuleSettings.AsNoTracking()
            .ToDictionaryAsync(s => s.RuleKey, cancellationToken);

        var keys = playbook.Rules.Keys.Union(settings.Keys).Order(StringComparer.Ordinal);
        return [.. keys.Select(key =>
        {
            var approval = playbook.Approvals?.GetValueOrDefault(key);
            settings.TryGetValue(key, out var setting);
            return new ReviewRuleSettingDto(key, approval?.Version, approval?.ApprovedBy,
                setting?.Enabled ?? true, setting?.SeverityOverride);
        })];
    }
}

/// <summary>Switches a rule off for the organisation or changes the severity it is reviewed at. Applies to the next
/// review; past findings keep the severity they were found with.</summary>
public record SetReviewSettingCommand(string RuleKey, bool Enabled, string? SeverityOverride) : ICommand;

public class SetReviewSettingCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<SetReviewSettingCommand>
{
    public async Task HandleAsync(SetReviewSettingCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageReviewSettings))
            throw new ForbiddenAccessException();

        var key = command.RuleKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > 80)
            throw new ValidationException("Unknown rule.");
        if (command.SeverityOverride is not null && !ReviewGrading.IsSeverity(command.SeverityOverride))
            throw new ValidationException("The severity must be Critical, High, Medium or Low.");

        var setting = await db.OrganisationRuleSettings.FirstOrDefaultAsync(s => s.RuleKey == key, cancellationToken);
        if (setting is null)
        {
            setting = OrganisationRuleSetting.For(key);
            db.OrganisationRuleSettings.Add(setting);
        }

        setting.Change(command.Enabled, command.SeverityOverride, currentUser.UserId!.Value);
        if (setting.IsDefault)
            db.OrganisationRuleSettings.Remove(setting);

        await db.SaveChangesAsync(cancellationToken);
    }
}
