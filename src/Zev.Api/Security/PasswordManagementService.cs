using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public sealed record PasswordChangeResult(TokenResult? Tokens, string? Error);
public enum PasswordResetResult { Success, NotFound, Unchanged }

public sealed class PasswordManagementService(ZevDbContext database, IPasswordHasher<AppUser> hasher,
    TokenService tokens, PasswordPolicy policy)
{
    public async Task<PasswordResetResult> ResetAsync(Guid actorUserId, Guid userId, string newPassword,
        bool requireChange, string correlationId, CancellationToken cancellationToken)
    {
        var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return PasswordResetResult.NotFound;
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, newPassword)
            != PasswordVerificationResult.Failed) return PasswordResetResult.Unchanged;
        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = requireChange;
        user.SecurityVersion = checked(user.SecurityVersion + 1);
        await RevokeSessionsAsync(userId, now, cancellationToken);
        database.AuditLogs.Add(new AuditLog { ActorUserId = actorUserId, EntityType = nameof(AppUser),
            EntityId = userId.ToString(), Action = "PASSWORD_RESET_BY_ADMIN",
            CorrelationId = correlationId });
        if (requireChange)
            database.AuditLogs.Add(new AuditLog { ActorUserId = actorUserId, EntityType = nameof(AppUser),
                EntityId = userId.ToString(), Action = "PASSWORD_CHANGE_REQUIRED",
                CorrelationId = correlationId });
        await database.SaveChangesAsync(cancellationToken);
        return PasswordResetResult.Success;
    }

    public async Task<PasswordChangeResult> ChangeAsync(Guid userId, string? currentPassword,
        string newPassword, string confirmPassword, string correlationId, CancellationToken cancellationToken)
    {
        if (newPassword != confirmPassword) return new(null, "Lozinke se ne podudaraju.");
        if (policy.Validate(newPassword) is string failure) return new(null, failure);
        var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId && item.IsActive,
            cancellationToken);
        if (user is null) return new(null, "Korisnik nije dostupan.");
        if (!user.MustChangePassword && (string.IsNullOrEmpty(currentPassword)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword)
                == PasswordVerificationResult.Failed))
            return new(null, "Trenutna lozinka nije ispravna.");
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, newPassword)
            != PasswordVerificationResult.Failed)
            return new(null, "Nova lozinka mora biti različita od prethodne.");

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.SecurityVersion = checked(user.SecurityVersion + 1);
        await RevokeSessionsAsync(userId, now, cancellationToken);
        var refreshToken = TokenService.NewRefreshToken();
        database.RefreshSessions.Add(new RefreshSession { UserId = userId,
            TokenHash = TokenService.HashRefreshToken(refreshToken), SecurityVersion = user.SecurityVersion,
            ExpiresUtc = now.AddDays(30) });
        database.AuditLogs.Add(new AuditLog { ActorUserId = userId, EntityType = nameof(AppUser),
            EntityId = userId.ToString(), Action = "PASSWORD_CHANGED_BY_USER",
            CorrelationId = correlationId });
        await database.SaveChangesAsync(cancellationToken);
        var (accessToken, expiresUtc) = tokens.CreateAccessToken(user);
        return new(new TokenResult(accessToken, expiresUtc, refreshToken), null);
    }

    private async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await database.RefreshSessions.Where(item => item.UserId == userId
            && item.RevokedUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.RevokedUtc = now;
    }
}
