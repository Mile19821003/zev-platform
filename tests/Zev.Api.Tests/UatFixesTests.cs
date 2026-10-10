using System.IO.Compression;
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
using Zev.Api.Payments;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

// Regression tests for the Phase 3 / part A UAT findings.
public sealed class UatFixesTests
{
    [Fact]
    public void PaymentReferenceIsTwelveDigitsWithMod97CheckDigits()
    {
        var reference = PaymentReferenceFormat.Format(1255);
        Assert.Equal("000000125519", reference);
        Assert.Equal(1255, PaymentReferenceFormat.Parse(reference));
        Assert.Equal(1255, PaymentReferenceFormat.Parse("0000 0012 5519"));
        Assert.Equal(PaymentReferenceFormat.MaxNumber,
            PaymentReferenceFormat.Parse(PaymentReferenceFormat.Format(PaymentReferenceFormat.MaxNumber)));
        Assert.Null(PaymentReferenceFormat.Parse("000000125518"));
        Assert.Null(PaymentReferenceFormat.Parse("00000012559"));
        Assert.Null(PaymentReferenceFormat.Parse(LegacyPaymentReference.Format(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentReferenceFormat.Format(0));
        var zev = Guid.NewGuid(); var unit = Guid.NewGuid(); var person = Guid.NewGuid();
        Assert.Equal(new PaymentAccountReference(zev, unit, person),
            LegacyPaymentReference.Parse(LegacyPaymentReference.Format(zev, unit, person, "ZEVTEST")));
    }

    [Fact]
    public void PaymentSlipAmountUsesLocalDecimalComma()
    {
        Assert.Equal("30,00", GenericPdfPaymentSlipProvider.FormatAmount(30m));
        Assert.Equal("1.234,50", GenericPdfPaymentSlipProvider.FormatAmount(1234.5m));
    }

    [Fact]
    public async Task AccountantPostsAndReversesExpenseAndAnnualReportStaysAvailable()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var accountant = Client(factory, seed.AccountantId);
        var root = $"/api/zevs/{seed.ZevId}";

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{root}/payments/accounts",
            new { fundId = seed.FundId, accountNumber = "abc", currencyCode = "BAM", bankName = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{root}/payments/accounts",
            new { fundId = seed.FundId, accountNumber = "161000001234567", currencyCode = "BAM", bankName = "Test" })).StatusCode);
        var account = await ReadAsync(await manager.PostAsJsonAsync($"{root}/payments/accounts",
            new { fundId = seed.FundId, accountNumber = "1610-0000 1234-5678", currencyCode = "BAM", bankName = "Test" }), HttpStatusCode.Created);
        Assert.Equal("1610000012345678", account.GetProperty("accountNumber").GetString());

        // Accountant now has expense rights by default.
        var supplier = await ReadAsync(await accountant.PostAsJsonAsync($"{root}/expenses/suppliers",
            new { name = "Dobavljač" }), HttpStatusCode.Created);
        var expense = await ReadAsync(await accountant.PostAsJsonAsync($"{root}/expenses", new { fundId = seed.FundId,
            supplierId = supplier.GetProperty("id").GetGuid(), documentId = (Guid?)null, invoiceNumber = "R-1",
            invoiceDate = "2026-03-05", amount = 40m, category = "Održavanje", description = (string?)null }), HttpStatusCode.Created);
        var expenseId = expense.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsync($"{root}/expenses/{expenseId}/post", null)).StatusCode);
        var list = await ReadAsync(await accountant.GetAsync($"{root}/expenses"), HttpStatusCode.OK);
        Assert.Equal("BAM", list.GetProperty("items")[0].GetProperty("currencyCode").GetString());
        // Reversal stays a manager right (decision: Accountant gets View/Create/Post only).
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.PostAsync($"{root}/expenses/{expenseId}/reverse", null)).StatusCode);

        var annual = await ReadAsync(await accountant.GetAsync($"{root}/reports/annual/2026"), HttpStatusCode.OK);
        Assert.Equal(40m, Month(annual, 3).GetProperty("spent").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"{root}/expenses/{expenseId}/reverse", null)).StatusCode);
        annual = await ReadAsync(await accountant.GetAsync($"{root}/reports/annual/2026"), HttpStatusCode.OK);
        var reversalMonth = DateTime.UtcNow.Month;
        Assert.Equal(reversalMonth == 3 ? 0m : 40m, Month(annual, 3).GetProperty("spent").GetDecimal());
        Assert.Equal(reversalMonth == 3 ? 0m : -40m, Month(annual, reversalMonth).GetProperty("spent").GetDecimal());
        Assert.All(annual.GetProperty("months").EnumerateArray(), month =>
            Assert.Equal(0m, month.GetProperty("received").GetDecimal()));

        var xlsx = await accountant.GetAsync($"{root}/reports/annual/2026.xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var text = XlsxText(await xlsx.Content.ReadAsByteArrayAsync());
        Assert.Contains("Valuta", text);
        Assert.Contains("Ukupno", text);
        Assert.Contains("BAM", text);

        var summary = await ReadAsync(await accountant.GetAsync($"{root}/reports/summary?from=2026-01-01&to=2026-12-31"), HttpStatusCode.OK);
        Assert.False(summary.GetProperty("mixedCurrency").GetBoolean());
        Assert.Equal("BAM", summary.GetProperty("currencyCode").GetString());
    }

    [Fact]
    public async Task ExpenseOnFundWithSeveralCurrenciesIsRejected()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var root = $"/api/zevs/{seed.ZevId}";
        foreach (var (number, currency) in new[] { ("1610000012345678", "BAM"), ("1610000087654321", "EUR") })
            Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync($"{root}/payments/accounts",
                new { fundId = seed.FundId, accountNumber = number, currencyCode = currency, bankName = "Test" })).StatusCode);
        var supplier = await ReadAsync(await manager.PostAsJsonAsync($"{root}/expenses/suppliers", new { name = "D" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"{root}/expenses", new { fundId = seed.FundId,
            supplierId = supplier.GetProperty("id").GetGuid(), invoiceNumber = "R-2", invoiceDate = "2026-03-05",
            amount = 10m, category = "X" })).StatusCode);
    }

    [Fact]
    public async Task RepresentativeFollowsBallotsAndReportsOnlyCommonAreaFaults()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var representative = Client(factory, seed.RepresentativeId);
        using var owner = Client(factory, seed.OwnerUserId);
        var root = $"/api/zevs/{seed.ZevId}";

        var draft = await ReadAsync(await manager.PostAsJsonAsync($"{root}/ballots", Ballot("Nacrt")), HttpStatusCode.Created);
        var open = await ReadAsync(await manager.PostAsJsonAsync($"{root}/ballots", Ballot("Krečenje")), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"{root}/ballots/{open.GetProperty("id").GetGuid()}/open", null)).StatusCode);
        var seen = await ReadAsync(await representative.GetAsync($"{root}/ballots"), HttpStatusCode.OK);
        var titles = seen.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToList();
        Assert.Equal(new[] { "Krečenje" }, titles);
        Assert.NotEqual(draft.GetProperty("id").GetGuid(), seen.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await representative.PostAsJsonAsync(
            $"{root}/ballots/{open.GetProperty("id").GetGuid()}/votes", new { propertyUnitId = seed.UnitId, choice = "Yes" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"{root}/ballots/{open.GetProperty("id").GetGuid()}/votes", new { propertyUnitId = seed.UnitId, choice = "Yes" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await representative.PostAsJsonAsync($"{root}/maintenance/tickets",
            Ticket(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await representative.PostAsJsonAsync($"{root}/maintenance/tickets",
            Ticket(seed.UnitId))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync($"{root}/maintenance/tickets",
            Ticket(seed.UnitId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"{root}/maintenance/tickets",
            Ticket(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"{root}/maintenance/tickets",
            Ticket(seed.UnitId))).StatusCode);
    }

    private static object Ballot(string title) => new { title, question = "?", kind = "Survey", mode = "OneUnitOneVote",
        weightBasis = "UnitArea", approvalDenominator = "Participating", quorumPercent = 0m, approvalPercent = 50m,
        opensUtc = DateTimeOffset.UtcNow.AddMinutes(-1), closesUtc = DateTimeOffset.UtcNow.AddDays(1) };

    private static object Ticket(Guid? unitId) => new { propertyUnitId = unitId, category = "Rasvjeta",
        description = "Sijalica", location = "Hodnik", priority = "Low", fileIds = Array.Empty<Guid>() };

    private static JsonElement Month(JsonElement annual, int month) =>
        annual.GetProperty("months").EnumerateArray().Single(item => item.GetProperty("month").GetInt32() == month);

    private static string XlsxText(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes));
        return string.Concat(archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .Select(entry => new StreamReader(entry.Open()).ReadToEnd()));
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

    private sealed record Seed(Guid ZevId, Guid FundId, Guid UnitId, Guid ManagerId, Guid AccountantId,
        Guid RepresentativeId, Guid OwnerUserId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "UAT", Name = "UAT" };
        var zev = new ZevEntity { Organization = organization, Code = "UAT", Name = "UAT ZEV" };
        var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, Building = building, Code = "E", Name = "Ulaz" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id,
            Entrance = entrance, Code = "A-01", AreaSquareMeters = 60m };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var accountant = User("accountant"); var representative = User("rep"); var ownerUser = User("owner");
        var owner = new Person { Organization = organization, DisplayName = "Vlasnik", UserId = ownerUser.Id,
            UserLinkStatus = UserPersonLinkStatus.Verified };
        database.AddRange(organization, zev, building, entrance, unit, fund, manager, accountant, representative, ownerUser, owner,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit, Person = owner,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = accountant, Organization = organization, Zev = zev, Role = MembershipRole.Accountant },
            new Membership { User = representative, Organization = organization, Zev = zev, Role = MembershipRole.BuildingRepresentative });
        await database.SaveChangesAsync();
        return new(zev.Id, fund.Id, unit.Id, manager.Id, accountant.Id, representative.Id, ownerUser.Id);
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
