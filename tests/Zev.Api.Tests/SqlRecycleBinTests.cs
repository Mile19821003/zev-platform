using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.RecycleBin;

namespace Zev.Api.Tests;

// Soft delete on SQL Server: the dependency check translates for every referencing table, and the unique code
// index ignores deleted rows (the code is free for a new record; restore then reports the conflict).
[Collection("Sql integration")]
public sealed class SqlRecycleBinTests
{
    [Fact]
    public async Task DeletedCodeIsReusableAndRestoreReportsConflict()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = $"ZevRecycle_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "SQLRB", Name = "SQL" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "SQLRB", Name = "ZEV" };
            var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Code = "B1", Name = "Zgrada" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id, Code = "E1", Name = "Ulaz" };
            database.AddRange(organization, zev, building, entrance);
            await database.SaveChangesAsync();
            var service = new SoftDeleteAdminService(database, new Zev.Api.Maintenance.MaintenanceScheduleService(database));

            Assert.Equal(SoftDeleteOutcome.InUse, (await service.DeleteAsync(building, null, CancellationToken.None)).Outcome);
            Assert.Equal(SoftDeleteOutcome.Done, (await service.DeleteAsync(entrance, null, CancellationToken.None)).Outcome);
            Assert.Equal(SoftDeleteOutcome.Done, (await service.DeleteAsync(building, null, CancellationToken.None)).Outcome);

            database.Add(new Building { OrganizationId = organization.Id, ZevId = zev.Id, Code = "B1", Name = "Nova zgrada" });
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
            Assert.Equal(1, await database.Buildings.CountAsync());
            Assert.Equal(2, await database.Buildings.IgnoreQueryFilters().CountAsync());

            Assert.Equal(SoftDeleteOutcome.Conflict, (await service.RestoreAsync("Building", building.Id, CancellationToken.None)).Outcome);
            Assert.Equal(SoftDeleteOutcome.ParentDeleted, (await service.RestoreAsync("Entrance", entrance.Id, CancellationToken.None)).Outcome);
            var listed = await service.ListAsync("Registar", null, null, null, CancellationToken.None);
            Assert.Equal(["Building", "Entrance"], listed.Select(item => item.EntityType).Order().ToArray());
            Assert.All(listed, item => Assert.NotNull(item.Id));
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
