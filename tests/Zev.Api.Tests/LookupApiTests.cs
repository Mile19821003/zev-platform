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

// Code lists: GET /api/lookups/{type} for forms, /api/admin/lookups for system administrators.
public sealed class LookupApiTests
{
    [Fact]
    public async Task ActiveItemsAreReadSortedAndScopedForEverySignedInUser()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var otherManager = Client(factory, seed.OtherManagerId);
        using var administrator = Client(factory, seed.SystemAdminId);

        var priorities = await ReadAsync(await manager.GetAsync("/api/lookups/MAINTENANCE_PRIORITY"), HttpStatusCode.OK);
        Assert.Equal(["Urgent", "High", "Normal", "Low"], Codes(priorities));
        Assert.Equal(["Hitno", "Visok", "Standardni", "Nizak"], priorities.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("Normal", priorities.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("isDefault").GetBoolean()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/lookups/NEPOSTOJECI")).StatusCode);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/lookups/PAYMENT_METHOD")).StatusCode);

        // A deactivated item disappears from forms; a ZEV-specific item is visible only within that ZEV.
        var low = await ItemAsync(administrator, "MAINTENANCE_PRIORITY", "Low");
        await ReadAsync(await administrator.PutAsJsonAsync($"/api/admin/lookups/items/{low}",
            new { name = "Nizak", sortOrder = 40, isActive = false, isDefault = false }), HttpStatusCode.OK);
        Assert.DoesNotContain("Low", Codes(await ReadAsync(await manager.GetAsync("/api/lookups/MAINTENANCE_PRIORITY"), HttpStatusCode.OK)));
        await ReadAsync(await administrator.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
            new { code = "GARDEN", name = "Održavanje zelenila", zevId = seed.ZevId }), HttpStatusCode.Created);
        Assert.Contains("GARDEN", Codes(await ReadAsync(await manager.GetAsync(
            $"/api/lookups/EXPENSE_CATEGORY?zevId={seed.ZevId}"), HttpStatusCode.OK)));
        Assert.DoesNotContain("GARDEN", Codes(await ReadAsync(await manager.GetAsync("/api/lookups/EXPENSE_CATEGORY"), HttpStatusCode.OK)));
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync($"/api/lookups/EXPENSE_CATEGORY?zevId={seed.ZevId}")).StatusCode);
    }

    [Fact]
    public async Task AdministratorAddsEditsAndDeactivatesItemsButCannotDeleteSystemItems()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var administrator = Client(factory, seed.SystemAdminId);

        var all = await ReadAsync(await administrator.GetAsync("/api/admin/lookups"), HttpStatusCode.OK);
        Assert.Equal(7, all.GetArrayLength());
        var expenses = all.EnumerateArray().Single(item => item.GetProperty("code").GetString() == "EXPENSE_CATEGORY");
        Assert.True(expenses.GetProperty("isSystem").GetBoolean());
        Assert.Equal(5, expenses.GetProperty("items").GetArrayLength());

        var created = await ReadAsync(await administrator.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
            new { code = "CLEANING", name = "  Čišćenje  " }), HttpStatusCode.Created);
        Assert.Equal("Čišćenje", created.GetProperty("name").GetString());
        Assert.Equal(60, created.GetProperty("sortOrder").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
            new { code = "CLEANING", name = "Duplikat" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
            new { code = "loša šifra", name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
            new { code = "EMPTY", name = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsJsonAsync("/api/admin/lookups/NEPOSTOJECI/items",
            new { code = "X", name = "X" })).StatusCode);

        var id = created.GetProperty("id").GetGuid();
        var edited = await ReadAsync(await administrator.PutAsJsonAsync($"/api/admin/lookups/items/{id}",
            new { name = "Čišćenje zajedničkih prostorija", sortOrder = 5, isActive = false, isDefault = false }), HttpStatusCode.OK);
        Assert.False(edited.GetProperty("isActive").GetBoolean());
        Assert.Equal(5, edited.GetProperty("sortOrder").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync($"/api/admin/lookups/items/{id}",
            new { name = "X", sortOrder = 5, isActive = false, isDefault = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsJsonAsync($"/api/admin/lookups/items/{Guid.NewGuid()}",
            new { name = "X", sortOrder = 5, isActive = true, isDefault = false })).StatusCode);

        // Making another item the default clears the previous one.
        var cash = await ItemAsync(administrator, "PAYMENT_METHOD", "Cash");
        await ReadAsync(await administrator.PutAsJsonAsync($"/api/admin/lookups/items/{cash}",
            new { name = "Gotovina", sortOrder = 10, isActive = true, isDefault = true }), HttpStatusCode.OK);
        var methods = await ReadAsync(await administrator.GetAsync("/api/lookups/PAYMENT_METHOD"), HttpStatusCode.OK);
        Assert.Equal(["Cash"], methods.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("isDefault").GetBoolean())
            .Select(item => item.GetProperty("code").GetString()));

        var conflict = await administrator.DeleteAsync($"/api/admin/lookups/items/{id}");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("deaktivirajte", await conflict.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.True(await database.LookupItems.AnyAsync(item => item.Id == id));
        var audit = await database.AuditLogs.Where(item => item.EntityType == nameof(LookupItem)).ToListAsync();
        Assert.Contains(audit, item => item.Action == "LookupItemCreated" && item.ActorUserId == seed.SystemAdminId);
        Assert.Contains(audit, item => item.Action == "LookupItemUpdated" && item.BeforeJson!.Contains("\"IsActive\":true")
            && item.AfterJson!.Contains("\"IsActive\":false"));
    }

    [Fact]
    public async Task OnlySystemAdministratorManagesCodeLists()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var organizationAdministrator = Client(factory, seed.OrgAdminId);
        var item = LookupSeed.Items.First().Id;
        foreach (var client in new[] { manager, organizationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/lookups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/lookups/EXPENSE_CATEGORY/items",
                new { code = "X", name = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/lookups/items/{item}",
                new { name = "X", sortOrder = 1, isActive = true, isDefault = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/admin/lookups/items/{item}")).StatusCode);
        }
    }

    private static IEnumerable<string?> Codes(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToList();

    private static async Task<Guid> ItemAsync(HttpClient administrator, string type, string code)
    {
        var all = await ReadAsync(await administrator.GetAsync("/api/admin/lookups"), HttpStatusCode.OK);
        return all.EnumerateArray().Single(item => item.GetProperty("code").GetString() == type).GetProperty("items")
            .EnumerateArray().Single(item => item.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
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

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid OtherManagerId, Guid OrgAdminId, Guid SystemAdminId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "LKP", Name = "Šifarnici" };
        var other = new Organization { Code = "LKP2", Name = "Drugi" };
        var zev = new ZevEntity { Organization = organization, Code = "LKP", Name = "ZEV A" };
        var otherZev = new ZevEntity { Organization = other, Code = "LKP2", Name = "ZEV B" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var otherManager = User("other"); var orgAdmin = User("orgadmin"); var admin = User("sysadmin");
        admin.IsSystemAdministrator = true;
        database.AddRange(organization, other, zev, otherZev, manager, otherManager, orgAdmin, admin,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = otherManager, Organization = other, Zev = otherZev, Role = MembershipRole.Manager },
            new Membership { User = orgAdmin, Organization = organization, Role = MembershipRole.OrganizationAdministrator });
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, otherManager.Id, orgAdmin.Id, admin.Id);
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
