using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public static class AdminBootstrap
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var email = configuration["Bootstrap:AdminEmail"];
        var password = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Bootstrap admin requires email and password.");
        }

        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        if (await database.Users.AnyAsync(user => user.IsSystemAdministrator, cancellationToken))
        {
            return;
        }

        var passwordFailure = scope.ServiceProvider.GetRequiredService<PasswordPolicy>().Validate(password);
        if (passwordFailure is not null)
            throw new InvalidOperationException($"Bootstrap admin password policy: {passwordFailure}");

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (await database.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new InvalidOperationException("Bootstrap email is already registered without administrator privileges.");
        }

        var user = new AppUser { Email = email.Trim(), NormalizedEmail = normalizedEmail, IsSystemAdministrator = true };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, password);
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
    }
}
