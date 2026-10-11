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

// GET /api/system/settings for every signed-in user; PUT only for system administrators.
public sealed class SystemSettingsApiTests
{
    private const string Path = "/api/system/settings";

    [Fact]
    public async Task EverySignedInUserReadsTheSeededDefaults()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);

        var settings = await ReadAsync(await manager.GetAsync(Path), HttpStatusCode.OK);
        Assert.Equal(",", settings.GetProperty("decimalSeparator").GetString());
        Assert.Equal("DD.MM.YYYY", settings.GetProperty("dateFormat").GetString());
        Assert.Equal("13px", settings.GetProperty("fontSize").GetString());
        Assert.False(settings.TryGetProperty("defaultTheme", out _), "the theme lives only in SystemThemeSettings");
        Assert.Equal([",", "."], settings.GetProperty("options").GetProperty("decimalSeparators").EnumerateArray()
            .Select(item => item.GetString()));
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task OnlySystemAdministratorChangesSettingsAndChangeIsAudited()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var organizationAdministrator = Client(factory, seed.OrgAdminId);
        using var administrator = Client(factory, seed.SystemAdminId);
        var current = await ReadAsync(await administrator.GetAsync(Path), HttpStatusCode.OK);
        var updatedAt = current.GetProperty("updatedAt").GetDateTimeOffset();
        object Change(string separator = ".", string date = "YYYY-MM-DD", string font = "15px", DateTimeOffset? seen = null) =>
            new { decimalSeparator = separator, dateFormat = date, fontSize = font, defaultTheme = "Default", updatedAt = seen ?? updatedAt };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(Path, Change())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await organizationAdministrator.PutAsJsonAsync(Path, Change())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Change(separator: ";"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Change(date: "MM/DD/YYYY"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Change(font: "20px"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PutAsJsonAsync(Path,
            Change(seen: updatedAt.AddMinutes(-1)))).StatusCode);

        var saved = await ReadAsync(await administrator.PutAsJsonAsync(Path, Change()), HttpStatusCode.OK);
        Assert.Equal(".", saved.GetProperty("decimalSeparator").GetString());
        Assert.Equal(seed.SystemAdminId, saved.GetProperty("updatedByUserId").GetGuid());
        // Everyone reads the new values; repeating with the old version is a conflict.
        var read = await ReadAsync(await manager.GetAsync(Path), HttpStatusCode.OK);
        Assert.Equal(("." , "YYYY-MM-DD", "15px"), (read.GetProperty("decimalSeparator").GetString(),
            read.GetProperty("dateFormat").GetString(), read.GetProperty("fontSize").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PutAsJsonAsync(Path, Change(separator: ","))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var audit = await database.AuditLogs.SingleAsync(item => item.Action == "SystemSettingsUpdated");
        Assert.Equal(seed.SystemAdminId, audit.ActorUserId);
        Assert.Contains("\"DecimalSeparator\":\",\"", audit.BeforeJson);
        Assert.Contains("\"DecimalSeparator\":\".\"", audit.AfterJson);
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
        var organization = new Organization { Code = "SET", Name = "Podešavanja" };
        var zev = new ZevEntity { Organization = organization, Code = "SET", Name = "ZEV" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var orgAdmin = User("orgadmin"); var systemAdmin = User("sysadmin");
        systemAdmin.IsSystemAdministrator = true;
        database.AddRange(organization, zev, manager, orgAdmin, systemAdmin,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager },
            new Membership { User = orgAdmin, Organization = organization, Role = MembershipRole.OrganizationAdministrator });
        await database.SaveChangesAsync();
        return new(manager.Id, orgAdmin.Id, systemAdmin.Id);
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
