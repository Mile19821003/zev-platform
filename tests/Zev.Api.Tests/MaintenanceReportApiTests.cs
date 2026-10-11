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
using Zev.Api.Endpoints;

// Phase C: year report of the maintenance module (table, costs per category, months, season timeline) and exports.
public sealed class MaintenanceReportApiTests
{
    private static async Task<JsonElement> CompleteAsync(HttpClient manager, string prefix, Guid orderId, decimal cost)
    {
        var dossier = await ReadAsync(await manager.GetAsync($"{prefix}/work-orders/{orderId}"), HttpStatusCode.OK);
        return await ReadAsync(await manager.PutAsJsonAsync($"{prefix}/work-orders/{orderId}/complete", new
        {
            completedDate = DateOnly.FromDateTime(DateTime.UtcNow), workDescription = "Urađeno.", invoiceNumber = "R-1", costAmount = cost,
            expenseFund = "Regular", recordExpense = false, rowVersion = dossier.GetProperty("rowVersion").GetString()
        }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task YearReportCombinesPlansWorkOrdersCostsAndTimelineAndExports()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = today.Year;

        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Servis lifta", category = "Lift", frequencyDays = 30, firstDueOn = today }), HttpStatusCode.Created);
        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Pregled krova", category = "Krov", frequencyDays = 90, firstDueOn = today.AddDays(10) }), HttpStatusCode.Created);
        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title = "Dezinsekcija", category = "Higijena", frequencyDays = 7, firstDueOn = today.AddDays(1) }), HttpStatusCode.Created);
        var calendar = await ReadAsync(await manager.GetAsync($"{prefix}/calendar?status=Due"), HttpStatusCode.OK);
        var scheduleId = calendar.EnumerateArray().Single().GetProperty("id").GetGuid();
        var fromPlan = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { scheduleId, supplierId = seed.SupplierId,
            title = "Servis lifta", description = "Mjesečni servis", priority = "Normal" }), HttpStatusCode.Created);
        await CompleteAsync(manager, prefix, fromPlan.GetProperty("id").GetGuid(), 200m);
        var ticket = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/tickets", new { propertyUnitId = (Guid?)null, category = "Rasvjeta",
            description = "Sijalica", location = "Ulaz", priority = "Normal", fileIds = Array.Empty<Guid>() }), HttpStatusCode.Created);
        var fromTicket = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { ticketId = ticket.GetProperty("id").GetGuid(),
            contractorName = "Električar Ado", title = "Zamjena sijalice", description = "Ulaz", priority = "Normal" }), HttpStatusCode.Created);
        await CompleteAsync(manager, prefix, fromTicket.GetProperty("id").GetGuid(), 50m);

        var report = await ReadAsync(await manager.GetAsync($"{prefix}/reports/summary?year={year}"), HttpStatusCode.OK);
        Assert.Equal(year, report.GetProperty("year").GetInt32());
        Assert.Equal(250m, report.GetProperty("totalCost").GetDecimal());
        Assert.Equal(2, report.GetProperty("realized").GetInt32());
        var costs = report.GetProperty("costByCategory").EnumerateArray().ToDictionary(item => item.GetProperty("category").GetString()!,
            item => item.GetProperty("cost").GetDecimal());
        Assert.Equal(200m, costs["Lift"]);
        Assert.Equal(50m, costs["Rasvjeta"]);
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        var done = rows.Single(item => item.GetProperty("title").GetString() == "Servis lifta" && item.GetProperty("status").GetString() == "Completed");
        Assert.Equal("Dobavljač d.o.o.", done.GetProperty("contractor").GetString());
        Assert.Equal(200m, done.GetProperty("cost").GetDecimal());
        var fix = rows.Single(item => item.GetProperty("source").GetString() == "Prijava");
        Assert.Equal("Električar Ado", fix.GetProperty("contractor").GetString());
        Assert.Equal("Rasvjeta", fix.GetProperty("category").GetString());
        var months = report.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, months.Count);
        Assert.Equal(2, months[today.Month - 1].GetProperty("realized").GetInt32());
        Assert.Equal(250m, months[today.Month - 1].GetProperty("cost").GetDecimal());
        Assert.Equal(report.GetProperty("planned").GetInt32(), months.Sum(item => item.GetProperty("planned").GetInt32()));
        var gantt = report.GetProperty("gantt").EnumerateArray().ToList();
        Assert.Equal(["Higijena", "Krov", "Lift"], gantt.Select(item => item.GetProperty("category").GetString()));
        var liftMarks = gantt[2].GetProperty("marks").EnumerateArray().ToList();
        Assert.All(liftMarks, item => Assert.Equal(year, DateOnly.Parse(item.GetProperty("date").GetString()!).Year));
        Assert.Contains(liftMarks, item => item.GetProperty("status").GetString() == "Completed");
        // Dates beyond the stored 60 days are projected (marked), never earlier ones.
        Assert.All(gantt.SelectMany(plan => plan.GetProperty("marks").EnumerateArray()).Where(item => item.GetProperty("projected").GetBoolean()),
            item => Assert.True(DateOnly.Parse(item.GetProperty("date").GetString()!) > today.AddDays(60)));
        if (today.AddDays(68).Year == year)
            Assert.Contains(gantt.Single(plan => plan.GetProperty("category").GetString() == "Higijena").GetProperty("marks").EnumerateArray(),
                item => item.GetProperty("projected").GetBoolean());

        var xlsx = await manager.GetAsync($"{prefix}/reports/{year}.xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("odrzavanje-" + year + ".xlsx", xlsx.Content.Headers.ContentDisposition?.FileNameStar ?? xlsx.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("PK"u8.ToArray(), (await xlsx.Content.ReadAsByteArrayAsync())[..2]);
        var pdf = await manager.GetAsync($"{prefix}/reports/{year}.pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF"u8.ToArray(), (await pdf.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{prefix}/reports/summary?year=1999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/zevs/{Guid.NewGuid()}/maintenance/reports/summary")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var exports = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs
            .Where(item => item.EntityType == "MaintenanceReport" && item.Action == "Export").Select(item => item.AfterJson).ToListAsync();
        Assert.Equal(2, exports.Count);
        Assert.Contains(exports, item => item!.Contains("\"pdf\""));
    }

    [Fact]
    public void AssemblyPdfListsTotalsCategoriesMonthsAndJobs()
    {
        var report = new Zev.Api.Maintenance.MaintenanceReport(2026,
            [new("Plan", "Servis lifta", "Lift", new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), "Completed", "Lift d.o.o.", "R-1", 200m, "Regular", false),
             new("Plan", "Servis lifta", "Lift", new DateOnly(2026, 12, 10), null, "Planned", null, null, null, null, true)],
            [new("Lift", 200m, 1)], [.. Enumerable.Range(1, 12).Select(month => new Zev.Api.Maintenance.MaintenanceMonth(month,
                month is 10 or 12 ? 1 : 0, month == 10 ? 1 : 0, month == 10 ? 200m : 0m))], [], 2, 1, 200m);
        var lines = MaintenanceReportEndpoints.PdfLines("Zgrada Centar", report);
        Assert.Equal("IZVJEŠTAJ O ODRŽAVANJU 2026 - Zgrada Centar", lines[0]);
        Assert.Contains("Planirano: 2   Realizovano: 1   Troškovi: 200,00 KM", lines);
        Assert.Contains("- Lift: 200,00 KM (1 posl.)", lines);
        Assert.Contains("- 10/2026: 1 / 1 / 200,00 KM", lines);
        Assert.Contains("10.12.2026 | Servis lifta (projekcija) | Lift | Planirano | - | -", lines);
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

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid SupplierId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "MR", Name = "Izvještaji" };
        var zev = new ZevEntity { Organization = organization, Code = "MR", Name = "Zgrada Centar" };
        var manager = new AppUser { Email = "manager@example.invalid", NormalizedEmail = "MANAGER@EXAMPLE.INVALID" };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Dobavljač d.o.o." };
        database.AddRange(organization, zev, manager, supplier,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, supplier.Id);
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
