using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlRegistryPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PagesFiltersAndSearchesTenThousandUnitsOnSqlServer()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevRegistryPerf_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "PERF", Name = "Performance" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "PERF", Name = "Performance" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            database.AddRange(organization, zev, building, entrance);
            await database.SaveChangesAsync();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                WITH Numbers AS (
                    SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS Number
                    FROM sys.all_objects a CROSS JOIN sys.all_objects b
                )
                INSERT INTO PropertyUnits
                    (Id, OrganizationId, ZevId, BuildingId, EntranceId, Code, Type, Floor,
                     AreaSquareMeters, Coefficient, RegisteredMembers, IsActive, OwnershipRevision)
                SELECT NEWID(), {organization.Id}, {zev.Id}, {building.Id}, {entrance.Id},
                    'U-' + RIGHT('00000' + CONVERT(varchar(5), Number), 5), 'Apartment', 1,
                    40.000, 1.0000, 1, CASE WHEN Number % 2 = 0 THEN 1 ELSE 0 END, 0
                FROM Numbers
                """);
            database.ChangeTracker.Clear();
            var filtered = database.PropertyUnits.AsNoTracking().Where(item => item.ZevId == zev.Id
                && item.BuildingId == building.Id && item.EntranceId == entrance.Id && item.IsActive);
            var pageQuery = filtered.OrderBy(item => item.Code).Skip(20).Take(20)
                .Select(item => new { item.Id, item.Code });
            var sql = pageQuery.ToQueryString();
            Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FETCH NEXT", sql, StringComparison.OrdinalIgnoreCase);
            var stopwatch = Stopwatch.StartNew();
            var total = await filtered.CountAsync();
            var page = await pageQuery.ToListAsync();
            stopwatch.Stop();
            Assert.Equal(5000, total);
            Assert.Equal(20, page.Count);
            Assert.Equal("U-00040", page[0].Code);
            var searched = await database.PropertyUnits.AsNoTracking().Where(item => item.ZevId == zev.Id
                && item.BuildingId == building.Id && item.EntranceId == entrance.Id
                && item.Code == "U-09999").ToListAsync();
            Assert.Single(searched);
            output.WriteLine($"SQL registry: total=10000, filtered={total}, returned={page.Count}, count+page={stopwatch.ElapsedMilliseconds}ms; query uses OFFSET/FETCH.");
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
