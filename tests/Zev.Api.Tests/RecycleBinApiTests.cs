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
using Zev.Api.RecycleBin;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

// Soft delete ("Obriši") of registry records and suppliers, and the "Obrisani redovi" administration (list, restore).
public sealed class RecycleBinApiTests
{
    [Fact]
    public async Task ManagerDeletesUnusedBuildingAndAdministratorRestoresIt()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var admin = Client(factory, seed.SystemAdminId);

        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/buildings/{seed.EmptyBuildingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/buildings/{seed.EmptyBuildingId}")).StatusCode);
        var buildings = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/registry/buildings"), HttpStatusCode.OK);
        Assert.DoesNotContain(buildings.GetProperty("items").EnumerateArray(), item => item.GetProperty("code").GetString() == "B2");

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var deleted = await database.Buildings.IgnoreQueryFilters().SingleAsync(item => item.Id == seed.EmptyBuildingId);
            Assert.True(deleted.IsDeleted);
            Assert.NotNull(deleted.DeletedAt);
            Assert.Equal(seed.ManagerId, deleted.DeletedByUserId);
            Assert.True(await database.AuditLogs.AnyAsync(item => item.Action == "RecordDeleted" && item.EntityType == "Building"
                && item.EntityId == seed.EmptyBuildingId.ToString() && item.ActorUserId == seed.ManagerId));
        }

        var bin = await ReadAsync(await admin.GetAsync("/api/admin/recycle-bin"), HttpStatusCode.OK);
        var row = Assert.Single(bin.GetProperty("items").EnumerateArray());
        Assert.Equal("Registar", row.GetProperty("module").GetString());
        Assert.Equal("Zgrade", row.GetProperty("form").GetString());
        Assert.Equal("Building", row.GetProperty("entityType").GetString());
        Assert.Equal(seed.EmptyBuildingId, row.GetProperty("recordId").GetGuid());
        Assert.Equal("B2 · Zgrada B", row.GetProperty("label").GetString());
        Assert.Equal("manager@example.invalid", row.GetProperty("deletedBy").GetString());
        Assert.True(row.GetProperty("id").GetInt64() > 0);
        Assert.Equal(5, bin.GetProperty("types").GetArrayLength());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(1, (await ReadAsync(await admin.GetAsync($"/api/admin/recycle-bin?module=Registar&entityType=Building&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"),
            HttpStatusCode.OK)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ReadAsync(await admin.GetAsync("/api/admin/recycle-bin?module=Finansije"), HttpStatusCode.OK))
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ReadAsync(await admin.GetAsync($"/api/admin/recycle-bin?to={today.AddDays(-1):yyyy-MM-dd}"),
            HttpStatusCode.OK)).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/recycle-bin?module=Nepoznato")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/recycle-bin?from=2026-10-02&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/recycle-bin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "Building", recordId = seed.EmptyBuildingId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "Invoice", recordId = seed.EmptyBuildingId })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "Building", recordId = seed.EmptyBuildingId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "Building", recordId = seed.EmptyBuildingId })).StatusCode);
        buildings = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/registry/buildings"), HttpStatusCode.OK);
        Assert.Contains(buildings.GetProperty("items").EnumerateArray(), item => item.GetProperty("code").GetString() == "B2");
        Assert.Equal(0, (await ReadAsync(await admin.GetAsync("/api/admin/recycle-bin"), HttpStatusCode.OK)).GetProperty("totalCount").GetInt32());

        using var check = factory.Services.CreateScope();
        var restored = await check.ServiceProvider.GetRequiredService<ZevDbContext>().Buildings.SingleAsync(item => item.Id == seed.EmptyBuildingId);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Null(restored.DeletedByUserId);
        Assert.True(await check.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs.AnyAsync(item => item.Action == "RecordRestored"
            && item.EntityId == seed.EmptyBuildingId.ToString() && item.ActorUserId == seed.SystemAdminId));
    }

    [Fact]
    public async Task RecordsInUseCannotBeDeletedAndRestoreNeedsLiveParent()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var admin = Client(factory, seed.SystemAdminId);

        var building = await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/buildings/{seed.BuildingId}");
        Assert.Contains("ulazi", (await ReadAsync(building, HttpStatusCode.Conflict)).GetProperty("error").GetString());
        var unit = await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/units/{seed.OwnedUnitId}");
        Assert.Contains("vlasništva", (await ReadAsync(unit, HttpStatusCode.Conflict)).GetProperty("error").GetString());
        var person = await admin.DeleteAsync($"/api/system/people/{seed.OwnerId}");
        Assert.Contains("vlasništva", (await ReadAsync(person, HttpStatusCode.Conflict)).GetProperty("error").GetString());

        // Empty unit, then its entrance (the owned unit still blocks the building), then nothing is left in B1's entrance E2.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/units/{seed.EmptyUnitId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/entrances/{seed.EmptyEntranceId}")).StatusCode);
        var restoreUnit = await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore", new { entityType = "PropertyUnit", recordId = seed.EmptyUnitId });
        Assert.Contains("najprije vratite ulaz", (await ReadAsync(restoreUnit, HttpStatusCode.Conflict)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "Entrance", recordId = seed.EmptyEntranceId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "PropertyUnit", recordId = seed.EmptyUnitId })).StatusCode);

        // A person without registry history can be deleted by the system administrator.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/system/people/{seed.LoosePersonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/system/people/{seed.LoosePersonId}")).StatusCode);
    }

    [Fact]
    public async Task ReusedCodeBlocksRestoreAndOtherZevIsNotFound()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var admin = Client(factory, seed.SystemAdminId);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/buildings/{seed.EmptyBuildingId}")).StatusCode);
        await ReadAsync(await manager.PostAsJsonAsync($"/api/zevs/{seed.ZevId}/registry/buildings",
            new { code = "B2", name = "Nova zgrada B" }), HttpStatusCode.Created);
        var restore = await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore", new { entityType = "Building", recordId = seed.EmptyBuildingId });
        Assert.Contains("B2", (await ReadAsync(restore, HttpStatusCode.Conflict)).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.DeleteAsync($"/api/zevs/{seed.OtherZevId}/registry/buildings/{seed.OtherBuildingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/registry/buildings/{seed.OtherBuildingId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/expenses/suppliers/{seed.SupplierId}")).StatusCode);
        var suppliers = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/expenses/suppliers"), HttpStatusCode.OK);
        Assert.Empty(suppliers.GetProperty("items").EnumerateArray());
        var bin = await ReadAsync(await admin.GetAsync("/api/admin/recycle-bin?module=Finansije"), HttpStatusCode.OK);
        Assert.Equal("Dobavljači", Assert.Single(bin.GetProperty("items").EnumerateArray()).GetProperty("form").GetString());
    }

    [Fact]
    public void DependencyCheckCoversForeignKeysAndNamedReferences()
    {
        using var factory = new TestFactory();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ZevDbContext>().Model;
        string[] Names(Type type) => SoftDeleteAdminService.References(model, type)
            .Select(item => $"{item.EntityType.ClrType.Name}.{item.Property}").ToArray();

        var person = Names(typeof(Person));
        Assert.Contains("Ownership.PersonId", person);
        Assert.Contains("Occupancy.PersonId", person);
        Assert.Contains("PaymentIdentificationHistory.PreviousPersonId", person);
        Assert.Contains("PaymentIdentificationHistory.NewPersonId", person);
        Assert.Contains("Entrance.BuildingId", Names(typeof(Building)));
        Assert.Contains("PropertyUnit.BuildingId", Names(typeof(Building)));
        Assert.Contains("PropertyUnit.EntranceId", Names(typeof(Entrance)));
        Assert.Contains("Charge.PropertyUnitId", Names(typeof(PropertyUnit)));
        Assert.Contains("Expense.SupplierId", Names(typeof(Supplier)));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static HttpClient Client(TestFactory factory, Guid userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        return client;
    }

    private sealed record Seed(Guid ZevId, Guid OtherZevId, Guid ManagerId, Guid SystemAdminId, Guid BuildingId,
        Guid EmptyBuildingId, Guid OtherBuildingId, Guid EmptyEntranceId, Guid OwnedUnitId, Guid EmptyUnitId, Guid OwnerId,
        Guid LoosePersonId, Guid SupplierId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "RB", Name = "Recycle" };
        var zev = new ZevEntity { Organization = organization, Code = "RB1", Name = "ZEV 1" };
        var other = new ZevEntity { Organization = organization, Code = "RB2", Name = "ZEV 2" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var admin = User("sysadmin");
        admin.IsSystemAdministrator = true;
        Building NewBuilding(ZevEntity owner, string code, string name) =>
            new() { OrganizationId = organization.Id, ZevId = owner.Id, Code = code, Name = name };
        var building = NewBuilding(zev, "B1", "Zgrada A");
        var emptyBuilding = NewBuilding(zev, "B2", "Zgrada B");
        var otherBuilding = NewBuilding(other, "B1", "Tuđa zgrada");
        Entrance NewEntrance(string code) => new() { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id, Code = code, Name = $"Ulaz {code}" };
        var entrance = NewEntrance("E1");
        var emptyEntrance = NewEntrance("E2");
        PropertyUnit NewUnit(Entrance parent, string code) => new() { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = parent.Id, Code = code, Type = PropertyUnitType.Apartment, AreaSquareMeters = 50 };
        var ownedUnit = NewUnit(entrance, "S1");
        var emptyUnit = NewUnit(emptyEntrance, "S2");
        var owner = new Person { OrganizationId = organization.Id, Kind = PersonKind.Individual, DisplayName = "Vlasnik" };
        var loose = new Person { OrganizationId = organization.Id, Kind = PersonKind.Individual, DisplayName = "Pogrešan unos" };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Dobavljač d.o.o." };
        database.AddRange(organization, zev, other, manager, admin, building, emptyBuilding, otherBuilding, entrance, emptyEntrance,
            ownedUnit, emptyUnit, owner, loose, supplier,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnitId = ownedUnit.Id, PersonId = owner.Id,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1 },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(zev.Id, other.Id, manager.Id, admin.Id, building.Id, emptyBuilding.Id, otherBuilding.Id, emptyEntrance.Id,
            ownedUnit.Id, emptyUnit.Id, owner.Id, loose.Id, supplier.Id);
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
