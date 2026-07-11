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

public class SubmitDsarCommandHandler(IApplicationDbContext db)
    : ICommandHandler<SubmitDsarCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitDsarCommand command, CancellationToken cancellationToken = default)
    {
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
