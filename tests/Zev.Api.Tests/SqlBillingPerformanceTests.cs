using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlBillingPerformanceTests
{
    [Fact]
    public async Task GeneratesThousandChargesOnSqlServerWithinThirtySeconds()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevPerf_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "PERF", Name = "Performance" };
            var zev = new Zev.Api.Domain.Zev { OrganizationId = organization.Id, Organization = organization,
                Code = "PERF", Name = "Performance" };
            var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Building = building, Code = "E", Name = "Entrance" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id,
                Kind = FundKind.Regular, Name = "Regular" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                FundId = fund.Id, Fund = fund, Code = "AREA", Name = "Area", Version = 1,
                Basis = TariffBasis.SquareMeter, Rate = 2m, ValidFrom = new DateOnly(2026, 1, 1) };
            database.AddRange(organization, zev, building, entrance, fund, tariff);
            for (var index = 0; index < 1001; index++)
            {
                var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                    BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance,
                    Code = $"U-{index}", AreaSquareMeters = 30m };
                var person = new Person { OrganizationId = organization.Id, Organization = organization,
                    Kind = PersonKind.Individual, DisplayName = $"Owner {index}" };
                database.AddRange(unit, person);
                database.Ownerships.Add(new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnitId = unit.Id, PropertyUnit = unit, PersonId = person.Id, Person = person,
                    ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            }
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
            var engine = new MonthlyBillingEngine(database);
            var preview = await engine.PreviewAsync(zev.Id, new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 20), null, CancellationToken.None);
            Assert.True(preview.Failure == BillingFailure.None,
                $"Billing preview failed: {preview.Failure}; detail: {preview.Detail ?? "none"}.");
            Assert.Equal(1001, preview.Units.Count);
            var stopwatch = Stopwatch.StartNew();
            var result = await engine.GenerateAsync(zev.Id,
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20), CancellationToken.None);
            stopwatch.Stop();
            Assert.True(result.Failure == BillingFailure.None,
                $"Billing failed: {result.Failure}; detail: {result.Detail ?? "none"}; elapsed: {stopwatch.Elapsed}.");
            Assert.Equal(1001, result.Run?.ChargeCount);
            Assert.Equal(1001, await database.Invoices.AsNoTracking().CountAsync());
            Assert.Equal(1001, await database.InvoiceLines.AsNoTracking().CountAsync());
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"SQL Server run took {stopwatch.Elapsed}.");
            var monthlyTotals = await database.Charges.AsNoTracking().Where(item => item.ZevId == zev.Id)
                .GroupBy(item => item.Period.Month)
                .Select(group => new { month = group.Key, total = group.Sum(item => item.Amount) })
                .ToListAsync();
            Assert.Single(monthlyTotals);
            Assert.Equal(9, monthlyTotals[0].month);
            Assert.Equal(60060m, monthlyTotals[0].total);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
