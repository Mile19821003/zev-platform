using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public sealed record TokenResult(string AccessToken, DateTimeOffset AccessExpiresUtc, string RefreshToken);

public sealed class AuthService(ZevDbContext database, IPasswordHasher<AppUser> passwordHasher, TokenService tokens)
{
    public async Task<TokenResult?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var refreshToken = TokenService.NewRefreshToken();
        database.RefreshSessions.Add(new RefreshSession
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(refreshToken),
            SecurityVersion = user.SecurityVersion,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
        await database.SaveChangesAsync(cancellationToken);
        var (accessToken, accessExpiresUtc) = tokens.CreateAccessToken(user);
        return new TokenResult(accessToken, accessExpiresUtc, refreshToken);
    }

    public async Task<TokenResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (refreshToken.Length is < 40 or > 200)
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var hash = TokenService.HashRefreshToken(refreshToken);
        var session = await database.RefreshSessions.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        if (session is null || !session.User.IsActive || session.User.MustChangePassword
            || session.SecurityVersion != session.User.SecurityVersion
            || session.ExpiresUtc <= DateTimeOffset.UtcNow || session.RevokedUtc is not null)
        {
            return null;
        }

        var nextToken = TokenService.NewRefreshToken();
        var nextSession = new RefreshSession
        {
            UserId = session.UserId,
            TokenHash = TokenService.HashRefreshToken(nextToken),
            SecurityVersion = session.User.SecurityVersion,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        session.RevokedUtc = DateTimeOffset.UtcNow;
        session.ReplacedById = nextSession.Id;
        database.RefreshSessions.Add(nextSession);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var (accessToken, accessExpiresUtc) = tokens.CreateAccessToken(session.User);
        return new TokenResult(accessToken, accessExpiresUtc, nextToken);
    }

    public async Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 200)
        {
            return;
        }

        var hash = TokenService.HashRefreshToken(refreshToken);
        var session = await database.RefreshSessions.SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        if (session is null || session.RevokedUtc is not null)
        {
            return;
        }

        session.RevokedUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }
}
