using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlBillingConcurrencyTests
{
    [Fact]
    public async Task ConcurrentMonthlyRunsDoNotDuplicateCharges()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevBillingRace_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "RACE", Name = "Race" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "RACE", Name = "Race" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 40m };
            var person = new Person { Organization = organization, DisplayName = "Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "FIX", Name = "Fixed", Rate = 10m, Version = 1,
                Basis = TariffBasis.FixedUnit, ValidFrom = new DateOnly(2026, 1, 1) };
            database.AddRange(organization, zev, building, entrance, unit, person, fund, tariff);
            database.Ownerships.Add(new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            await database.SaveChangesAsync();

            await using var firstDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var secondDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<BillingResult> RunAsync(ZevDbContext context)
            {
                await start.Task;
                return await new MonthlyBillingEngine(context).GenerateAsync(zev.Id,
                    new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20), CancellationToken.None);
            }
            var first = RunAsync(firstDatabase);
            var second = RunAsync(secondDatabase);
            start.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.All(results, result => Assert.Equal(BillingFailure.None, result.Failure));
            Assert.Single(await database.BillingRuns.AsNoTracking().ToListAsync());
            Assert.Single(await database.Charges.AsNoTracking().ToListAsync());
            Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
            Assert.Single(await database.InvoiceLines.AsNoTracking().ToListAsync());
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
