using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RegulatoryWatch.Commands;

public record RegulatoryArticleInput(string Number, string Text);

/// <summary>
/// Replaces a law's articles with the given list. The articles are what a verifier signs off, so saving new ones
/// withdraws any earlier sign-off: nobody should be able to change the text under a verification.
/// </summary>
public record SaveRegulatoryArticlesCommand(Guid RegulatoryTextId, List<RegulatoryArticleInput> Articles) : ICommand;

public class SaveRegulatoryArticlesCommandValidator : AbstractValidator<SaveRegulatoryArticlesCommand>
{
    public SaveRegulatoryArticlesCommandValidator()
    {
        RuleFor(x => x.RegulatoryTextId).NotEmpty();
        RuleFor(x => x.Articles).NotNull().Must(a => a.Count <= 3000).WithMessage("A law cannot have more than 3000 articles.");
        RuleForEach(x => x.Articles).ChildRules(a =>
        {
            a.RuleFor(x => x.Number).NotEmpty().MaximumLength(30);
            a.RuleFor(x => x.Text).NotEmpty().MaximumLength(60_000);
        });
        RuleFor(x => x.Articles)
            .Must(list => list.Select(a => a.Number.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == list.Count)
            .WithMessage("Two articles have the same number.");
    }
}

public class SaveRegulatoryArticlesCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<SaveRegulatoryArticlesCommandHandler> logger) : ICommandHandler<SaveRegulatoryArticlesCommand>
{
    public async Task HandleAsync(SaveRegulatoryArticlesCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var text = await db.RegulatoryTexts
            .Include(r => r.Articles)
            .FirstOrDefaultAsync(r => r.Id == command.RegulatoryTextId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegulatoryText), command.RegulatoryTextId);

        db.RegulatoryArticles.RemoveRange(text.Articles);

        var order = 0;
        foreach (var input in command.Articles)
            db.RegulatoryArticles.Add(RegulatoryArticle.Create(text.Id, input.Number, input.Text, ++order));

        text.ClearVerification();
        text.SetUpdatedBy(currentUser.UserId?.ToString() ?? string.Empty);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text {RegulatoryTextId} now has {Count} articles (saved by {UserId}); verification cleared",
            text.Id, command.Articles.Count, currentUser.UserId);
    }
}

/// <summary>
/// A person attests that the stored articles are the official text, word for word. Allowed only when there is
/// something to check against — articles, the official reference and where to read it — so a sign-off always names
/// its source.
/// </summary>
public record VerifyRegulatoryTextCommand(Guid RegulatoryTextId) : ICommand;

public class VerifyRegulatoryTextCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<VerifyRegulatoryTextCommandHandler> logger) : ICommandHandler<VerifyRegulatoryTextCommand>
{
    public async Task HandleAsync(VerifyRegulatoryTextCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var text = await db.RegulatoryTexts
            .Include(r => r.Articles)
            .FirstOrDefaultAsync(r => r.Id == command.RegulatoryTextId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegulatoryText), command.RegulatoryTextId);

        if (text.Articles.Count == 0)
            throw new ConflictException("There are no articles to verify. Add the text of the law first.");
        if (string.IsNullOrWhiteSpace(text.Reference))
            throw new ConflictException("Give the official reference (for example the law number and date) before verifying.");
        if (string.IsNullOrWhiteSpace(text.SourceUrl))
            throw new ConflictException("Give the address of the official source before verifying, so the sign-off names what it was checked against.");

        text.MarkVerified(userId);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text {RegulatoryTextId} verified by {UserId}", text.Id, userId);
    }
}
