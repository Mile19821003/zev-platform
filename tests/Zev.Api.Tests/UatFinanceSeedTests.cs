using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class UatFinanceSeedTests
{
    [Fact]
    public async Task SeedIsExplicitAndDoesNotWriteOutsideDevelopmentOrUat()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        var disabled = await seed.RunAsync(false, "Development", null, null, CancellationToken.None);
        var production = await seed.RunAsync(true, "Production", null, null, CancellationToken.None);
        Assert.False(disabled.Success);
        Assert.False(production.Success);
        Assert.Empty(database.Organizations);
    }

    [Fact]
    public async Task SeedCreatesExactPreviewWithoutPostingAndIsIdempotent()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        var first = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        var second = await seed.RunAsync(true, "UAT", null, null, CancellationToken.None);
        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(53m, second.UnitUatA01);
        Assert.Equal(69m, second.UnitUatA02);
        Assert.Equal(122m, second.Total);
        Assert.Equal(1, database.Organizations.Count());
        Assert.Equal(2, database.Zevs.Count());
        Assert.Equal(2, database.PropertyUnits.Count());
        Assert.Equal(2, database.Ownerships.Count());
        Assert.Equal(2, database.Funds.Count());
        Assert.Equal(3, database.TariffVersions.Count());
        Assert.All(database.TariffVersions, tariff => Assert.True(tariff.IsOptional));
        Assert.Empty(database.BillingRuns);
        Assert.Empty(database.Charges);
        var zevA = database.Zevs.Single(item => item.Code == "TEST1");
        var units = database.PropertyUnits.OrderBy(item => item.Code).Select(item => item.Id).ToArray();
        var tariffs = database.TariffVersions.Select(item => item.Id).ToArray();
        var preview = await new MonthlyBillingEngine(database).PreviewSelectedUnitsAsync(zevA.Id,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30),
            new BillingDraftRequest(tariffs, null, null), units, CancellationToken.None);
        Assert.Equal(BillingFailure.None, preview.Failure);
        Assert.Equal(53m, preview.Units.Single(item => item.UnitCode == "UAT-A01").Total);
        Assert.Equal(69m, preview.Units.Single(item => item.UnitCode == "UAT-A02").Total);
        Assert.Equal(122m, preview.TotalAmount);
        Assert.Equal(["UAT-CIS1", "UAT-OD1", "UAT-RF1"], preview.Units[0].Lines.Select(item => item.Code)
            .OrderBy(item => item).ToArray());
        Assert.All(preview.Units.SelectMany(item => item.Lines), line => Assert.NotEmpty(line.FundName));
    }

    [Fact]
    public async Task SeedReusesUatRecordsAndLeavesOrdinaryUnitsAndTariffsUntouched()
    {
        using var database = NewDatabase();
        var organization = new Organization { Code = "TEST", Name = "Test upravnik" };
        var zevA = new Zev.Api.Domain.Zev { OrganizationId = organization.Id, Code = "TEST1", Name = "TEST ZEV A" };
        var zevB = new Zev.Api.Domain.Zev { OrganizationId = organization.Id, Code = "TEST2", Name = "TEST ZEV B" };
        var building = new Building { OrganizationId = organization.Id, ZevId = zevA.Id,
            Code = "ZGR-A", Name = "Test zgrada A" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zevA.Id,
            BuildingId = building.Id, Code = "UL-A", Name = "Ulaz A" };
        var ordinaryA01 = new PropertyUnit { OrganizationId = organization.Id, ZevId = zevA.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Code = "A-01",
            AreaSquareMeters = 47m, RegisteredMembers = 1 };
        var ordinaryA02 = new PropertyUnit { OrganizationId = organization.Id, ZevId = zevA.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Code = "A-02",
            AreaSquareMeters = 95m, RegisteredMembers = 4 };
        var regular = new Fund { OrganizationId = organization.Id, ZevId = zevA.Id,
            Kind = FundKind.Regular, Name = "Redovno održavanje" };
        var ordinaryTariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zevA.Id,
            Code = "OD1", Name = "Ručno unesena tarifa", Version = 1, Basis = TariffBasis.SquareMeter,
            Rate = 2m, FundId = regular.Id, ValidFrom = new DateOnly(2026, 10, 1) };
        database.AddRange(organization, zevA, zevB, building, entrance, ordinaryA01, ordinaryA02,
            regular, ordinaryTariff);
        await database.SaveChangesAsync();

        var seed = NewSeed(database);
        var first = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        var second = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(122m, second.Total);
        Assert.Equal(47m, ordinaryA01.AreaSquareMeters);
        Assert.Equal(95m, ordinaryA02.AreaSquareMeters);
        Assert.Equal(2m, ordinaryTariff.Rate);
        Assert.Equal("Ručno unesena tarifa", ordinaryTariff.Name);
        Assert.Equal(4, database.PropertyUnits.Count());
        Assert.Equal(4, database.TariffVersions.Count());
        Assert.Equal(2, database.Ownerships.Count());
        Assert.DoesNotContain(database.Ownerships, item => item.PropertyUnitId == ordinaryA01.Id
            || item.PropertyUnitId == ordinaryA02.Id);
        Assert.Empty(database.BillingRuns);
        Assert.Empty(database.Charges);
    }

    [Fact]
    public async Task ConflictingUatUnitFailsWithoutOverwritingIt()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        Assert.True((await seed.RunAsync(true, "Development", null, null, CancellationToken.None)).Success);
        var unit = database.PropertyUnits.Single(item => item.Code == "UAT-A01");
        unit.AreaSquareMeters = 61m;
        await database.SaveChangesAsync();

        var result = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Property unit UAT-A01 area is 61", result.Error);
        Assert.Equal(61m, unit.AreaSquareMeters);
        Assert.Equal(2, database.PropertyUnits.Count());
    }

    [Fact]
    public async Task ConflictingUatTariffFailsWithoutOverwritingIt()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        Assert.True((await seed.RunAsync(true, "Development", null, null, CancellationToken.None)).Success);
        var tariff = database.TariffVersions.Single(item => item.Code == "UAT-OD1");
        tariff.Rate = .75m;
        await database.SaveChangesAsync();

        var result = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Tariff UAT-OD1 rate is 0.75", result.Error);
        Assert.Equal(.75m, tariff.Rate);
        Assert.Equal(3, database.TariffVersions.Count());
    }

    [Fact]
    public async Task LimitedManagerCannotAccessSecondZev()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var result = await seed.RunAsync(true, "Development", "uat@example.test",
            password, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        var user = Assert.Single(database.Users);
        var zevA = database.Zevs.Single(item => item.Code == "TEST1");
        var zevB = database.Zevs.Single(item => item.Code == "TEST2");
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", user.Id.ToString())], "test"));
        var access = new TenantAccess(database, new HttpContextAccessor { HttpContext = context },
            new EffectiveAccessService(database, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Assert.True(await access.CanManageFinancesAsync(zevA.Id, CancellationToken.None));
        Assert.False(await access.CanReadZevAsync(zevB.Id, CancellationToken.None));
        Assert.True((await seed.RunAsync(true, "Development", "uat@example.test",
            password, CancellationToken.None)).Success);
        Assert.Single(database.Memberships);
    }

    [Fact]
    public async Task ExistingPostedRunBlocksPreviewWithoutCreatingAnotherRun()
    {
        using var database = NewDatabase();
        var seed = NewSeed(database);
        Assert.True((await seed.RunAsync(true, "Development", null, null, CancellationToken.None)).Success);
        var zev = database.Zevs.Single(item => item.Code == "TEST1");
        database.BillingRuns.Add(new BillingRun { OrganizationId = zev.OrganizationId, ZevId = zev.Id,
            Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30) });
        await database.SaveChangesAsync();
        var result = await seed.RunAsync(true, "Development", null, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("already been posted", result.Error);
        Assert.Single(database.BillingRuns);
    }

    private static ZevDbContext NewDatabase()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var database = new ZevDbContext(options, new HttpContextAccessor());
        database.Database.EnsureCreated();
        return database;
    }

    private static UatFinanceSeed NewSeed(ZevDbContext database) =>
        new(database, new MonthlyBillingEngine(database), new PasswordHasher<AppUser>());
}
