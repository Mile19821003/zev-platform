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

// v2.19: deleting needs its own permission (*.Delete); edit rights alone get 403 on DELETE routes.
public sealed class DeletePermissionApiTests
{
    [Fact]
    public void DeletePermissionsAreKnownAndOnlyManagersAndOrganizationAdministratorsHaveThemByDefault()
    {
        string[] keys = ["Buildings.Delete", "PropertyUnits.Delete", "Suppliers.Delete", "Maintenance.Delete"];
        foreach (var key in keys)
        {
            Assert.True(GranularPermissions.IsKnown(key), key);
            Assert.True(GranularPermissions.RoleDefault(MembershipRole.Manager, key), key);
            Assert.True(GranularPermissions.RoleDefault(MembershipRole.OrganizationAdministrator, key), key);
            Assert.False(GranularPermissions.RoleDefault(MembershipRole.Accountant, key), key);
            Assert.False(GranularPermissions.RoleDefault(MembershipRole.BuildingRepresentative, key), key);
            Assert.False(GranularPermissions.RoleDefault(MembershipRole.Owner, key), key);
        }
        Assert.Contains("Delete", GranularPermissions.Modules.Single(module => module.Name == "Maintenance").Actions);
    }

    [Fact]
    public async Task EditRightWithoutDeleteRightGets403OnEveryDeleteRoute()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var editor = Client(factory, seed.EditorId);
        var zev = $"/api/zevs/{seed.ZevId}";

        // The editor (manager with every *.Delete denied) still edits.
        await ReadAsync(await editor.PutAsync($"{zev}/maintenance/plans/{seed.PlanId}/toggle-active", null), HttpStatusCode.OK);
        foreach (var path in new[] { $"{zev}/registry/buildings/{seed.BuildingId}", $"{zev}/registry/entrances/{seed.EntranceId}",
            $"{zev}/registry/units/{seed.UnitId}", $"{zev}/expenses/suppliers/{seed.SupplierId}", $"{zev}/maintenance/plans/{seed.PlanId}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync(path)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.False(await database.Buildings.IgnoreQueryFilters().AnyAsync(item => item.IsDeleted));
        Assert.False(await database.MaintenancePlans.IgnoreQueryFilters().AnyAsync(item => item.IsDeleted));
    }

    [Fact]
    public async Task DeleteRightAloneIsEnoughAndManagersDeleteByDefault()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var deleter = Client(factory, seed.DeleterId);
        using var manager = Client(factory, seed.ManagerId);
        var zev = $"/api/zevs/{seed.ZevId}";

        // A representative granted only Buildings.Delete deletes the building but cannot edit it or delete a unit.
        Assert.Equal(HttpStatusCode.Forbidden, (await deleter.PutAsync($"{zev}/maintenance/plans/{seed.PlanId}/toggle-active", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deleter.DeleteAsync($"{zev}/registry/units/{seed.UnitId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await deleter.DeleteAsync($"{zev}/registry/buildings/{seed.EmptyBuildingId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{zev}/registry/units/{seed.UnitId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{zev}/expenses/suppliers/{seed.SupplierId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{zev}/maintenance/plans/{seed.PlanId}")).StatusCode);

        var permissions = await ReadAsync(await manager.GetAsync($"{zev}/access/permissions"), HttpStatusCode.OK);
        Assert.Contains(permissions.GetProperty("permissions").EnumerateArray(), item =>
            item.GetProperty("key").GetString() == "Maintenance.Delete" && item.GetProperty("effective").GetBoolean());
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

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid EditorId, Guid DeleterId, Guid BuildingId, Guid EmptyBuildingId,
        Guid EntranceId, Guid UnitId, Guid SupplierId, Guid PlanId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "DEL", Name = "Brisanje" };
        var zev = new ZevEntity { Organization = organization, Code = "DEL", Name = "ZEV" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var editor = User("editor"); var deleter = User("deleter");
        var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Code = "B1", Name = "Zgrada" };
        var emptyBuilding = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Code = "B2", Name = "Prazna zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id, Code = "E1", Name = "Ulaz" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id, EntranceId = entrance.Id,
            Code = "S1", Type = PropertyUnitType.Apartment, AreaSquareMeters = 50 };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Dobavljač" };
        var plan = new MaintenancePlan { OrganizationId = organization.Id, ZevId = zev.Id, Title = "Plan", Category = "Opće",
            FrequencyDays = 30, FirstDueOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5) };
        AccessOverride Override(AppUser user, string key, AccessEffect effect) => new()
        {
            UserId = user.Id, OrganizationId = organization.Id, ZevId = zev.Id, PermissionKey = key, Effect = effect,
            ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = manager.Id, Reason = "Test prava brisanja"
        };
        database.AddRange(organization, zev, manager, editor, deleter, building, emptyBuilding, entrance, unit, supplier, plan,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = editor, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = deleter, Organization = organization, Zev = zev, Role = MembershipRole.BuildingRepresentative },
            Override(editor, "Buildings.Delete", AccessEffect.Deny), Override(editor, "PropertyUnits.Delete", AccessEffect.Deny),
            Override(editor, "Suppliers.Delete", AccessEffect.Deny), Override(editor, "Maintenance.Delete", AccessEffect.Deny),
            Override(deleter, "Buildings.Delete", AccessEffect.Grant));
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, editor.Id, deleter.Id, building.Id, emptyBuilding.Id, entrance.Id, unit.Id, supplier.Id, plan.Id);
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();
        public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"zev-images-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=TestOnly;Trusted_Connection=True",
                ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Storage:Root"] = StorageRoot
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        }
    }
}
