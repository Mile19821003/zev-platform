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
using Zev.Api.Maintenance;

// Maintenance plan lifecycle: delete (only without realized work), deactivate / reactivate, restore from "Obrisani redovi".
public sealed class MaintenancePlanLifecycleApiTests
{
    private static async Task<Guid> CreatePlanAsync(HttpClient client, Guid zevId, string title, int frequencyDays, DateOnly firstDueOn)
    {
        await ReadAsync(await client.PostAsJsonAsync($"/api/zevs/{zevId}/maintenance/plans",
            new { title, category = "Opće", frequencyDays, firstDueOn }), HttpStatusCode.Created);
        var plans = await ReadAsync(await client.GetAsync($"/api/zevs/{zevId}/maintenance/plans"), HttpStatusCode.OK);
        return plans.EnumerateArray().Single(item => item.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
    }

    private static async Task<int> OccurrencesAsync(TestFactory factory, Guid planId, Func<ScheduledMaintenance, bool>? filter = null)
    {
        using var scope = factory.Services.CreateScope();
        var items = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().ScheduledMaintenances
            .Where(item => item.PlanId == planId).ToListAsync();
        return items.Count(filter ?? (_ => true));
    }

    [Fact]
    public async Task PlanWithoutWorkIsDeletedWithItsOccurrencesAndRestoredWithNewOnes()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var admin = Client(factory, seed.SystemAdminId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var planId = await CreatePlanAsync(manager, seed.ZevId, "Čišćenje oluka", 7, today.AddDays(-3));
        Assert.True(await OccurrencesAsync(factory, planId) > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/zevs/{seed.ZevId}/maintenance/plans/{planId}")).StatusCode);
        Assert.Equal(0, await OccurrencesAsync(factory, planId));
        var plans = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/maintenance/plans"), HttpStatusCode.OK);
        Assert.Empty(plans.EnumerateArray());
        var calendar = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/maintenance/calendar"), HttpStatusCode.OK);
        Assert.Empty(calendar.EnumerateArray());

        var bin = await ReadAsync(await admin.GetAsync("/api/admin/recycle-bin?module=Održavanje"), HttpStatusCode.OK);
        var row = Assert.Single(bin.GetProperty("items").EnumerateArray());
        Assert.Equal("Planovi održavanja", row.GetProperty("form").GetString());
        Assert.Equal("Čišćenje oluka", row.GetProperty("label").GetString());
        Assert.Contains(bin.GetProperty("types").EnumerateArray(), item => item.GetProperty("entityType").GetString() == "MaintenancePlan");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/recycle-bin/restore",
            new { entityType = "MaintenancePlan", recordId = planId })).StatusCode);
        plans = await ReadAsync(await manager.GetAsync($"/api/zevs/{seed.ZevId}/maintenance/plans"), HttpStatusCode.OK);
        Assert.True(Assert.Single(plans.EnumerateArray()).GetProperty("isActive").GetBoolean());
        Assert.True(await OccurrencesAsync(factory, planId) > 0, "restore regenerates the coming occurrences");
        Assert.True(await OccurrencesAsync(factory, planId, item => item.DueOn > today.AddDays(MaintenanceScheduleService.HorizonDays)) == 0);

        using var scope = factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs
            .Where(item => item.EntityType == nameof(MaintenancePlan) && item.EntityId == planId.ToString())
            .Select(item => item.Action).ToListAsync();
        Assert.Contains("RecordDeleted", actions);
        Assert.Contains("RecordRestored", actions);
    }

    [Fact]
    public async Task PlanWithWorkOrderCannotBeDeletedButCanBeDeactivatedAndStartedAgain()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var planId = await CreatePlanAsync(manager, seed.ZevId, "Servis lifta", 7, today.AddDays(-14));
        var calendar = await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK);
        var due = calendar.EnumerateArray().First(item => item.GetProperty("status").GetString() == "Due").GetProperty("id").GetGuid();
        var order = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { title = "Servis lifta",
            description = "Redovni servis", priority = "Normal", costAmount = 120m }), HttpStatusCode.Created);
        await ReadAsync(await manager.PatchAsJsonAsync($"{prefix}/calendar/{due}/status",
            new { status = "InProgress", workOrderId = order.GetProperty("id").GetGuid() }), HttpStatusCode.OK);

        var refused = await manager.DeleteAsync($"{prefix}/plans/{planId}");
        Assert.Equal(MaintenanceScheduleService.RealizedWorkMessage,
            (await ReadAsync(refused, HttpStatusCode.Conflict)).GetProperty("error").GetString());
        var before = await OccurrencesAsync(factory, planId);

        var stopped = await ReadAsync(await manager.PutAsync($"{prefix}/plans/{planId}/toggle-active", null), HttpStatusCode.OK);
        Assert.False(stopped.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, await OccurrencesAsync(factory, planId, item => item.Status == ScheduleStatus.Due));
        Assert.True(await OccurrencesAsync(factory, planId, item => item.WorkOrderId != null) == 1, "work in progress is kept");
        Assert.True(await OccurrencesAsync(factory, planId) < before);
        // Reading the calendar must not regenerate a stopped plan.
        var afterRead = await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK);
        Assert.DoesNotContain(afterRead.EnumerateArray(), item => item.GetProperty("status").GetString() is "Planned" or "Due");
        Assert.False((await ReadAsync(await manager.GetAsync($"{prefix}/plans"), HttpStatusCode.OK))[0].GetProperty("isActive").GetBoolean());

        var started = await ReadAsync(await manager.PutAsync($"{prefix}/plans/{planId}/toggle-active", null), HttpStatusCode.OK);
        Assert.True(started.GetProperty("isActive").GetBoolean());
        Assert.True(await OccurrencesAsync(factory, planId, item => item.Status == ScheduleStatus.Due && item.DueOn > today) > 0);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"{prefix}/plans/{Guid.NewGuid()}")).StatusCode);
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

    private sealed record Seed(Guid ZevId, Guid SystemAdminId, Guid OrganizationAdminId, Guid ManagerId, Guid OutsiderId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "IMG", Name = "Slike" };
        var other = new Organization { Code = "OTH", Name = "Druga" };
        var zev = new ZevEntity { Organization = organization, Code = "IMG", Name = "Zgrada sa slikom" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var admin = User("sysadmin"); var organizationAdmin = User("orgadmin"); var manager = User("manager"); var outsider = User("outsider");
        admin.IsSystemAdministrator = true;
        database.AddRange(organization, other, zev, admin, organizationAdmin, manager, outsider,
            new Membership { User = organizationAdmin, Organization = organization, Role = MembershipRole.OrganizationAdministrator },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = outsider, Organization = other, Role = MembershipRole.OrganizationAdministrator });
        await database.SaveChangesAsync();
        return new(zev.Id, admin.Id, organizationAdmin.Id, manager.Id, outsider.Id);
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
