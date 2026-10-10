using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

// Audit of permission changes (event names, reasons, actor) and route protection for overrides.
public sealed class AccessAuditTests
{
    [Fact]
    public async Task MatrixChangesAreAuditedAsGrantDenyAndRevokeWithReasonAndActor()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var path = MatrixPath(seed, seed.AccountantId);

        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(path, Matrix("Privremeno bez exporta",
            ("Invoice.Export", "Deny"), ("Access.Manage", "Grant")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(path, Matrix("Export ponovo po ulozi",
            ("Invoice.Export", null)))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var logs = await database.AuditLogs.Where(item => item.EntityType == nameof(AccessOverride)).ToListAsync();
        Assert.Contains(logs, item => item.Action == "PermissionDenied" && item.ActorUserId == seed.ManagerId
            && item.AfterJson!.Contains("Invoice.Export") && item.AfterJson.Contains("Privremeno bez exporta"));
        Assert.Contains(logs, item => item.Action == "PermissionGranted" && item.ActorUserId == seed.ManagerId
            && item.AfterJson!.Contains("Access.Manage"));
        Assert.Contains(logs, item => item.Action == "PermissionRevoked" && item.ActorUserId == seed.ManagerId
            && item.AfterJson!.Contains("Invoice.Export") && item.AfterJson.Contains("Export ponovo po ulozi"));
        Assert.DoesNotContain(logs, item => item.Action is "Added" or "Modified");
        var reset = await database.AccessOverrides.SingleAsync(item => item.UserId == seed.AccountantId
            && item.PermissionKey == "Invoice.Export");
        Assert.Equal("Export ponovo po ulozi", reset.RevokeReason);
        Assert.Equal(seed.ManagerId, reset.RevokedByUserId);
    }

    [Fact]
    public async Task GrantorCannotGrantOrRestoreRightsTheyDoNotHold()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var limited = Client(factory, seed.LimitedManagerId);

        // Explicit grant of a right the actor is denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PutAsJsonAsync(MatrixPath(seed, seed.RepresentativeId),
            Matrix("Pokušaj", ("Payments.Export", "Grant")))).StatusCode);
        // Reset to a role default that would grant the same right back.
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PutAsJsonAsync(MatrixPath(seed, seed.AccountantId),
            Matrix("Pokušaj", ("Payments.Export", null)))).StatusCode);
        // Denying is allowed: it only narrows access.
        Assert.Equal(HttpStatusCode.OK, (await limited.PutAsJsonAsync(MatrixPath(seed, seed.RepresentativeId),
            Matrix("Bez glasanja", ("Voting.View", "Deny")))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.False(await database.AccessOverrides.AnyAsync(item => item.UserId == seed.RepresentativeId
            && item.PermissionKey == "Payments.Export"));
        Assert.Null((await database.AccessOverrides.SingleAsync(item => item.UserId == seed.AccountantId
            && item.PermissionKey == "Payments.Export")).RevokedUtc);
    }

    [Fact]
    public async Task OwnRightsAndOrganizationAdministratorsCannotBeChangedOnAnyRoute()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);

        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync(MatrixPath(seed, seed.ManagerId),
            Matrix("Sam sebi", ("Voting.View", "Deny")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync(MatrixPath(seed, seed.OrgAdminId),
            Matrix("Administrator", ("Voting.View", "Deny")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(OverridesPath(seed),
            CoarseOverride(seed.ManagerId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(OverridesPath(seed),
            CoarseOverride(seed.OrgAdminId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(
            $"{OverridesPath(seed)}/{seed.ManagerOwnOverrideId}/revoke", new { reason = "Sam sebi" })).StatusCode);
        // Another user's override can be created and revoked by the same manager.
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync(OverridesPath(seed),
            CoarseOverride(seed.AccountantId))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Null((await database.AccessOverrides.SingleAsync(item => item.Id == seed.ManagerOwnOverrideId)).RevokedUtc);
        Assert.False(await database.AccessOverrides.AnyAsync(item => item.UserId == seed.ManagerId
            && item.CreatedByUserId == seed.ManagerId));
        Assert.False(await database.AccessOverrides.AnyAsync(item => item.UserId == seed.OrgAdminId));
    }

    [Fact]
    public async Task RevokeRequiresReasonAndIsAuditedOnce()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var revoke = $"{OverridesPath(seed)}/{seed.AccountantOverrideId}/revoke";

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync(revoke, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(revoke, new { reason = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(revoke,
            new { reason = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync(revoke,
            new { reason = "Ugovor o održavanju istekao" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(revoke, new { reason = "Ponovo" })).StatusCode);

        var list = await manager.GetStringAsync(OverridesPath(seed));
        Assert.Contains("\"revokeReason\":\"Ugovor o održavanju istekao\"", list);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var revoked = await database.AccessOverrides.SingleAsync(item => item.Id == seed.AccountantOverrideId);
        Assert.Equal("Ugovor o održavanju istekao", revoked.RevokeReason);
        Assert.Equal(seed.ManagerId, revoked.RevokedByUserId);
        Assert.Single(await database.AuditLogs.Where(item => item.EntityType == nameof(AccessOverride)
            && item.EntityId == seed.AccountantOverrideId.ToString() && item.Action == "PermissionRevoked"
            && item.ActorUserId == seed.ManagerId).ToListAsync());
    }

    [Fact]
    public async Task OverridesAreAppendOnlyExceptForASingleRevocation()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var item = await database.AccessOverrides.SingleAsync(value => value.Id == seed.AccountantOverrideId);

        item.Reason = "Prepravljen razlog";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        await database.Entry(item).ReloadAsync();
        item.RevokeReason = "Bez opoziva";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        await database.Entry(item).ReloadAsync();
        database.AccessOverrides.Remove(item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        await database.Entry(item).ReloadAsync();

        item.RevokedUtc = DateTimeOffset.UtcNow; item.RevokedByUserId = seed.ManagerId; item.RevokeReason = "Opoziv";
        await database.SaveChangesAsync();
        item.RevokeReason = "Drugi opoziv";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static string MatrixPath(Seed seed, Guid userId) => $"/api/zevs/{seed.ZevId}/access/users/{userId}/permissions";
    private static string OverridesPath(Seed seed) => $"/api/zevs/{seed.ZevId}/access/overrides";

    private static object Matrix(string reason, params (string Key, string? Effect)[] changes) =>
        new { reason, changes = changes.Select(change => new { key = change.Key, effect = change.Effect }).ToArray() };

    private static object CoarseOverride(Guid userId) => new { userId, permissionKey = "maintenance.manage",
        effect = "Grant", validFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), validToUtc = (DateTimeOffset?)null,
        reason = "Zamjena tokom odmora" };

    private static HttpClient Client(TestFactory factory, Guid userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        return client;
    }

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid LimitedManagerId, Guid AccountantId,
        Guid RepresentativeId, Guid OrgAdminId, Guid AccountantOverrideId, Guid ManagerOwnOverrideId);
    // AccountantOverrideId is a coarse override (maintenance.manage): /revoke handles coarse keys,
    // granular keys are reset through the matrix.

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "AUD", Name = "Audit" };
        var zev = new ZevEntity { Organization = organization, Code = "AUD", Name = "Audit ZEV" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var limited = User("limited"); var accountant = User("accountant");
        var representative = User("rep"); var orgAdmin = User("orgadmin");
        AccessOverride Override(AppUser user, string key, AccessEffect effect, AppUser createdBy) => new()
        {
            UserId = user.Id, OrganizationId = organization.Id, ZevId = zev.Id, PermissionKey = key, Effect = effect,
            ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Reason = "Početno stanje", CreatedByUserId = createdBy.Id
        };
        // The limited manager is denied Payments.Export; the accountant's export is denied too.
        var limitedDeny = Override(limited, "Payments.Export", AccessEffect.Deny, orgAdmin);
        var accountantDeny = Override(accountant, "Payments.Export", AccessEffect.Deny, orgAdmin);
        var accountantCoarse = Override(accountant, "maintenance.manage", AccessEffect.Grant, orgAdmin);
        var managerOwn = Override(manager, "legal.manage", AccessEffect.Grant, orgAdmin);
        database.AddRange(organization, zev, manager, limited, accountant, representative, orgAdmin,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = limited, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = accountant, Organization = organization, Zev = zev, Role = MembershipRole.Accountant },
            new Membership { User = representative, Organization = organization, Zev = zev, Role = MembershipRole.BuildingRepresentative },
            new Membership { User = orgAdmin, Organization = organization, Role = MembershipRole.OrganizationAdministrator },
            limitedDeny, accountantDeny, accountantCoarse, managerOwn);
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, limited.Id, accountant.Id, representative.Id, orgAdmin.Id,
            accountantCoarse.Id, managerOwn.Id);
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=TestOnly;Trusted_Connection=True",
                ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
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
