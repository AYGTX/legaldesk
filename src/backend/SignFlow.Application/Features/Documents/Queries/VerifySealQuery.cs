using System.Security.Cryptography;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Documents.Queries;

/// <summary>What the public verification page may say about a sealed contract: who sealed it and when, never its content.</summary>
public record SealInfoDto(string Code, string OrganisationName, DateTime SealedAt);

public record SealCheckDto(string Code, bool Matches, DateTime SealedAt, string OrganisationName);

/// <summary>R-M5-9: GET /verify/{code}, public.</summary>
public record VerifySealQuery(string Code) : IQuery<SealInfoDto>;

public class VerifySealQueryHandler(IPlatformQueries platform) : IQueryHandler<VerifySealQuery, SealInfoDto>
{
    public async Task<SealInfoDto> HandleAsync(VerifySealQuery query, CancellationToken cancellationToken = default)
    {
        var seal = await platform.FindSealAsync(Normalise(query.Code), cancellationToken)
                   ?? throw new NotFoundException("No sealed contract has this verification code.");
        return new SealInfoDto(seal.Code, seal.OrganisationName, seal.SealedAt);
    }

    public static string Normalise(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>R-M5-9: compares an uploaded PDF with the sealed one, byte for byte through its SHA-256. The upload is not kept.</summary>
public record CheckSealedFileCommand(string Code, Stream File) : ICommand<SealCheckDto>;

public class CheckSealedFileCommandHandler(IPlatformQueries platform) : ICommandHandler<CheckSealedFileCommand, SealCheckDto>
{
    public async Task<SealCheckDto> HandleAsync(CheckSealedFileCommand command, CancellationToken cancellationToken = default)
    {
        var seal = await platform.FindSealAsync(VerifySealQueryHandler.Normalise(command.Code), cancellationToken)
                   ?? throw new NotFoundException("No sealed contract has this verification code.");
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(command.File, cancellationToken));
        return new SealCheckDto(seal.Code, string.Equals(sha, seal.Sha256, StringComparison.OrdinalIgnoreCase), seal.SealedAt, seal.OrganisationName);
    }
}
