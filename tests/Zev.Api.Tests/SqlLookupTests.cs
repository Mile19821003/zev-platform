using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Lookups;

namespace Zev.Api.Tests;

// Seed and the "one default per list" filtered unique index on SQL Server.
[Collection("Sql integration")]
public sealed class SqlLookupTests
{
    [Fact]
    public async Task SeedIsMigratedAndDefaultCanMoveBetweenItems()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = $"ZevLookups_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            Assert.Equal(7, await database.LookupTypes.CountAsync());
            Assert.Equal(38, await database.LookupItems.CountAsync());
            var theme = await database.ThemeSettings.SingleAsync();
            Assert.Equal(SystemThemeSettings.Factory().Colors(), theme.Colors());
            Assert.Equal("Default", theme.ActiveThemeName);
            Assert.Equal(["v2.10", "v2.11", "v2.12", "v2.13", "v2.14", "v2.15", "v2.7", "v2.8", "v2.9"],
                await database.VersionHistory.OrderBy(item => item.Version).Select(item => item.Version).ToListAsync());
            Assert.Equal("Transfer", await database.LookupItems.Where(item => item.LookupTypeCode == "PAYMENT_METHOD"
                && item.IsDefault).Select(item => item.Code).SingleAsync());

            var service = new LookupService(database);
            var cash = await database.LookupItems.Where(item => item.LookupTypeCode == "PAYMENT_METHOD" && item.Code == "Cash")
                .Select(item => item.Id).SingleAsync();
            Assert.Equal(LookupChangeStatus.Done, (await service.UpdateAsync(cash, "Gotovina", 10, true, true, null, null,
                CancellationToken.None)).Status);
            var created = await service.CreateAsync("PAYMENT_METHOD", "CARD", "Kartica", null, true, null, null, null,
                CancellationToken.None);
            Assert.Equal(LookupChangeStatus.Done, created.Status);
            Assert.Equal(LookupChangeStatus.Conflict, (await service.CreateAsync("PAYMENT_METHOD", "CARD", "Duplikat", null,
                false, null, null, null, CancellationToken.None)).Status);

            database.ChangeTracker.Clear();
            Assert.Equal(["CARD"], await database.LookupItems.Where(item => item.LookupTypeCode == "PAYMENT_METHOD"
                && item.IsDefault).Select(item => item.Code).ToListAsync());
            Assert.Equal(60, created.Item!.SortOrder);
            Assert.Equal(LookupChangeStatus.Conflict, (await service.DeleteAsync(cash, null, null, CancellationToken.None)).Status);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
