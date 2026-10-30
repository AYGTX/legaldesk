namespace SignFlow.Application.Features.Auth.Commands;

/// <summary>Renews the access token from the session cookie (R-M1-4); the controller reads and rewrites the cookie.</summary>
public record RefreshTokenCommand(string? RefreshToken, ClientInfo Client) : Common.Interfaces.ICommand<AuthSession>;

public class RefreshTokenCommandHandler(AuthSessions sessions)
    : Common.Interfaces.ICommandHandler<RefreshTokenCommand, AuthSession>
{
    public Task<AuthSession> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken = default) =>
        sessions.RefreshAsync(command.RefreshToken ?? string.Empty, command.Client, cancellationToken);
}

public record LogoutCommand(string? RefreshToken) : Common.Interfaces.ICommand;

public class LogoutCommandHandler(AuthSessions sessions) : Common.Interfaces.ICommandHandler<LogoutCommand>
{
    public Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default) =>
        sessions.EndAsync(command.RefreshToken, cancellationToken);
}
