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

// Phase B: work order dossier (from calendar or report), attachments, completion with expense, calendar filters, summary.
public sealed class WorkOrderDossierApiTests
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, .. new byte[300]];
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[300]];
    private static object Upload(string name, string type, byte[] content, string kind) =>
        new { fileName = name, contentType = type, contentBase64 = Convert.ToBase64String(content), attachmentType = kind };

    private static async Task<(Guid ScheduleId, string Prefix)> PlanDueTodayAsync(HttpClient manager, Guid zevId, string title, string category)
    {
        var prefix = $"/api/zevs/{zevId}/maintenance";
        await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/plans", new { title, category, frequencyDays = 30,
            firstDueOn = DateOnly.FromDateTime(DateTime.UtcNow) }), HttpStatusCode.Created);
        var calendar = await ReadAsync(await manager.GetAsync($"{prefix}/calendar"), HttpStatusCode.OK);
        var due = calendar.EnumerateArray().First(item => item.GetProperty("title").GetString() == title && item.GetProperty("status").GetString() == "Due");
        return (due.GetProperty("id").GetGuid(), prefix);
    }

    [Fact]
    public async Task WorkOrderFromCalendarIsADossierAndCompletionClosesTheOccurrenceAndBooksTheExpense()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var (scheduleId, prefix) = await PlanDueTodayAsync(manager, seed.ZevId, "Servis pumpe", "Vodovod");

        var created = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { scheduleId, title = "Servis pumpe",
            description = "Godišnji servis", priority = "Normal", contractorName = "Majstor d.o.o.", contractorPhone = "061 111 222" }), HttpStatusCode.Created);
        var orderId = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"{prefix}/work-orders", new { scheduleId, title = "Duplikat",
            description = "x", priority = "Normal" })).StatusCode);
        var calendar = await ReadAsync(await manager.GetAsync($"{prefix}/calendar?status=InProgress"), HttpStatusCode.OK);
        Assert.Equal(orderId, Assert.Single(calendar.EnumerateArray()).GetProperty("workOrderId").GetGuid());

        var attachments = $"{prefix}/work-orders/{orderId}/attachments";
        await ReadAsync(await manager.PostAsJsonAsync(attachments, Upload("racun.pdf", "application/pdf", Pdf, "Invoice")), HttpStatusCode.Created);
        var before = await ReadAsync(await manager.PostAsJsonAsync(attachments, Upload("prije.png", "image/png", Png, "PhotoBefore")), HttpStatusCode.Created);
        await ReadAsync(await manager.PostAsJsonAsync(attachments, Upload("poslije.png", "image/png", Png, "PhotoAfter")), HttpStatusCode.Created);
        var extra = await ReadAsync(await manager.PostAsJsonAsync(attachments, Upload("visak.png", "image/png", Png, "PhotoAfter")), HttpStatusCode.Created);
        foreach (var invalid in new[] { Upload("racun.docx", "application/pdf", Pdf, "Invoice"), Upload("racun.pdf", "application/pdf", Png, "Invoice"),
            Upload("slika.png", "image/png", Png, "Selfie"), Upload("velika.pdf", "application/pdf", [.. Pdf, .. new byte[10_000_000]], "Invoice") })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(attachments, invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{attachments}/{extra.GetProperty("id").GetGuid()}")).StatusCode);
        var download = await manager.GetAsync($"{attachments}/{before.GetProperty("id").GetGuid()}");
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync());

        var dossier = await ReadAsync(await manager.GetAsync($"{prefix}/work-orders/{orderId}"), HttpStatusCode.OK);
        Assert.Equal(["Invoice", "PhotoBefore", "PhotoAfter"], dossier.GetProperty("attachments").EnumerateArray().Select(item => item.GetProperty("attachmentType").GetString()));
        Assert.Equal("Majstor d.o.o.", dossier.GetProperty("contractorName").GetString());
        Assert.Equal(scheduleId, dossier.GetProperty("schedule").GetProperty("id").GetGuid());
        Assert.False(dossier.GetProperty("readOnly").GetBoolean());
        var rowVersion = dossier.GetProperty("rowVersion").GetString();
        object Complete(bool expense, Guid? supplier) => new { completedDate = DateOnly.FromDateTime(DateTime.UtcNow), workDescription = "Zamijenjen ležaj.",
            invoiceNumber = "R-15/2026", costAmount = 240.50m, expenseFund = "Regular", supplierId = supplier, recordExpense = expense, rowVersion };

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"{prefix}/work-orders/{orderId}/complete", Complete(true, null))).StatusCode);
        var done = await ReadAsync(await manager.PutAsJsonAsync($"{prefix}/work-orders/{orderId}/complete", Complete(true, seed.SupplierId)), HttpStatusCode.OK);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        var expenseId = done.GetProperty("expenseId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync($"{prefix}/work-orders/{orderId}/complete", Complete(false, null))).StatusCode);

        dossier = await ReadAsync(await manager.GetAsync($"{prefix}/work-orders/{orderId}"), HttpStatusCode.OK);
        Assert.True(dossier.GetProperty("readOnly").GetBoolean());
        Assert.Equal("Completed", dossier.GetProperty("schedule").GetProperty("status").GetString());
        Assert.Equal("Regular", dossier.GetProperty("expenseFund").GetString());
        Assert.Contains(dossier.GetProperty("history").EnumerateArray(), item => item.GetProperty("status").GetString() == "Completed");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(attachments, Upload("kasno.pdf", "application/pdf", Pdf, "Invoice"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync($"{attachments}/{before.GetProperty("id").GetGuid()}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        var expense = await database.Expenses.SingleAsync(item => item.Id == expenseId);
        Assert.Equal(240.50m, expense.Amount);
        Assert.Equal("R-15/2026", expense.InvoiceNumber);
        Assert.Equal("Vodovod", expense.Category);
        Assert.Equal(ExpenseStatus.Draft, expense.Status);
        Assert.Equal(seed.RegularFundId, expense.FundId);
        Assert.True(await database.AuditLogs.AnyAsync(item => item.Action == "WorkOrderCompleted" && item.EntityId == orderId.ToString()));
    }

    [Fact]
    public async Task WorkOrderFromReportMovesItToInProgressAndCompletionResolvesIt()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var ticket = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/tickets", new { propertyUnitId = (Guid?)null, category = "Rasvjeta",
            description = "Pregorjela sijalica", location = "Ulaz A", priority = "Normal", fileIds = Array.Empty<Guid>() }), HttpStatusCode.Created);
        var ticketId = ticket.GetProperty("id").GetGuid();
        var order = await ReadAsync(await manager.PostAsJsonAsync($"{prefix}/work-orders", new { ticketId, supplierId = seed.SupplierId,
            title = "Zamjena sijalice", description = "Ulaz A", priority = "Normal" }), HttpStatusCode.Created);
        string TicketStatus(JsonElement list) => list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == ticketId)
            .GetProperty("status").GetString()!;
        Assert.Equal("InProgress", TicketStatus(await ReadAsync(await manager.GetAsync($"{prefix}/tickets"), HttpStatusCode.OK)));
        var dossier = await ReadAsync(await manager.GetAsync($"{prefix}/work-orders/{order.GetProperty("id").GetGuid()}"), HttpStatusCode.OK);
        Assert.Equal("Dobavljač d.o.o.", dossier.GetProperty("supplierName").GetString());
        await ReadAsync(await manager.PutAsJsonAsync($"{prefix}/work-orders/{order.GetProperty("id").GetGuid()}/complete", new
        {
            completedDate = DateOnly.FromDateTime(DateTime.UtcNow), workDescription = "Zamijenjena sijalica.", recordExpense = false,
            rowVersion = dossier.GetProperty("rowVersion").GetString()
        }), HttpStatusCode.OK);
        Assert.Equal("Completed", TicketStatus(await ReadAsync(await manager.GetAsync($"{prefix}/tickets"), HttpStatusCode.OK)));
    }

    [Fact]
    public async Task CalendarFiltersPeriodStatusAndCategoryAndProjectsTheYear()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory);
        using var manager = Client(factory, seed.ManagerId);
        await PlanDueTodayAsync(manager, seed.ZevId, "Servis lifta", "Lift");
        await PlanDueTodayAsync(manager, seed.ZevId, "Čišćenje oluka", "Krov");
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var year = await ReadAsync(await manager.GetAsync($"{prefix}/calendar?from={today:yyyy-MM-dd}&to={today.AddDays(364):yyyy-MM-dd}"), HttpStatusCode.OK);
        var items = year.EnumerateArray().ToList();
        Assert.Equal(26, items.Count);
        Assert.Contains(items, item => item.GetProperty("projected").GetBoolean() && item.GetProperty("status").GetString() == "Planned"
            && DateOnly.Parse(item.GetProperty("dueOn").GetString()!) > today.AddDays(60));
        Assert.All(items.Where(item => item.GetProperty("projected").GetBoolean()), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("id").ValueKind));

        var lift = await ReadAsync(await manager.GetAsync($"{prefix}/calendar?from={today:yyyy-MM-dd}&to={today.AddDays(364):yyyy-MM-dd}&category=Lift"), HttpStatusCode.OK);
        Assert.All(lift.EnumerateArray(), item => Assert.Equal("Lift", item.GetProperty("category").GetString()));
        Assert.Equal(13, lift.GetArrayLength());
        var due = await ReadAsync(await manager.GetAsync($"{prefix}/calendar?status=Due"), HttpStatusCode.OK);
        Assert.Equal(2, due.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{prefix}/calendar?status=Hitno")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{prefix}/calendar?from={today:yyyy-MM-dd}&to={today.AddDays(400):yyyy-MM-dd}")).StatusCode);

        var summary = await ReadAsync(await manager.GetAsync($"{prefix}/dashboard-summary"), HttpStatusCode.OK);
        Assert.Equal(2, summary.GetProperty("due").GetInt32());
        Assert.Equal(0, summary.GetProperty("inProgress").GetInt32());
        Assert.Equal(0, summary.GetProperty("unreadTickets").GetInt32());
        Assert.Equal(today.AddDays(30), DateOnly.Parse(summary.GetProperty("nextDueOn").GetString()!));
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

    private sealed record Seed(Guid ZevId, Guid ManagerId, Guid SupplierId, Guid RegularFundId);

    private static async Task<Seed> SeedAsync(TestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        var organization = new Organization { Code = "WO", Name = "Nalozi" };
        var zev = new ZevEntity { Organization = organization, Code = "WO", Name = "ZEV" };
        var manager = new AppUser { Email = "manager@example.invalid", NormalizedEmail = "MANAGER@EXAMPLE.INVALID" };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Dobavljač d.o.o." };
        var regular = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
        var reserve = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Reserve, Name = "Rezervni" };
        database.AddRange(organization, zev, manager, supplier, regular, reserve,
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(zev.Id, manager.Id, supplier.Id, regular.Id);
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
