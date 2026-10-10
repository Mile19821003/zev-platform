using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
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

// GET /api/system/module-icons for every signed-in user; PUT only for system administrators, audited.
[Collection("Sql integration")]
public sealed class ModuleIconApiTests
{
    private const string Path = "/api/system/module-icons";

    [Fact]
    public async Task EverySignedInUserReadsTheIconMapAndPalette()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);

        var result = await ReadAsync(await manager.GetAsync(Path), HttpStatusCode.OK);
        var icons = result.GetProperty("icons");
        Assert.Equal(ModuleIcons.Defaults.Length, icons.EnumerateObject().Count());
        Assert.Equal("landmark", icons.GetProperty("payments").GetString());
        Assert.Equal("banknote", icons.GetProperty("category:finance").GetString());
        Assert.Equal(40, result.GetProperty("palette").GetArrayLength());
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task OnlySystemAdministratorChangesIconsWithValidKeysAndChangesAreAudited()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var organizationAdministrator = Client(factory, seed.OrgAdminId);
        using var administrator = Client(factory, seed.SystemAdminId);
        object Changes(params (string Module, string Icon)[] changes) =>
            new { changes = changes.Select(item => new { moduleKey = item.Module, iconKey = item.Icon }).ToArray() };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(Path, Changes(("payments", "credit-card")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await organizationAdministrator.PutAsJsonAsync(Path,
            Changes(("payments", "credit-card")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Changes(("payments", "rocket")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Changes(("nepostojeci", "cog")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path,
            Changes(("payments", "cog"), ("payments", "bell")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Changes())).StatusCode);

        var saved = await ReadAsync(await administrator.PutAsJsonAsync(Path,
            Changes(("payments", "credit-card"), ("category:finance", "chart-pie"), ("reports", "chart-bar"))), HttpStatusCode.OK);
        Assert.Equal("credit-card", saved.GetProperty("icons").GetProperty("payments").GetString());
        var read = await ReadAsync(await manager.GetAsync(Path), HttpStatusCode.OK);
        Assert.Equal("chart-pie", read.GetProperty("icons").GetProperty("category:finance").GetString());

        // A request without real changes is accepted but leaves no audit entry.
        await ReadAsync(await administrator.PutAsJsonAsync(Path, Changes(("payments", "credit-card"))), HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var audit = Assert.Single(await database.AuditLogs.Where(item => item.Action == "ModuleIconsUpdated").ToListAsync());
        Assert.Equal(seed.SystemAdminId, audit.ActorUserId);
        Assert.Equal("{\"payments\":\"landmark\",\"category:finance\":\"banknote\"}", audit.BeforeJson);
        Assert.Equal("{\"payments\":\"credit-card\",\"category:finance\":\"chart-pie\"}", audit.AfterJson);
        var setting = await database.ModuleIconSettings.SingleAsync(item => item.ModuleKey == "payments");
        Assert.Equal(seed.SystemAdminId, setting.UpdatedByUserId);
    }

    [Fact]
    public async Task SeedIsMigratedOnSqlServer()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = $"ZevIcons_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            Assert.Equal(ModuleIcons.Defaults.Length, await database.ModuleIconSettings.CountAsync());
            Assert.Equal("barcode", await database.ModuleIconSettings.Where(item => item.ModuleKey == "lookups")
                .Select(item => item.IconKey).SingleAsync());
        }
        finally { await database.Database.EnsureDeletedAsync(); }
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

    private sealed record Seed(Guid ManagerId, Guid OrgAdminId, Guid SystemAdminId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "ICO", Name = "Ikonice" };
        var zev = new ZevEntity { Organization = organization, Code = "ICO", Name = "ZEV" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var orgAdmin = User("orgadmin"); var admin = User("sysadmin");
        admin.IsSystemAdministrator = true;
        database.AddRange(organization, zev, manager, orgAdmin, admin,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = orgAdmin, Organization = organization, Role = MembershipRole.OrganizationAdministrator });
        await database.SaveChangesAsync();
        return new(manager.Id, orgAdmin.Id, admin.Id);
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
