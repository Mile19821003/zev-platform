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

// Cover photo of a ZEV: upload / replace / remove by ZEV administrators, validation, serving to readers.
public sealed class ZevImageApiTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[200]];
    private static readonly byte[] Jpeg = [255, 216, 255, 224, .. new byte[200]];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. new byte[200]];

    private static object Upload(string fileName, string contentType, byte[] content) =>
        new { fileName, contentType, contentBase64 = Convert.ToBase64String(content) };

    [Fact]
    public async Task AdministratorsUploadReplaceAndRemoveCoverImage()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.SystemAdminId);
        using var organizationAdmin = Client(factory, seed.OrganizationAdminId);
        using var manager = Client(factory, seed.ManagerId);
        var path = $"/api/zevs/{seed.ZevId}/image";

        var first = await ReadAsync(await admin.PostAsJsonAsync(path, Upload("zgrada.png", "image/png", Png)), HttpStatusCode.OK);
        var firstUrl = first.GetProperty("imageUrl").GetString()!;
        Assert.StartsWith($"/api/zevs/{seed.ZevId}/image?v=", firstUrl);
        Assert.Single(Directory.GetFiles(factory.StorageRoot));

        var served = await manager.GetAsync(firstUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Contains("private", served.Headers.CacheControl?.ToString());
        var mine = await ReadAsync(await manager.GetAsync("/api/me/zevs"), HttpStatusCode.OK);
        Assert.Equal(firstUrl, mine.GetProperty("items")[0].GetProperty("imageUrl").GetString());
        var listed = await ReadAsync(await admin.GetAsync($"/api/system/zevs/{seed.ZevId}"), HttpStatusCode.OK);
        Assert.Equal(firstUrl, listed.GetProperty("imageUrl").GetString());

        // Replacing removes the previous file; the URL changes so browser caches pick up the new photo.
        var second = await ReadAsync(await organizationAdmin.PostAsJsonAsync(path, Upload("zgrada.webp", "image/webp", Webp)),
            HttpStatusCode.OK);
        Assert.NotEqual(firstUrl, second.GetProperty("imageUrl").GetString());
        Assert.Single(Directory.GetFiles(factory.StorageRoot));
        Assert.Equal(Webp, await (await manager.GetAsync(path)).Content.ReadAsByteArrayAsync());
        await ReadAsync(await admin.PostAsJsonAsync(path, Upload("zgrada.JPEG", "image/jpeg", Jpeg)), HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(path)).StatusCode);
        Assert.Empty(Directory.GetFiles(factory.StorageRoot));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(path)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var zev = await database.Zevs.SingleAsync(item => item.Id == seed.ZevId);
        Assert.Null(zev.ImageUrl);
        Assert.Null(zev.ImageStorageKey);
        var audit = await database.AuditLogs.Where(item => item.EntityType == "Zev" && item.EntityId == seed.ZevId.ToString()
                && item.Action.StartsWith("ZevImage"))
            .OrderBy(item => item.Id).Select(item => new { item.Action, item.ActorUserId }).ToListAsync();
        Assert.Equal(["ZevImageUpdated", "ZevImageUpdated", "ZevImageUpdated", "ZevImageRemoved"], audit.Select(item => item.Action));
        Assert.Equal(seed.OrganizationAdminId, audit[1].ActorUserId);
    }

    [Fact]
    public async Task OnlyPhotosUpToFiveMegabytesAreAccepted()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.SystemAdminId);
        var path = $"/api/zevs/{seed.ZevId}/image";
        object[] invalid =
        [
            Upload("zgrada.gif", "image/gif", Png),
            Upload("zgrada.png", "image/jpeg", Png),
            Upload("zgrada.jpg", "image/jpeg", Png),
            Upload("dokument.png", "image/png", "%PDF-1.7 renamed"u8.ToArray()),
            Upload("zgrada.webp", "image/webp", Png),
            Upload("../zgrada.png", "image/png", Png),
            Upload("velika.png", "image/png", [.. Png, .. new byte[5_000_000]]),
            new { fileName = "zgrada.png", contentType = "image/png", contentBase64 = "nije-base64!" },
        ];
        foreach (var body in invalid)
        {
            var response = await admin.PostAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("5 MB", (await ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("error").GetString());
        }
        Assert.False(Directory.Exists(factory.StorageRoot) && Directory.GetFiles(factory.StorageRoot).Length > 0);
    }

    [Fact]
    public async Task ReadersGet403AndOutsiders404()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        using var outsider = Client(factory, seed.OutsiderId);
        using var admin = Client(factory, seed.SystemAdminId);
        var path = $"/api/zevs/{seed.ZevId}/image";

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(path, Upload("zgrada.png", "image/png", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(path, Upload("zgrada.png", "image/png", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/zevs/{Guid.NewGuid()}/image",
            Upload("zgrada.png", "image/png", Png))).StatusCode);

        await ReadAsync(await admin.PostAsJsonAsync(path, Upload("zgrada.png", "image/png", Png)), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
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
