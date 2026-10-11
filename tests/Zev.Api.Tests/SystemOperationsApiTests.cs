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
using Zev.Api.Payments;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

// System error log (client reports, unhandled API exceptions, administration), version history and the
// document types added for the DOCUMENT_CATEGORY code list.
public sealed class SystemOperationsApiTests
{
    [Fact]
    public async Task ClientErrorsAreAcceptedEvenAnonymouslyAndClipped()
    {
        using var factory = new TestFactory();
        await SeedAsync(factory);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var accepted = await anonymous.PostAsJsonAsync("/api/system/errors/client", new
        {
            title = new string('N', 500), errorType = "TypeError", message = "Cannot read properties of undefined (reading 'map')",
            stackTrace = "at PaymentsPage (PaymentsPage.tsx:120)", url = "https://zev.example/uplate"
        });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/system/errors/client",
            new { title = "Bez poruke", message = " " })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var entry = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().SystemErrorLogs.SingleAsync();
        Assert.Equal(SystemErrorSource.Frontend, entry.Source);
        Assert.Equal(SystemErrorStatus.Active, entry.Status);
        Assert.Equal(SystemErrorLog.TitleLength, entry.Title.Length);
        Assert.Equal("TypeError", entry.ErrorType);
        Assert.Equal("https://zev.example/uplate", entry.RequestPath);
        Assert.Null(entry.UserId);
    }

    [Fact]
    public async Task UnhandledApiExceptionIsLoggedAndAnsweredWithErrorId()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);

        var response = await manager.PostAsJsonAsync($"/api/zevs/{seed.ZevId}/payments/statements/preview", new
        {
            bankAccountId = Guid.NewGuid(), fileName = "izvod.boom", format = "BOOM",
            contentBase64 = Convert.ToBase64String("x"u8.ToArray())
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadAsync(response, HttpStatusCode.InternalServerError);
        var errorId = problem.GetProperty("errorId").GetInt32();

        using var scope = factory.Services.CreateScope();
        var entry = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().SystemErrorLogs.SingleAsync(item => item.Id == errorId);
        Assert.Equal(SystemErrorSource.Backend, entry.Source);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ErrorType);
        Assert.Equal("Parser je pao.", entry.Message);
        Assert.Equal($"POST /api/zevs/{seed.ZevId}/payments/statements/preview", entry.Title);
        Assert.Equal(seed.ManagerId, entry.UserId);
        Assert.Contains("ThrowingParser", entry.StackTrace);
    }

    [Fact]
    public async Task AdministratorFiltersAndResolvesErrors()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var administrator = Client(factory, seed.SystemAdminId);
        using var manager = Client(factory, seed.ManagerId);
        int resolvedId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var old = new SystemErrorLog { Title = "Stara", Source = SystemErrorSource.Backend, ErrorType = "X", Message = "m",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2) };
            var recent = new SystemErrorLog { Title = "Nova", Source = SystemErrorSource.Frontend, ErrorType = "Y", Message = "m" };
            database.SystemErrorLogs.AddRange(old, recent);
            await database.SaveChangesAsync();
            resolvedId = old.Id;
        }

        var active = await ReadAsync(await administrator.GetAsync("/api/system/errors"), HttpStatusCode.OK);
        Assert.Equal(["Nova", "Stara"], active.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()));
        Assert.Equal("Frontend", active.GetProperty("items")[0].GetProperty("source").GetString());

        var saved = await ReadAsync(await administrator.PutAsJsonAsync($"/api/system/errors/{resolvedId}",
            new { resolved = true, adminNotes = "  Riješeno u v2.12. " }), HttpStatusCode.OK);
        Assert.Equal("Resolved", saved.GetProperty("status").GetString());
        Assert.Equal("Riješeno u v2.12.", saved.GetProperty("adminNotes").GetString());
        Assert.Equal(seed.SystemAdminId, saved.GetProperty("resolvedByUserId").GetGuid());
        Assert.Equal(1, (await ReadAsync(await administrator.GetAsync("/api/system/errors?status=active"), HttpStatusCode.OK))
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await ReadAsync(await administrator.GetAsync("/api/system/errors?status=resolved"), HttpStatusCode.OK))
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await ReadAsync(await administrator.GetAsync("/api/system/errors?status=all"), HttpStatusCode.OK))
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.GetAsync("/api/system/errors?status=x")).StatusCode);

        // Reopening clears the resolution; unknown id is 404; long notes are rejected.
        var reopened = await ReadAsync(await administrator.PutAsJsonAsync($"/api/system/errors/{resolvedId}",
            new { resolved = false, adminNotes = "Ponovo se javlja." }), HttpStatusCode.OK);
        Assert.Equal("Active", reopened.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsJsonAsync("/api/system/errors/999999",
            new { resolved = true, adminNotes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PutAsJsonAsync($"/api/system/errors/{resolvedId}",
            new { resolved = true, adminNotes = new string('x', 2001) })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/system/errors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/system/errors/{resolvedId}",
            new { resolved = true, adminNotes = "x" })).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal(2, await check.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs
            .CountAsync(item => item.Action == "SystemErrorUpdated" && item.ActorUserId == seed.SystemAdminId));
    }

    [Fact]
    public async Task VersionHistoryIsSortedNumericallyAndNotesAreSaved()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var administrator = Client(factory, seed.SystemAdminId);
        using var manager = Client(factory, seed.ManagerId);

        var versions = await ReadAsync(await administrator.GetAsync("/api/system/versions"), HttpStatusCode.OK);
        Assert.Equal(["v2.15", "v2.14", "v2.13", "v2.12", "v2.11", "v2.10", "v2.9", "v2.8", "v2.7"],
            versions.EnumerateArray().Select(item => item.GetProperty("version").GetString()));
        Assert.Equal("installed", versions[0].GetProperty("status").GetString());

        var saved = await ReadAsync(await administrator.PutAsJsonAsync("/api/system/versions/v2.10/notes",
            new { adminNotes = "Ikonice provjerene na UAT-u." }), HttpStatusCode.OK);
        Assert.Equal("Ikonice provjerene na UAT-u.", saved.GetProperty("adminNotes").GetString());
        var read = await ReadAsync(await administrator.GetAsync("/api/system/versions"), HttpStatusCode.OK);
        Assert.Equal("Ikonice provjerene na UAT-u.", read.EnumerateArray()
            .Single(item => item.GetProperty("version").GetString() == "v2.10").GetProperty("adminNotes").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsJsonAsync("/api/system/versions/v9.9/notes",
            new { adminNotes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/system/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync("/api/system/versions/v2.10/notes",
            new { adminNotes = "x" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs
            .Where(item => item.Action == "VersionNotesUpdated" && item.EntityId == "v2.10").ToListAsync());
    }

    [Fact]
    public async Task CommunityDocumentsAcceptInsurancePolicyAndDecision()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var path = $"/api/zevs/{seed.ZevId}/community/documents";
        foreach (var type in new[] { "InsurancePolicy", "Decision" })
            Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync(path, new { title = $"Dokument {type}", type,
                visibility = "ZevMembers", fileId = seed.FileId, personId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(path, new { title = "X", type = "INSURANCE_POLICY",
            visibility = "ZevMembers", fileId = seed.FileId, personId = (Guid?)null })).StatusCode);
        var list = await ReadAsync(await manager.GetAsync(path), HttpStatusCode.OK);
        Assert.Equal(["Decision", "InsurancePolicy"], list.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString()).Order());
    }

    private sealed class ThrowingParser : IBankStatementParser
    {
        public string Format => "BOOM";
        public IReadOnlyList<ParsedBankTransaction> Parse(string content) => throw new InvalidOperationException("Parser je pao.");
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

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid SystemAdminId, Guid FileId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "OPS", Name = "Operacije" };
        var zev = new ZevEntity { Organization = organization, Code = "OPS", Name = "ZEV" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var manager = User("manager"); var admin = User("sysadmin");
        admin.IsSystemAdministrator = true;
        var file = new StoredFile { OrganizationId = organization.Id, ZevId = zev.Id, UploadedByUserId = manager.Id,
            OriginalFileName = "polisa.pdf", ContentType = "application/pdf", Sha256 = new string('a', 64), Length = 10,
            StorageKey = "test/polisa.pdf" };
        database.AddRange(organization, zev, manager, admin, file,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, admin.Id, file.Id);
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
                services.AddSingleton<IBankStatementParser, ThrowingParser>();
            });
        }
    }
}
