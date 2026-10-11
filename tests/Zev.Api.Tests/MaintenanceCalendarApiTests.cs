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

// Maintenance calendar: 60-day generation window, Planned / Due / Completed statuses, completion through work orders.
public sealed class MaintenanceCalendarApiTests
{
    [Fact]
    public async Task ShortIntervalCreatesOnlyTheNextSixtyDaysAndFutureDatesArePlanned()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Čišćenje stubišta", category = "Higijena",
            frequencyDays = 3, firstDueOn = today.AddDays(2) }), HttpStatusCode.Created);
        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Pregled lifta", category = "Lift",
            frequencyDays = 7, firstDueOn = today.AddDays(-14) }), HttpStatusCode.Created);

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var stairs = await database.ScheduledMaintenances.Where(item => item.Plan.Title == "Čišćenje stubišta").ToListAsync();
            Assert.Equal(20, stairs.Count);
            Assert.True(stairs.Max(item => item.DueOn) <= today.AddDays(60));
            var lift = await database.ScheduledMaintenances.Where(item => item.Plan.Title == "Pregled lifta")
                .Select(item => item.DueOn).OrderBy(item => item).ToListAsync();
            Assert.Equal(today, lift[0]);
        }

        var calendar = (await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK)).EnumerateArray().ToList();
        string Status(JsonElement item) => item.GetProperty("status").GetString()!;
        DateOnly Due(JsonElement item) => DateOnly.Parse(item.GetProperty("dueOn").GetString()!);
        Assert.All(calendar.Where(item => Due(item) > today), item => Assert.Equal("Planned", Status(item)));
        Assert.All(calendar.Where(item => Due(item) <= today), item => Assert.Equal("Due", Status(item)));
        Assert.Contains(calendar, item => Due(item) == today && item.GetProperty("title").GetString() == "Pregled lifta");
        Assert.Equal(calendar.OrderBy(Due).Select(Due), calendar.Select(Due));
    }

    [Fact]
    public async Task OccurrenceIsCompletedWhenItsWorkOrderIsCompleted()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Servis pumpe", category = "Vodovod",
            frequencyDays = 30, firstDueOn = today }), HttpStatusCode.Created);
        var occurrence = (await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK))[0];
        Assert.Equal("Due", occurrence.GetProperty("status").GetString());

        var order = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { title = "Servis pumpe",
            description = "Redovni servis", priority = "Normal" }), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        await ReadAsync(await manager.PatchAsJsonAsync($"{prefix}/calendar/{occurrence.GetProperty("id").GetGuid()}/status",
            new { status = "InProgress", workOrderId = orderId }), HttpStatusCode.OK);
        async Task<string> Move(string status)
        {
            var list = await ReadAsync(await manager.GetAsync($"{prefix}/work-orders"), HttpStatusCode.OK);
            var rowVersion = list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == orderId)
                .GetProperty("rowVersion").GetString();
            await ReadAsync(await manager.PatchAsJsonAsync($"{prefix}/work-orders/{orderId}/status", new { status, rowVersion }),
                HttpStatusCode.OK);
            return (await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK)).EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == occurrence.GetProperty("id").GetGuid()).GetProperty("status").GetString()!;
        }
        Assert.Equal("InProgress", await Move("InProgress"));
        Assert.Equal("Completed", await Move("Completed"));
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
