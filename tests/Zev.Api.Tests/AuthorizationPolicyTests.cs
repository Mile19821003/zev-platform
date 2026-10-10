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

// "403 ili 404" (ACCESS_CONTROL_AND_PRIVACY.md): inside a readable ZEV a missing permission is 403;
// another tenant's or an unknown ZEV is 404, so foreign resources are not revealed.
public sealed class AuthorizationPolicyTests
{
    [Fact]
    public async Task MissingPermissionInsideReadableZevIsForbidden()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var accountant = Client(factory, seed.AccountantId);
        using var owner = Client(factory, seed.OwnerUserId);
        var root = $"/api/zevs/{seed.ZevId}";

        // Manager with explicit DENY overrides.
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"{root}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"{root}/payments/accounts",
            new { fundId = seed.FundId, accountNumber = "1610000012345678", currencyCode = "BAM" })).StatusCode);
        // Permission is checked before the resource: an unknown payment is still 403 without Payments.Reverse.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"{root}/payments/{Guid.NewGuid()}/reverse",
            new { reason = "Storno" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{root}/reports/summary")).StatusCode);
        // Accountant without Access.Manage; owner without staff permissions.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync($"{root}/access/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsync($"{root}/expenses/{Guid.NewGuid()}/reverse", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{root}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{root}/reports/open-items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{root}/access/users")).StatusCode);
        // With the permission, a missing resource is still 404.
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(
            $"{root}/payments/accounts/{Guid.NewGuid()}/balance")).StatusCode);
    }

    [Fact]
    public async Task ForeignOrUnknownZevIsNotFound()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var otherManager = Client(factory, seed.OtherManagerId);
        using var outsider = Client(factory, seed.OutsiderId);
        using var manager = Client(factory, seed.ManagerId);
        var root = $"/api/zevs/{seed.ZevId}";

        foreach (var client in new[] { otherManager, outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{root}/payments/accounts")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{root}/payments/accounts",
                new { fundId = seed.FundId, accountNumber = "1610000012345678", currencyCode = "BAM" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{root}/payments/{Guid.NewGuid()}/reverse",
                new { reason = "Storno" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{root}/reports/summary")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{root}/access/users")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/zevs/{Guid.NewGuid()}/payments/accounts")).StatusCode);
        // The other manager's own ZEV works normally.
        Assert.Equal(HttpStatusCode.OK, (await otherManager.GetAsync($"/api/zevs/{seed.OtherZevId}/payments/accounts")).StatusCode);
    }

    private static HttpClient Client(TestFactory factory, Guid userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        return client;
    }

    private sealed record Seed(Guid ZevId, Guid FundId, Guid ManagerId, Guid AccountantId, Guid OwnerUserId,
        Guid OtherZevId, Guid OtherManagerId, Guid OutsiderId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "POL", Name = "Politika" };
        var otherOrganization = new Organization { Code = "POL2", Name = "Drugi upravnik" };
        var zev = new ZevEntity { Organization = organization, Code = "POL", Name = "ZEV A" };
        var otherZev = new ZevEntity { Organization = otherOrganization, Code = "POL2", Name = "ZEV B" };
        var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, Building = building, Code = "E", Name = "Ulaz" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id,
            Entrance = entrance, Code = "A-01", AreaSquareMeters = 50m };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var accountant = User("accountant"); var ownerUser = User("owner");
        var otherManager = User("other"); var outsider = User("outsider");
        var owner = new Person { Organization = organization, DisplayName = "Vlasnik", UserId = ownerUser.Id,
            UserLinkStatus = UserPersonLinkStatus.Verified };
        database.AddRange(organization, otherOrganization, zev, otherZev, building, entrance, unit, fund,
            manager, accountant, ownerUser, otherManager, outsider, owner,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit, Person = owner,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = accountant, Organization = organization, Zev = zev, Role = MembershipRole.Accountant },
            new Membership { User = otherManager, Organization = otherOrganization, Zev = otherZev, Role = MembershipRole.Manager });
        foreach (var key in new[] { "BankAccounts.Create", "Payments.Reverse", "Reports.View" })
            database.AccessOverrides.Add(new AccessOverride { UserId = manager.Id, OrganizationId = organization.Id,
                ZevId = zev.Id, PermissionKey = key, Effect = AccessEffect.Deny,
                ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = otherManager.Id, Reason = "Politika 403" });
        await database.SaveChangesAsync();
        return new(zev.Id, fund.Id, manager.Id, accountant.Id, ownerUser.Id, otherZev.Id, otherManager.Id, outsider.Id);
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
