using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// One turn of "ask the contract". The conversation is kept by the client and sent back each time, so nothing is
/// stored here beyond an audit entry (the contract text goes to the language model, and that is recorded).
/// </summary>
public record AskContractQuestionCommand(
    Guid DocumentId, string Question, IReadOnlyList<ChatTurn> History, string Language) : ICommand<ContractChatReplyDto>;

/// <param name="Verified">True when the excerpt was found word for word in the contract.</param>
public record ChatCitationDto(string Quote, bool Verified);

/// <param name="Grounded">False when the model gave no excerpt that checks out: the answer is then a lead to verify, not
/// something the contract is shown to say.</param>
public record ContractChatReplyDto(string Answer, IReadOnlyList<ChatCitationDto> Citations, bool Grounded, string Language);

public class AskContractQuestionCommandHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<AskContractQuestionCommandHandler> logger)
    : ICommandHandler<AskContractQuestionCommand, ContractChatReplyDto>
{
    public const int MaxQuestionChars = 1_000;
    public const int MaxHistoryTurns = 10;
    private const int MaxTurnChars = 2_000;

    public async Task<ContractChatReplyDto> HandleAsync(AskContractQuestionCommand command, CancellationToken cancellationToken = default)
    {
        var question = command.Question?.Trim() ?? string.Empty;
        if (question.Length == 0) throw new ValidationException("Ask a question about the contract.");
        if (question.Length > MaxQuestionChars) throw new ValidationException($"Questions are limited to {MaxQuestionChars} characters.");

        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // Only the last few turns, only user/assistant roles, each capped: the client is not trusted with the prompt.
        var history = (command.History ?? [])
            .Where(t => (t.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(t.Content))
            .TakeLast(MaxHistoryTurns)
            .Select(t => new ChatTurn(t.Role, t.Content.Length > MaxTurnChars ? t.Content[..MaxTurnChars] : t.Content))
            .ToList();

        var text = await textProvider.GetTextAsync(document, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("This contract has no readable text to answer from (a scan without OCR?).");

        var language = ContractLanguage.ForAnswer(text, command.Language);

        var answer = await aiService.AnswerQuestionAsync(text, document.Title, history, question, language, cancellationToken);

        var checkedQuotes = answer.Quotes
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Distinct()
            .Take(5)
            .Select(q => new ChatCitationDto(q, QuoteMatcher.IsVerbatim(text, q)))
            .ToList();
        // A quote the model invented is dropped rather than shown: showing it would lend the answer authority it has not earned.
        var shown = checkedQuotes.Where(c => c.Verified).ToList();

        await auditService.LogAsync(
            document.Id,
            AuditAction.ContractQuestionAsked,
            "system",
            $"Question asked about the contract; {shown.Count} verified excerpt(s), {checkedQuotes.Count - shown.Count} rejected",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Contract {DocumentId} question by {UserId}: {Verified} verified / {Rejected} rejected excerpts",
            document.Id, currentUser.UserId, shown.Count, checkedQuotes.Count - shown.Count);

        return new ContractChatReplyDto(answer.Answer, shown, shown.Count > 0, language);
    }
}
