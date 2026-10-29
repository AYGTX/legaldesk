using FluentValidation;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Gdpr.Commands;

public record SubmitDsarCommand(
    string Email,
    string? Name,
    DsarType RequestType,
    string? Details) : ICommand<Guid>;

public class SubmitDsarCommandValidator : AbstractValidator<SubmitDsarCommand>
{
    public SubmitDsarCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.RequestType).IsInEnum();
        RuleFor(x => x.Details).MaximumLength(2000);
    }
}

public class SubmitDsarCommandHandler(IApplicationDbContext db, ITenantContext tenant, IPlatformQueries platform)
    : ICommandHandler<SubmitDsarCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitDsarCommand command, CancellationToken cancellationToken = default)
    {
        // The public form has no organisation in its address yet: it files the request with the install's only
        // organisation. With several organisations the form must name one (M13 work).
        var organisationId = tenant.OrganisationId
            ?? await platform.SoleOrganisationIdAsync(cancellationToken)
            ?? throw new Common.Exceptions.ValidationException(
                [new FluentValidation.Results.ValidationFailure("organisation", "Say which organisation this request is for.")]);
        using var scope = tenant.BeginScope(organisationId);

        var request = DataSubjectRequest.Create(
            command.Email,
            command.Name,
            command.RequestType,
            command.Details);

        db.DataSubjectRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        return request.Id;
    }
}
