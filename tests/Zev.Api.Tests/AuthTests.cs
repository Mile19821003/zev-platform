using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class AuthTests
{
    [Fact]
    public void PasswordPolicyUsesConfiguredMinimumAndEveryCharacterClass()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordPolicy:MinimumLength"] = "14",
            ["PasswordPolicy:MaximumLength"] = "32"
        }).Build();
        var policy = new PasswordPolicy(configuration);
        Assert.Contains("14 znakova", policy.Validate("Short#Pass1"));
        Assert.Contains("veliko slovo", policy.Validate("lowercase#password123"));
        Assert.Contains("malo slovo", policy.Validate("UPPERCASE#PASSWORD123"));
        Assert.Contains("broj", policy.Validate("NoNumbers#Password"));
        Assert.Contains("specijalni znak", policy.Validate("NoSymbolsPassword123"));
        Assert.Null(policy.Validate("Strong#Password123"));
    }

    [Fact]
    public async Task LoginStoresOnlyRefreshHashAndExcludesSecretsFromAudit()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        database.Database.EnsureCreated();
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser { Email = "owner@example.invalid", NormalizedEmail = "OWNER@EXAMPLE.INVALID" };
        user.PasswordHash = hasher.HashPassword(user, "test-password-123");
        database.Users.Add(user);
        await database.SaveChangesAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience"
        }).Build();
        var auth = new AuthService(database, hasher, new TokenService(configuration));

        Assert.Null(await auth.LoginAsync(user.Email, "wrong-password", CancellationToken.None));
        var result = await auth.LoginAsync(user.Email, "test-password-123", CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        var session = await database.RefreshSessions.SingleAsync();
        Assert.NotEqual(result.RefreshToken, session.TokenHash);
        Assert.Equal(TokenService.HashRefreshToken(result.RefreshToken), session.TokenHash);
        Assert.DoesNotContain(database.AuditLogs, entry =>
            (entry.AfterJson ?? string.Empty).Contains(result.RefreshToken, StringComparison.Ordinal)
            || (entry.AfterJson ?? string.Empty).Contains(user.PasswordHash, StringComparison.Ordinal));
    }
}
