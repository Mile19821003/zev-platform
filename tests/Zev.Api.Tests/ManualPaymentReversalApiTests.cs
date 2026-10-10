using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

// POST /api/zevs/{zevId}/payments/{paymentId}/reverse — storno of a manual payment.
public sealed class ManualPaymentReversalApiTests
{
    [Fact]
    public async Task ManagerReversesManualPaymentWithReasonAndRepeatIsIdempotent()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var paymentId = await CreateManualPaymentAsync(manager, seed);

        var response = await manager.PostAsJsonAsync(ReversePath(seed, paymentId), new { reason = "Uplata evidentirana dva puta" });
        var body = await ReadAsync(response, HttpStatusCode.OK);
        Assert.False(body.GetProperty("alreadyExists").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var payment = await database.Payments.SingleAsync(item => item.Id == paymentId);
            Assert.Equal(PaymentStatus.Reversed, payment.Status);
            Assert.Equal("Uplata evidentirana dva puta", payment.ReversalReason);
            Assert.Equal(seed.ManagerId, payment.ReversedByUserId);
            Assert.NotNull(payment.ReversedOn);
            Assert.Equal(0m, await database.FundTransactions.Where(item => item.ZevId == seed.ZevId)
                .SumAsync(item => item.SignedAmount));
            Assert.Contains(await database.AuditLogs.Where(item => item.EntityType == nameof(Payment)
                && item.Action == "Modified").ToListAsync(), item => item.ActorUserId == seed.ManagerId
                && item.AfterJson!.Contains("Uplata evidentirana dva puta"));
        }
        var repeated = await ReadAsync(await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { reason = "Uplata evidentirana dva puta" }), HttpStatusCode.OK);
        Assert.True(repeated.GetProperty("alreadyExists").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { reason = "Drugi razlog" })).StatusCode);
    }

    [Fact]
    public async Task ReversalWithoutPaymentsReversePermissionIsRejected()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var paymentId = await CreateManualPaymentAsync(manager, seed);
        var request = new { reason = "Storno" };

        // Representative: reads the ZEV but has no Payments.Reverse by default.
        using var representative = Client(factory, seed.RepresentativeId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await representative.PostAsJsonAsync(ReversePath(seed, paymentId), request)).StatusCode);
        // Manager with an explicit DENY on Payments.Reverse in this ZEV.
        using var deniedManager = Client(factory, seed.DeniedManagerId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await deniedManager.PostAsJsonAsync(ReversePath(seed, paymentId), request)).StatusCode);
        // No access to the ZEV at all: the ZEV is not revealed.
        using var outsider = Client(factory, seed.OutsiderId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.PostAsJsonAsync(ReversePath(seed, paymentId), request)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Equal(PaymentStatus.Posted, (await database.Payments.SingleAsync(item => item.Id == paymentId)).Status);
        Assert.DoesNotContain(await database.FundTransactions.ToListAsync(),
            item => item.Kind == FundTransactionKind.Reversal);
    }

    [Fact]
    public async Task ReversalWithoutReasonIsRejected()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var paymentId = await CreateManualPaymentAsync(manager, seed);

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { reason = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { reason = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(ReversePath(seed, paymentId),
            new { reason = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(ReversePath(seed, Guid.NewGuid()),
            new { reason = "Storno" })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var payment = await database.Payments.SingleAsync(item => item.Id == paymentId);
        Assert.Equal(PaymentStatus.Posted, payment.Status);
        Assert.Null(payment.ReversalReason);
    }

    private static string ReversePath(Seed seed, Guid paymentId) => $"/api/zevs/{seed.ZevId}/payments/{paymentId}/reverse";

    private static async Task<Guid> CreateManualPaymentAsync(HttpClient client, Seed seed)
    {
        var created = await ReadAsync(await client.PostAsJsonAsync($"/api/zevs/{seed.ZevId}/payments/manual", new
        {
            requestId = Guid.NewGuid(), personId = seed.OwnerPersonId, propertyUnitId = (Guid?)null,
            fundId = seed.FundId, paidOn = "2026-10-01", amount = 30m, currencyCode = "BAM",
            method = "Transfer", reason = "Uplata bez izvoda", reference = (string?)null,
            description = (string?)null, allocations = (object[]?)null
        }), HttpStatusCode.OK);
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static HttpClient Client(TestFactory factory, Guid userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        return client;
    }

    private sealed record Seed(Guid ZevId, Guid FundId, Guid OwnerPersonId, Guid ManagerId, Guid DeniedManagerId,
        Guid RepresentativeId, Guid OutsiderId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "REV", Name = "Storno" };
        var zev = new ZevEntity { Organization = organization, Code = "REV", Name = "Storno ZEV" };
        var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, Building = building, Code = "E", Name = "Ulaz" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id,
            Entrance = entrance, Code = "A-01", AreaSquareMeters = 50m };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = fund,
            AccountNumber = "1610000012345678", CurrencyCode = "BAM" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var deniedManager = User("denied"); var representative = User("rep");
        var outsider = User("outsider");
        var owner = new Person { Organization = organization, DisplayName = "Vlasnik" };
        database.AddRange(organization, zev, building, entrance, unit, fund, account, manager, deniedManager,
            representative, outsider, owner,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit, Person = owner,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = deniedManager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = representative, Organization = organization, Zev = zev, Role = MembershipRole.BuildingRepresentative },
            new AccessOverride { UserId = deniedManager.Id, OrganizationId = organization.Id, ZevId = zev.Id,
                PermissionKey = "Payments.Reverse", Effect = AccessEffect.Deny,
                ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = manager.Id,
                Reason = "Bez storna uplata" });
        await database.SaveChangesAsync();
        return new(zev.Id, fund.Id, owner.Id, manager.Id, deniedManager.Id, representative.Id, outsider.Id);
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
