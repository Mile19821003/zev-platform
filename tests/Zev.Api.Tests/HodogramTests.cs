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
using Zev.Api.Hodograms;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

public sealed class HodogramTests
{
    [Fact]
    public void ValidatorAcceptsOneResponsibleOneAccountableAndOptionalConsultedInformed()
    {
        var step = Guid.NewGuid();
        Assert.Empty(RaciValidator.Validate([step], [(step, RaciValue.R), (step, RaciValue.A)]));
        Assert.Empty(RaciValidator.Validate([step], [(step, RaciValue.R), (step, RaciValue.R), (step, RaciValue.A),
            (step, RaciValue.C), (step, RaciValue.C), (step, RaciValue.I), (step, RaciValue.I)]));
    }

    [Fact]
    public void ValidatorReportsMissingResponsibleAndAccountablePerStep()
    {
        var empty = Guid.NewGuid();
        var noResponsible = Guid.NewGuid();
        var twoAccountable = Guid.NewGuid();
        var issues = RaciValidator.Validate([empty, noResponsible, twoAccountable],
        [
            (noResponsible, RaciValue.A), (noResponsible, RaciValue.C),
            (twoAccountable, RaciValue.R), (twoAccountable, RaciValue.A), (twoAccountable, RaciValue.A)
        ]);
        Assert.Equal(new[]
        {
            (empty, RaciValidator.MissingResponsible), (empty, RaciValidator.MissingAccountable),
            (noResponsible, RaciValidator.MissingResponsible), (twoAccountable, RaciValidator.MultipleAccountable)
        }, issues.Select(issue => (issue.StepId, issue.Code)));
    }

    [Fact]
    public async Task OrganizationAdministratorBuildsValidatedMatrixWithRevisionChecks()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.AdminUserId);
        var root = $"/api/organizations/{seed.OrganizationId}/hodograms";

        var created = await ReadAsync(await client.PostAsJsonAsync(root,
            new { name = "Upravljanje ZEV-om", description = "Godišnji ciklus" }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(Enum.GetNames<MembershipRole>(), created.GetProperty("roles").EnumerateArray()
            .Select(item => item.GetString()));
        var revision = created.GetProperty("revision").GetInt32();

        var withPhase = await ReadAsync(await client.PostAsJsonAsync($"{root}/{id}/phases",
            new { name = "Planiranje", revision }), HttpStatusCode.OK);
        var phaseId = withPhase.GetProperty("phases")[0].GetProperty("id").GetGuid();
        revision = withPhase.GetProperty("revision").GetInt32();
        var oneStep = await ReadAsync(await client.PostAsJsonAsync($"{root}/{id}/phases/{phaseId}/steps",
            new { name = "Priprema plana", revision }), HttpStatusCode.OK);
        revision = oneStep.GetProperty("revision").GetInt32();
        var twoSteps = await ReadAsync(await client.PostAsJsonAsync($"{root}/{id}/phases/{phaseId}/steps",
            new { name = "Usvajanje plana", revision }), HttpStatusCode.OK);
        revision = twoSteps.GetProperty("revision").GetInt32();
        var steps = twoSteps.GetProperty("phases")[0].GetProperty("steps");
        var prepare = steps[0].GetProperty("id").GetGuid();
        var adopt = steps[1].GetProperty("id").GetGuid();
        Assert.False(twoSteps.GetProperty("isValid").GetBoolean());
        Assert.Equal(4, twoSteps.GetProperty("issues").GetArrayLength());

        var invalid = await client.PutAsJsonAsync($"{root}/{id}/matrix", new { revision, cells = new object[]
        {
            new { stepId = prepare, role = "Manager", value = "R" },
            new { stepId = prepare, role = "OrganizationAdministrator", value = "A" },
            new { stepId = adopt, role = "Manager", value = "A" },
            new { stepId = adopt, role = "OrganizationAdministrator", value = "A" }
        } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var invalidBody = await ReadAsync(invalid, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(new string?[] { RaciValidator.MissingResponsible, RaciValidator.MultipleAccountable },
            invalidBody.GetProperty("issues").EnumerateArray().Select(item => item.GetProperty("code").GetString()));

        var saved = await ReadAsync(await client.PutAsJsonAsync($"{root}/{id}/matrix", new { revision, cells = new object[]
        {
            new { stepId = prepare, role = "Manager", value = "R" },
            new { stepId = prepare, role = "Accountant", value = "C" },
            new { stepId = prepare, role = "BuildingRepresentative", value = "C" },
            new { stepId = prepare, role = "Owner", value = "I" },
            new { stepId = prepare, role = "OrganizationAdministrator", value = "A" },
            new { stepId = adopt, role = "Manager", value = "R" },
            new { stepId = adopt, role = "OrganizationAdministrator", value = "A" }
        } }), HttpStatusCode.OK);
        Assert.True(saved.GetProperty("isValid").GetBoolean());
        Assert.Equal(revision + 1, saved.GetProperty("revision").GetInt32());
        Assert.Equal("C", saved.GetProperty("phases")[0].GetProperty("steps")[0].GetProperty("cells")
            .GetProperty("Accountant").GetString());

        var stale = await client.PutAsJsonAsync($"{root}/{id}/matrix", new { revision, cells = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        revision = saved.GetProperty("revision").GetInt32();

        Dictionary<(Guid, MembershipRole), Guid> before;
        using (var scope = factory.Services.CreateScope())
            before = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().HodogramAssignments.AsNoTracking()
                .ToDictionaryAsync(item => (item.StepId, item.Role), item => item.Id);

        // Re-saving a changed matrix keeps untouched cells and only writes the real differences.
        var resaved = await ReadAsync(await client.PutAsJsonAsync($"{root}/{id}/matrix", new { revision, cells = new object[]
        {
            new { stepId = prepare, role = "Manager", value = "R" },
            new { stepId = prepare, role = "Accountant", value = "R" },
            new { stepId = prepare, role = "Owner", value = "I" },
            new { stepId = prepare, role = "OrganizationAdministrator", value = "A" },
            new { stepId = adopt, role = "Manager", value = "R" },
            new { stepId = adopt, role = "OrganizationAdministrator", value = "A" }
        } }), HttpStatusCode.OK);
        revision = resaved.GetProperty("revision").GetInt32();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var after = await database.HodogramAssignments.AsNoTracking().ToListAsync();
            Assert.Equal(6, after.Count);
            Assert.All(after, item => Assert.Equal(before[(item.StepId, item.Role)], item.Id));
            Assert.Equal(RaciValue.R, after.Single(item => item.StepId == prepare
                && item.Role == MembershipRole.Accountant).Value);
            Assert.DoesNotContain(after, item => item.Role == MembershipRole.BuildingRepresentative);
            Assert.Equal(1, await database.AuditLogs.CountAsync(item => item.EntityType == nameof(HodogramAssignment)
                && item.Action == "Deleted"));
            Assert.Equal(1, await database.AuditLogs.CountAsync(item => item.EntityType == nameof(HodogramAssignment)
                && item.Action == "Modified"));
        }

        var phaseWithSteps = await client.DeleteAsync($"{root}/{id}/phases/{phaseId}?revision={revision}");
        Assert.Equal(HttpStatusCode.Conflict, phaseWithSteps.StatusCode);
        var withoutAdopt = await ReadAsync(await client.DeleteAsync($"{root}/{id}/steps/{adopt}?revision={revision}"),
            HttpStatusCode.OK);
        Assert.True(withoutAdopt.GetProperty("isValid").GetBoolean());
        Assert.Equal(1, withoutAdopt.GetProperty("phases")[0].GetProperty("steps").GetArrayLength());
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ZevDbContext>().HodogramAssignments
                .AnyAsync(item => item.StepId == adopt));

        var list = await ReadAsync(await client.GetAsync(root), HttpStatusCode.OK);
        Assert.Equal(1, list[0].GetProperty("stepCount").GetInt32());
    }

    [Fact]
    public async Task MatrixRejectsUnknownRolesValuesStepsAndDuplicateCells()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.AdminUserId);
        var root = $"/api/organizations/{seed.OrganizationId}/hodograms";
        var (id, stepId, revision) = await CreateWithStepAsync(client, root);

        foreach (var cells in new[]
        {
            new object[] { new { stepId, role = "Director", value = "R" } },
            new object[] { new { stepId, role = "Manager", value = "X" } },
            new object[] { new { stepId = Guid.NewGuid(), role = "Manager", value = "R" } },
            new object[] { new { stepId, role = "Manager", value = "R" }, new { stepId, role = "Manager", value = "A" } }
        })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PutAsJsonAsync($"{root}/{id}/matrix", new { revision, cells })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(root, new { name = "Hodogram" })).StatusCode);
    }

    [Fact]
    public async Task HodogramsAreVisibleOnlyToTheirOrganizationAdministratorsAndSystemAdministrators()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        var root = $"/api/organizations/{seed.OrganizationId}/hodograms";
        Guid id;
        using (var admin = Client(factory, seed.AdminUserId))
            (id, _, _) = await CreateWithStepAsync(admin, root);

        using (var outsider = Client(factory, seed.OtherOrganizationAdminUserId))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(root)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{root}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.PostAsJsonAsync(root, new { name = "Strani hodogram" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(
                $"/api/organizations/{seed.OtherOrganizationId}/hodograms/{id}")).StatusCode);
        }
        using (var manager = Client(factory, seed.ZevManagerUserId))
            Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{root}/{id}")).StatusCode);
        using (var system = Client(factory, seed.SystemAdminUserId))
            Assert.Equal(HttpStatusCode.OK, (await system.GetAsync($"{root}/{id}")).StatusCode);
    }

    private static async Task<(Guid Id, Guid StepId, int Revision)> CreateWithStepAsync(HttpClient client, string root)
    {
        var created = await ReadAsync(await client.PostAsJsonAsync(root, new { name = "Hodogram" }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var phase = await ReadAsync(await client.PostAsJsonAsync($"{root}/{id}/phases",
            new { name = "Faza", revision = created.GetProperty("revision").GetInt32() }), HttpStatusCode.OK);
        var step = await ReadAsync(await client.PostAsJsonAsync(
            $"{root}/{id}/phases/{phase.GetProperty("phases")[0].GetProperty("id").GetGuid()}/steps",
            new { name = "Korak", revision = phase.GetProperty("revision").GetInt32() }), HttpStatusCode.OK);
        return (id, step.GetProperty("phases")[0].GetProperty("steps")[0].GetProperty("id").GetGuid(),
            step.GetProperty("revision").GetInt32());
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

    private sealed record Seed(Guid OrganizationId, Guid OtherOrganizationId, Guid AdminUserId,
        Guid OtherOrganizationAdminUserId, Guid ZevManagerUserId, Guid SystemAdminUserId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "HOD", Name = "Hodogram organizacija" };
        var other = new Organization { Code = "OTHER", Name = "Druga organizacija" };
        var zev = new ZevEntity { Organization = organization, Code = "HZ", Name = "Hodogram ZEV" };
        AppUser User(string email, bool system = false) => new() { Email = email,
            NormalizedEmail = email.ToUpperInvariant(), IsSystemAdministrator = system };
        var admin = User("hodogram-admin@example.invalid");
        var otherAdmin = User("other-admin@example.invalid");
        var manager = User("zev-manager@example.invalid");
        var system = User("system@example.invalid", true);
        database.AddRange(organization, other, zev, admin, otherAdmin, manager, system,
            new Membership { User = admin, Organization = organization, Role = MembershipRole.OrganizationAdministrator },
            new Membership { User = otherAdmin, Organization = other, Role = MembershipRole.OrganizationAdministrator },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(organization.Id, other.Id, admin.Id, otherAdmin.Id, manager.Id, system.Id);
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
