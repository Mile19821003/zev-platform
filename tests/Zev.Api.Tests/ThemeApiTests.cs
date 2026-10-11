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

// GET /api/system/theme for everyone (also before sign-in); PUT and reset only for system administrators.
public sealed class ThemeApiTests
{
    private const string Path = "/api/system/theme";

    [Fact]
    public async Task EveryoneReadsTheFactoryPaletteAndPresets()
    {
        using var factory = new TestFactory();
        await SeedAsync(factory);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var theme = await ReadAsync(await anonymous.GetAsync(Path), HttpStatusCode.OK);
        Assert.Equal("Default", theme.GetProperty("activeThemeName").GetString());
        Assert.Equal("#176d50", theme.GetProperty("primaryColor").GetString());
        Assert.Equal("#102e31", theme.GetProperty("menuBgColor").GetString());
        Assert.Equal("#e4e9ec", theme.GetProperty("borderColor").GetString());
        var presets = theme.GetProperty("presets").EnumerateArray().ToList();
        Assert.Equal(["Default ERP", "Tamna / Tamnoplava", "Visoki kontrast"], presets.Select(item => item.GetProperty("label").GetString()));
        Assert.All(presets, preset => Assert.Equal(12, preset.GetProperty("colors").EnumerateObject().Count()));
        Assert.Equal("#176d50", presets[0].GetProperty("colors").GetProperty("primaryColor").GetString());
    }

    [Fact]
    public async Task SystemAdministratorChangesAndResetsThePaletteWithAudit()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var administrator = Client(factory, seed.SystemAdminId);
        var current = await ReadAsync(await administrator.GetAsync(Path), HttpStatusCode.OK);
        var updatedAt = current.GetProperty("updatedAt").GetDateTimeOffset();
        object Change(string name = "DarkNavy", string primary = "#3B82F6", string background = "#0f172a", DateTimeOffset? seen = null) => new
        {
            activeThemeName = name, primaryColor = primary, secondaryColor = "#94a3b8", accentColor = "#1e3a5f",
            backgroundColor = background, surfaceColor = "#1e293b", textColor = "#e2e8f0", headerBgColor = "#1e293b",
            headerTextColor = "#e2e8f0", menuBgColor = "#020617", menuTextColor = "#cbd5e1", menuActiveColor = "#1d4ed8",
            borderColor = "#334155", updatedAt = seen ?? updatedAt
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(Path, Change())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"{Path}/reset", new { updatedAt })).StatusCode);
        foreach (var invalid in new[] { "#12345", "red", "#1234567", "#ggg000", "url(x)" })
            Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Change(primary: invalid))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync(Path, Change(name: "Neon"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PutAsJsonAsync(Path,
            Change(seen: updatedAt.AddMinutes(-1)))).StatusCode);

        var saved = await ReadAsync(await administrator.PutAsJsonAsync(Path, Change()), HttpStatusCode.OK);
        Assert.Equal("DarkNavy", saved.GetProperty("activeThemeName").GetString());
        Assert.Equal("#3b82f6", saved.GetProperty("primaryColor").GetString());
        var read = await ReadAsync(await manager.GetAsync(Path), HttpStatusCode.OK);
        Assert.Equal("#0f172a", read.GetProperty("backgroundColor").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync($"{Path}/reset", new { updatedAt })).StatusCode);

        var custom = await ReadAsync(await administrator.PutAsJsonAsync(Path, Change(name: "Custom", primary: "#aa3300",
            seen: saved.GetProperty("updatedAt").GetDateTimeOffset())), HttpStatusCode.OK);
        var reset = await ReadAsync(await administrator.PostAsJsonAsync($"{Path}/reset",
            new { updatedAt = custom.GetProperty("updatedAt").GetDateTimeOffset() }), HttpStatusCode.OK);
        Assert.Equal("Default", reset.GetProperty("activeThemeName").GetString());
        Assert.Equal(SystemThemeSettings.Factory().Colors(), SystemThemeSettings.ColorNames
            .Select(name => reset.GetProperty(char.ToLowerInvariant(name[0]) + name[1..]).GetString()).ToArray());

        using var scope = factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs
            .Where(item => item.EntityType == nameof(SystemThemeSettings)).OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(["ThemeUpdated", "ThemeUpdated", "ThemeReset"], audit.Select(item => item.Action));
        Assert.All(audit, item => Assert.Equal(seed.SystemAdminId, item.ActorUserId));
        Assert.Contains("#aa3300", audit[2].BeforeJson);
        Assert.Contains("#176d50", audit[2].AfterJson);
    }

    [Fact]
    public void PresetsCoverEveryColourWithValidHex()
    {
        Assert.Equal(12, SystemThemeSettings.ColorNames.Length);
        Assert.Equal(["Default", "DarkNavy", "HighContrast", "Custom"], SystemThemeSettings.ThemeNames);
        Assert.All(SystemThemeSettings.Presets, preset =>
        {
            Assert.Equal(12, preset.Colors.Length);
            Assert.All(preset.Colors, color => Assert.Equal(color, SystemThemeSettings.NormalizeColor(color)));
        });
        Assert.Equal(SystemThemeSettings.Factory().Colors(), SystemThemeSettings.Presets[0].Colors);
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
