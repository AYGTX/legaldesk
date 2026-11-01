using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Administration;

/// <param name="ModelAvailable">This server has a model configured; without one the assistant cannot be turned on.</param>
/// <param name="ModelEnabled">The organisation lets contract text be sent to that model (D3, R-M12-1).</param>
public record OrganisationSettingsDto(bool ModelAvailable, bool ModelEnabled, string Provider, string Model, string AlertOffsets);

public record GetOrganisationSettingsQuery : IQuery<OrganisationSettingsDto>;

/// <param name="AlertOffsets">Days before a deadline the alerts go out, e.g. "60,30,14,7,0" (R-M10-3).</param>
public record SetOrganisationSettingsCommand(bool ModelEnabled, string AlertOffsets) : ICommand<OrganisationSettingsDto>;

/// <summary>Whether the contract assistant is on for the signed-in person's organisation, so the screens can hide it.</summary>
public record AssistantStatusDto(bool Enabled, string Provider, string Model);

public record GetAssistantStatusQuery : IQuery<AssistantStatusDto>;

public class GetOrganisationSettingsQueryHandler(IApplicationDbContext db, ITenantContext tenant, IModelInfo model)
    : IQueryHandler<GetOrganisationSettingsQuery, OrganisationSettingsDto>
{
    public async Task<OrganisationSettingsDto> HandleAsync(GetOrganisationSettingsQuery query, CancellationToken cancellationToken = default)
    {
        var organisation = await db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == tenant.OrganisationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organisation), tenant.OrganisationId ?? Guid.Empty);
        return OrganisationSettings.ToDto(organisation, model);
    }
}

public class SetOrganisationSettingsCommandHandler(IApplicationDbContext db, ITenantContext tenant, IModelInfo model)
    : ICommandHandler<SetOrganisationSettingsCommand, OrganisationSettingsDto>
{
    public async Task<OrganisationSettingsDto> HandleAsync(SetOrganisationSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var organisation = await db.Organisations.FirstOrDefaultAsync(o => o.Id == tenant.OrganisationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organisation), tenant.OrganisationId ?? Guid.Empty);
        if (command.ModelEnabled && !model.IsConfigured)
            throw new ConflictException("model.notConfigured", "No model is configured on this server: the assistant cannot be turned on.");

        var raw = (command.AlertOffsets ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length == 0 || raw.Any(v => !int.TryParse(v, out var n) || n is < 0 or > 365))
            throw new ValidationException("Alert days must be whole numbers between 0 and 365, separated by commas.");

        organisation.SetModelMode(command.ModelEnabled ? OrganisationSettings.ModeOf(model) : ModelMode.Off);
        organisation.SetAlertOffsets(string.Join(",", TermLadder.Offsets(command.AlertOffsets)));
        await db.SaveChangesAsync(cancellationToken);
        return OrganisationSettings.ToDto(organisation, model);
    }
}

public class GetAssistantStatusQueryHandler(IApplicationDbContext db, ITenantContext tenant, IModelInfo model)
    : IQueryHandler<GetAssistantStatusQuery, AssistantStatusDto>
{
    public async Task<AssistantStatusDto> HandleAsync(GetAssistantStatusQuery query, CancellationToken cancellationToken = default)
    {
        var mode = await db.Organisations.AsNoTracking().Where(o => o.Id == tenant.OrganisationId)
            .Select(o => (ModelMode?)o.ModelMode).FirstOrDefaultAsync(cancellationToken);
        var on = mode is not null and not ModelMode.Off && model.IsConfigured;
        return new AssistantStatusDto(on, on ? model.Provider : string.Empty, on ? model.Model : string.Empty);
    }
}

internal static class OrganisationSettings
{
    /// <summary>The mode follows the server's provider: a model on the organisation's own machine, or Groq.</summary>
    public static ModelMode ModeOf(IModelInfo model) =>
        model.Provider.Contains("local", StringComparison.OrdinalIgnoreCase) || model.Provider.Contains("ollama", StringComparison.OrdinalIgnoreCase)
            ? ModelMode.Local : ModelMode.Groq;

    public static OrganisationSettingsDto ToDto(Organisation organisation, IModelInfo model) =>
        new(model.IsConfigured, organisation.ModelMode != ModelMode.Off, model.Provider, model.Model, organisation.AlertOffsets);
}
