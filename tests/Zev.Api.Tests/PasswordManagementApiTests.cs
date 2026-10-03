using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

public sealed class PasswordManagementApiTests
{
    private const string InitialPassword = "Initial#Password123";
    private const string TemporaryPassword = "Temporary#Password456";
    private const string FinalPassword = "Final#Password789";

    [Fact]
    public async Task SystemAdminResetRevokesTokensAndForcesPasswordChange()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var (adminId, userId) = await SeedUsersAsync(factory);
        string initialHash;
        using (var initialScope = factory.Services.CreateScope())
            initialHash = await initialScope.ServiceProvider.GetRequiredService<ZevDbContext>()
                .Users.Where(item => item.Id == userId).Select(item => item.PasswordHash).SingleAsync();
        var originalLogin = await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = InitialPassword });
        Assert.Equal(HttpStatusCode.OK, originalLogin.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalLogin.Content.ReadAsStringAsync());
        var oldAccess = originalJson.RootElement.GetProperty("accessToken").GetString()!;
        var oldRefresh = originalJson.RootElement.GetProperty("refreshToken").GetString()!;

        Authorize(client, factory, adminId);
        var reset = await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password", new
        {
            newPassword = TemporaryPassword, confirmPassword = TemporaryPassword, requireChange = true
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.DoesNotContain(TemporaryPassword, await reset.Content.ReadAsStringAsync());
        string temporaryHash;
        using (var resetScope = factory.Services.CreateScope())
            temporaryHash = await resetScope.ServiceProvider.GetRequiredService<ZevDbContext>()
                .Users.Where(item => item.Id == userId).Select(item => item.PasswordHash).SingleAsync();
        using var users = JsonDocument.Parse(await client.GetStringAsync("/api/system/users"));
        var userView = users.RootElement.GetProperty("items")[1];
        Assert.True(userView.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(userView.TryGetProperty("passwordHash", out _));
        Assert.False(userView.TryGetProperty("password", out _));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = oldRefresh })).StatusCode);
        using (var staleScope = factory.Services.CreateScope())
        {
            var staleDatabase = staleScope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var oldSession = await staleDatabase.RefreshSessions.SingleAsync(item =>
                item.TokenHash == TokenService.HashRefreshToken(oldRefresh));
            oldSession.RevokedUtc = null;
            await staleDatabase.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = oldRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = InitialPassword })).StatusCode);

        var temporaryLogin = await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = TemporaryPassword });
        Assert.Equal(HttpStatusCode.OK, temporaryLogin.StatusCode);
        using var temporaryJson = JsonDocument.Parse(await temporaryLogin.Content.ReadAsStringAsync());
        var temporaryAccess = temporaryJson.RootElement.GetProperty("accessToken").GetString()!;
        var temporaryRefresh = temporaryJson.RootElement.GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", temporaryAccess);
        using var profile = JsonDocument.Parse(await client.GetStringAsync("/api/me"));
        Assert.True(profile.RootElement.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(profile.RootElement.TryGetProperty("passwordHash", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/zevs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh",
                new { refreshToken = temporaryRefresh })).StatusCode);

        var change = await client.PostAsJsonAsync("/api/me/password", new
        {
            currentPassword = (string?)null, newPassword = FinalPassword, confirmPassword = FinalPassword
        });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var changedJson = JsonDocument.Parse(await change.Content.ReadAsStringAsync());
        var newAccess = changedJson.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", temporaryAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        using var changedProfile = JsonDocument.Parse(await client.GetStringAsync("/api/me"));
        Assert.False(changedProfile.RootElement.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/zevs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = TemporaryPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = FinalPassword })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var user = await database.Users.SingleAsync(item => item.Id == userId);
        Assert.False(user.MustChangePassword);
        var audits = await database.AuditLogs.Where(item => item.EntityId == userId.ToString()).ToListAsync();
        Assert.Contains(audits, item => item.Action == "PASSWORD_RESET_BY_ADMIN" && item.ActorUserId == adminId);
        Assert.Contains(audits, item => item.Action == "PASSWORD_CHANGE_REQUIRED" && item.ActorUserId == adminId);
        Assert.Contains(audits, item => item.Action == "PASSWORD_CHANGED_BY_USER" && item.ActorUserId == userId);
        Assert.All(audits.Where(item => item.Action.StartsWith("PASSWORD_", StringComparison.Ordinal)),
            item => Assert.False(string.IsNullOrWhiteSpace(item.CorrelationId)));
        var auditText = string.Join("|", audits.Select(item => item.BeforeJson + item.AfterJson));
        foreach (var secret in new[] { InitialPassword, TemporaryPassword, FinalPassword,
                     initialHash, temporaryHash, user.PasswordHash })
            Assert.DoesNotContain(secret, auditText);
    }

    [Fact]
    public async Task SelfServiceRequiresCurrentPasswordAndSharesCreationPolicy()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var (adminId, userId) = await SeedUsersAsync(factory);
        Authorize(client, factory, adminId);
        var weakCreate = await client.PostAsJsonAsync("/api/system/users",
            new { email = "weak@example.invalid", password = "longbutalllowercase" });
        Assert.Equal(HttpStatusCode.BadRequest, weakCreate.StatusCode);
        Assert.Contains("veliko slovo", await weakCreate.Content.ReadAsStringAsync());
        var weakReset = await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password",
            new { newPassword = "short", confirmPassword = "short", requireChange = false });
        Assert.Equal(HttpStatusCode.BadRequest, weakReset.StatusCode);
        Assert.Contains("12 znakova", await weakReset.Content.ReadAsStringAsync());
        var mismatch = await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password",
            new { newPassword = TemporaryPassword, confirmPassword = FinalPassword, requireChange = false });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("ne podudaraju", await mismatch.Content.ReadAsStringAsync());
        var unchanged = await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password",
            new { newPassword = InitialPassword, confirmPassword = InitialPassword, requireChange = true });
        Assert.Equal(HttpStatusCode.BadRequest, unchanged.StatusCode);
        Assert.Contains("različita", await unchanged.Content.ReadAsStringAsync());

        var login = await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = InitialPassword });
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var oldAccess = loginJson.RootElement.GetProperty("accessToken").GetString()!;
        var oldRefresh = loginJson.RootElement.GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldAccess);
        var wrong = await client.PostAsJsonAsync("/api/me/password", new
        {
            currentPassword = "Wrong#Password123", newPassword = FinalPassword, confirmPassword = FinalPassword
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Trenutna lozinka", await wrong.Content.ReadAsStringAsync());
        var changed = await client.PostAsJsonAsync("/api/me/password", new
        {
            currentPassword = InitialPassword, newPassword = FinalPassword, confirmPassword = FinalPassword
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = oldRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = InitialPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = FinalPassword })).StatusCode);
    }

    [Fact]
    public async Task ManagerAndOrganizationAdministratorCannotResetOtherUsers()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var (_, userId) = await SeedUsersAsync(factory);
        Guid managerId;
        Guid otherOrganizationAdminId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var ownOrganization = new Organization { Code = "PASSWORD-A", Name = "A" };
            var otherOrganization = new Organization { Code = "PASSWORD-B", Name = "B" };
            var zev = new ZevEntity { Organization = ownOrganization, Code = "PASSWORD-A", Name = "A" };
            var manager = new AppUser { Email = "manager@example.invalid",
                NormalizedEmail = "MANAGER@EXAMPLE.INVALID" };
            var orgAdmin = new AppUser { Email = "org-admin@example.invalid",
                NormalizedEmail = "ORG-ADMIN@EXAMPLE.INVALID" };
            database.AddRange(ownOrganization, otherOrganization, zev, manager, orgAdmin);
            database.Memberships.AddRange(
                new Membership { User = manager, Organization = ownOrganization, Zev = zev,
                    Role = MembershipRole.Manager },
                new Membership { User = orgAdmin, Organization = otherOrganization,
                    Role = MembershipRole.OrganizationAdministrator });
            await database.SaveChangesAsync();
            managerId = manager.Id;
            otherOrganizationAdminId = orgAdmin.Id;
        }
        foreach (var actorId in new[] { managerId, otherOrganizationAdminId })
        {
            Authorize(client, factory, actorId);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password", new
                {
                    newPassword = TemporaryPassword, confirmPassword = TemporaryPassword, requireChange = false
                })).StatusCode);
        }
        using var verificationScope = factory.Services.CreateScope();
        var target = await verificationScope.ServiceProvider.GetRequiredService<ZevDbContext>()
            .Users.SingleAsync(item => item.Id == userId);
        Assert.False(target.MustChangePassword);
        Assert.Equal(0, target.SecurityVersion);
    }

    [Fact]
    public async Task MobileForcedChangeReturnsFreshRotatableRefreshToken()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var (adminId, userId) = await SeedUsersAsync(factory);
        Authorize(client, factory, adminId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"/api/system/users/{userId}/reset-password", new
            {
                newPassword = TemporaryPassword, confirmPassword = TemporaryPassword, requireChange = true
            })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "target@example.invalid", password = TemporaryPassword });
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var temporaryAccess = loginJson.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", temporaryAccess);
        var changed = await client.PostAsJsonAsync("/api/me/password/mobile", new
        {
            currentPassword = (string?)null, newPassword = FinalPassword, confirmPassword = FinalPassword
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedJson = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        var nextAccess = changedJson.RootElement.GetProperty("accessToken").GetString()!;
        var nextRefresh = changedJson.RootElement.GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nextAccess);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/zevs")).StatusCode);
        var refreshed = await client.PostAsJsonAsync("/api/auth/mobile/refresh",
            new { refreshToken = nextRefresh });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    private static async Task<(Guid AdminId, Guid UserId)> SeedUsersAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var admin = new AppUser { Email = "admin@example.invalid",
            NormalizedEmail = "ADMIN@EXAMPLE.INVALID", IsSystemAdministrator = true };
        var user = new AppUser { Email = "target@example.invalid",
            NormalizedEmail = "TARGET@EXAMPLE.INVALID" };
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        admin.PasswordHash = hasher.HashPassword(admin, InitialPassword);
        user.PasswordHash = hasher.HashPassword(user, InitialPassword);
        database.Users.AddRange(admin, user);
        await database.SaveChangesAsync();
        return (admin.Id, user.Id);
    }

    private static void Authorize(HttpClient client, TestFactory factory, Guid userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=localhost;Database=TestOnly;Trusted_Connection=True",
                    ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
                    ["Jwt:Issuer"] = "test-issuer", ["Jwt:Audience"] = "test-audience"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ZevDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ZevDbContext>>();
                services.RemoveAll<ZevDbContext>();
                services.AddDbContext<ZevDbContext>(options => options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }
    }
}
