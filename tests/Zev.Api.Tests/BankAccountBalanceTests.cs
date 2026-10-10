using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

// Statement balances (parsing, storage) and GET /payments/accounts/{id}/balance (bank vs fund).
public sealed class BankAccountBalanceTests
{
    [Fact]
    public void Camt053BalancesAndPeriodAreRead()
    {
        var statement = new Camt053StatementParser().ParseStatement(Camt(100m, 120m, "2026-09-01", "2026-09-30",
            (30m, "2026-09-05", "REF-1"), (-10m, "2026-09-07", null)));
        Assert.Equal(100m, statement.OpeningBalance);
        Assert.Equal(120m, statement.ClosingBalance);
        Assert.Equal(new DateOnly(2026, 9, 1), statement.From);
        Assert.Equal(new DateOnly(2026, 9, 30), statement.To);
        Assert.Equal("BAM", statement.BalanceCurrencyCode);
        Assert.Equal([30m, -10m], statement.Transactions.Select(item => item.Amount));

        // Debit balance is negative; PRCD (previous closing) stands in for a missing opening balance.
        var overdrawn = new Camt053StatementParser().ParseStatement(Camt(-5m, 25m, null, null,
            (30m, "2026-10-02", null)).Replace("OPBD", "PRCD"));
        Assert.Equal(-5m, overdrawn.OpeningBalance);
        Assert.Null(overdrawn.From);
    }

    [Fact]
    public void FormatsWithoutBalancesLeaveThemEmpty()
    {
        var csv = ((IBankStatementParser)new DelimitedStatementParser("CSV", ',')).ParseStatement("date,amount,reference\n2026-09-01,10.00,R");
        Assert.Null(csv.OpeningBalance); Assert.Null(csv.ClosingBalance); Assert.Null(csv.From); Assert.Null(csv.To);
        var camtWithoutBalances = new Camt053StatementParser().ParseStatement(Camt(null, null, null, null, (10m, "2026-09-01", null)));
        Assert.Null(camtWithoutBalances.OpeningBalance); Assert.Null(camtWithoutBalances.ClosingBalance);

        var xml = new XmlStatementParser().ParseStatement("<statement><openingBalance>10.50</openingBalance>"
            + "<closingBalance>20.50</closingBalance><from>2026-09-01</from><to>2026-09-30</to>"
            + "<transaction><date>2026-09-02</date><amount>10.00</amount></transaction></statement>");
        Assert.Equal(10.50m, xml.OpeningBalance); Assert.Equal(20.50m, xml.ClosingBalance);
        Assert.Equal(new DateOnly(2026, 9, 30), xml.To);
        Assert.Throws<FormatException>(() => new XmlStatementParser().ParseStatement(
            "<statement><openingBalance>deset</openingBalance><transaction><date>2026-09-02</date><amount>1</amount></transaction></statement>"));
    }

    [Fact]
    public async Task ImportStoresBalancesAndRejectsStatementsThatDoNotAddUp()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var good = Camt(100m, 120m, "2026-09-01", "2026-09-30", (30m, "2026-09-05", "A"), (-10m, "2026-09-07", null));

        var preview = await ReadAsync(await manager.PostAsJsonAsync(StatementsPath(seed.ZevId) + "/preview",
            Statement(seed.AccountId, "CAMT053", good)), HttpStatusCode.OK);
        Assert.Equal(100m, preview.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(120m, preview.GetProperty("closingBalance").GetDecimal());
        await ReadAsync(await manager.PostAsJsonAsync(StatementsPath(seed.ZevId), Statement(seed.AccountId, "CAMT053", good)),
            HttpStatusCode.Created);
        var listed = (await ReadAsync(await manager.GetAsync(StatementsPath(seed.ZevId)), HttpStatusCode.OK)).GetProperty("items")[0];
        Assert.Equal(100m, listed.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(120m, listed.GetProperty("closingBalance").GetDecimal());
        Assert.Equal("2026-09-30", listed.GetProperty("statementTo").GetString());

        var wrongClosing = Camt(100m, 125m, null, null, (30m, "2026-09-05", "B"));
        var rejected = await manager.PostAsJsonAsync(StatementsPath(seed.ZevId), Statement(seed.AccountId, "CAMT053", wrongClosing));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("opening 100.00 + movements 30.00 != closing 125.00", await rejected.Content.ReadAsStringAsync());
        var foreignCurrency = Camt(0m, 5m, null, null, (5m, "2026-09-05", "C")).Replace("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"BAM\">",
            "<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"EUR\">");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(StatementsPath(seed.ZevId),
            Statement(seed.AccountId, "CAMT053", foreignCurrency))).StatusCode);
        var backwards = Camt(0m, 5m, "2026-09-30", "2026-09-01", (5m, "2026-09-05", "D"));
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(StatementsPath(seed.ZevId),
            Statement(seed.AccountId, "CAMT053", backwards))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Single(await database.BankStatementImports.ToListAsync());
    }

    [Fact]
    public async Task BalanceKeepsBankStatementAndFundLedgerApartAndExplainsTheDifference()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var root = $"/api/zevs/{seed.ZevId}";
        // Account A: September 100 → 120 (+30 owner payment, −10 bank fee); October starts at 125 (5 missing) → 145.
        await ReadAsync(await manager.PostAsJsonAsync(StatementsPath(seed.ZevId), Statement(seed.AccountId, "CAMT053",
            Camt(100m, 120m, "2026-09-01", "2026-09-30", (30m, "2026-09-05", "A"), (-10m, "2026-09-07", null)))), HttpStatusCode.Created);
        await ReadAsync(await manager.PostAsJsonAsync(StatementsPath(seed.ZevId), Statement(seed.AccountId, "CAMT053",
            Camt(125m, 145m, "2026-10-01", "2026-10-31", (20m, "2026-10-03", "B")))), HttpStatusCode.Created);
        // Account B of the same fund: CSV without balances, +15.
        await ReadAsync(await manager.PostAsJsonAsync(StatementsPath(seed.ZevId), Statement(seed.SecondAccountId, "CSV",
            "date,amount,reference\n2026-09-10,15.00,C")), HttpStatusCode.Created);
        // Fund only: manual payment +50 and posted expense −40.
        await ReadAsync(await manager.PostAsJsonAsync($"{root}/payments/manual", new
        {
            requestId = Guid.NewGuid(), personId = seed.OwnerPersonId, propertyUnitId = (Guid?)null, fundId = seed.FundId,
            paidOn = "2026-09-15", amount = 50m, currencyCode = "BAM", method = "Transfer", reason = "Uplata bez izvoda",
            reference = (string?)null, description = (string?)null, allocations = (object[]?)null
        }), HttpStatusCode.OK);
        var supplier = await ReadAsync(await manager.PostAsJsonAsync($"{root}/expenses/suppliers", new { name = "Čistoća" }),
            HttpStatusCode.Created);
        var expense = await ReadAsync(await manager.PostAsJsonAsync($"{root}/expenses", new { fundId = seed.FundId,
            supplierId = supplier.GetProperty("id").GetGuid(), documentId = (Guid?)null, invoiceNumber = "R-9",
            invoiceDate = "2026-09-20", amount = 40m, category = "Održavanje", description = (string?)null }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync(
            $"{root}/expenses/{expense.GetProperty("id").GetGuid()}/post", null)).StatusCode);

        var balance = await ReadAsync(await manager.GetAsync(BalancePath(seed.ZevId, seed.AccountId)), HttpStatusCode.OK);
        var bank = balance.GetProperty("bank");
        Assert.Equal("Statement", bank.GetProperty("balanceSource").GetString());
        Assert.Equal(145m, bank.GetProperty("reportedBalance").GetProperty("amount").GetDecimal());
        Assert.Equal("2026-10-31", bank.GetProperty("reportedBalance").GetProperty("asOf").GetString());
        Assert.Equal(50m, bank.GetProperty("credits").GetDecimal());
        Assert.Equal(-10m, bank.GetProperty("debits").GetDecimal());
        Assert.Equal(40m, bank.GetProperty("movement").GetDecimal());
        Assert.Equal(140m, bank.GetProperty("derivedBalance").GetDecimal());
        Assert.False(bank.GetProperty("derivedMatchesReported").GetBoolean());
        var gap = Assert.Single(bank.GetProperty("gaps").EnumerateArray());
        Assert.Equal(5m, gap.GetProperty("difference").GetDecimal());
        Assert.Equal(2, bank.GetProperty("statementCount").GetInt32());

        var fund = balance.GetProperty("fund");
        Assert.Equal(75m, fund.GetProperty("balance").GetDecimal());
        Assert.Equal(50m, fund.GetProperty("fromThisAccount").GetDecimal());
        Assert.Equal(15m, fund.GetProperty("fromOtherAccounts").GetDecimal());
        Assert.Equal(50m, fund.GetProperty("manualPayments").GetDecimal());
        Assert.Equal(-40m, fund.GetProperty("expenses").GetDecimal());

        var reconciliation = balance.GetProperty("reconciliation");
        Assert.Equal(40m, reconciliation.GetProperty("bankMovement").GetDecimal());
        Assert.Equal(75m, reconciliation.GetProperty("fundBalance").GetDecimal());
        Assert.Equal(-35m, reconciliation.GetProperty("difference").GetDecimal());
        Assert.Equal(-10m, reconciliation.GetProperty("bankOutflowsNotInFund").GetDecimal());
        Assert.Equal(0m, reconciliation.GetProperty("unexplained").GetDecimal());

        // Account B has no statement balances: nothing is invented.
        var second = (await ReadAsync(await manager.GetAsync(BalancePath(seed.ZevId, seed.SecondAccountId)),
            HttpStatusCode.OK)).GetProperty("bank");
        Assert.Equal("NotProvided", second.GetProperty("balanceSource").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("reportedBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("derivedBalance").ValueKind);
        Assert.Equal(15m, second.GetProperty("movement").GetDecimal());
    }

    [Fact]
    public async Task BalanceIsIsolatedPerZevAndRequiresBankAccountView()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var otherManager = Client(factory, seed.OtherManagerId);
        using var representative = Client(factory, seed.RepresentativeId);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(BalancePath(seed.ZevId, seed.AccountId))).StatusCode);
        // Another ZEV's account through this ZEV's path, and this ZEV's account by its manager of another ZEV.
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(BalancePath(seed.ZevId, seed.OtherAccountId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync(BalancePath(seed.ZevId, seed.AccountId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync(BalancePath(seed.OtherZevId, seed.AccountId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherManager.GetAsync(BalancePath(seed.OtherZevId, seed.OtherAccountId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await representative.GetAsync(BalancePath(seed.ZevId, seed.AccountId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(BalancePath(seed.ZevId, Guid.NewGuid()))).StatusCode);
    }

    private static string StatementsPath(Guid zevId) => $"/api/zevs/{zevId}/payments/statements";
    private static string BalancePath(Guid zevId, Guid accountId) => $"/api/zevs/{zevId}/payments/accounts/{accountId}/balance";

    private static object Statement(Guid accountId, string format, string content) => new
    {
        bankAccountId = accountId, fileName = $"izvod-{Guid.NewGuid():N}.{(format == "CSV" ? "csv" : "xml")}", format,
        contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
    };

    private static string Camt(decimal? opening, decimal? closing, string? from, string? to,
        params (decimal Amount, string Date, string? Reference)[] entries)
    {
        static string Amount(decimal value) => Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
        static string Balance(string code, decimal value) => $"<Bal><Tp><CdOrPrtry><Cd>{code}</Cd></CdOrPrtry></Tp>"
            + $"<Amt Ccy=\"BAM\">{Amount(value)}</Amt><CdtDbtInd>{(value < 0m ? "DBIT" : "CRDT")}</CdtDbtInd></Bal>";
        var builder = new StringBuilder("<Document><BkToCstmrStmt><Stmt>");
        if (from is not null) builder.Append($"<FrToDt><FrDtTm>{from}T00:00:00</FrDtTm><ToDtTm>{to}T23:59:59</ToDtTm></FrToDt>");
        if (opening is decimal open) builder.Append(Balance("OPBD", open));
        if (closing is decimal close) builder.Append(Balance("CLBD", close));
        foreach (var entry in entries)
            builder.Append($"<Ntry><Amt Ccy=\"BAM\">{Amount(entry.Amount)}</Amt><CdtDbtInd>{(entry.Amount < 0m ? "DBIT" : "CRDT")}</CdtDbtInd>"
                + $"<BookgDt><Dt>{entry.Date}</Dt></BookgDt>"
                + (entry.Reference is null ? "" : $"<NtryDtls><TxDtls><RmtInf><Strd><CdtrRefInf><Ref>{entry.Reference}</Ref></CdtrRefInf></Strd></RmtInf></TxDtls></NtryDtls>")
                + "</Ntry>");
        return builder.Append("</Stmt></BkToCstmrStmt></Document>").ToString();
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

    private sealed record Seed(Guid ZevId, Guid FundId, Guid AccountId, Guid SecondAccountId, Guid OwnerPersonId,
        Guid ManagerId, Guid RepresentativeId, Guid OtherZevId, Guid OtherAccountId, Guid OtherManagerId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "BAL", Name = "Saldo" };
        var zev = new ZevEntity { Organization = organization, Code = "BAL", Name = "Saldo ZEV" };
        var otherZev = new ZevEntity { Organization = organization, Code = "BAL2", Name = "Drugi ZEV" };
        var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, Building = building, Code = "E", Name = "Ulaz" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id,
            Entrance = entrance, Code = "A-01", AreaSquareMeters = 50m };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        var otherFund = new Fund { OrganizationId = organization.Id, ZevId = otherZev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = fund,
            AccountNumber = "1610000012345678", CurrencyCode = "BAM" };
        var secondAccount = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = fund,
            AccountNumber = "1610000087654321", CurrencyCode = "BAM" };
        var otherAccount = new BankAccount { OrganizationId = organization.Id, ZevId = otherZev.Id, Fund = otherFund,
            AccountNumber = "1610000011112222", CurrencyCode = "BAM" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var otherManager = User("other"); var representative = User("rep");
        var owner = new Person { Organization = organization, DisplayName = "Vlasnik" };
        database.AddRange(organization, zev, otherZev, building, entrance, unit, fund, otherFund, account, secondAccount,
            otherAccount, manager, otherManager, representative, owner,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit, Person = owner,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = otherManager, Organization = organization, Zev = otherZev, Role = MembershipRole.Manager },
            new Membership { User = representative, Organization = organization, Zev = zev, Role = MembershipRole.BuildingRepresentative });
        await database.SaveChangesAsync();
        return new(zev.Id, fund.Id, account.Id, secondAccount.Id, owner.Id, manager.Id, representative.Id,
            otherZev.Id, otherAccount.Id, otherManager.Id);
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
