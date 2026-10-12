using System.Collections.Concurrent;
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
using Zev.Api.Notifications;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

// Fault reports by residents: own unit or common parts, e-mail to the notification address, new / read for the bell.
public sealed class TicketNotificationApiTests
{
    private static object Ticket(Guid? unitId, string location = "Ulaz A, stubište") => new
    {
        propertyUnitId = unitId, category = "Rasvjeta", description = "Ne radi svjetlo na drugom spratu.",
        location, priority = "Normal", fileIds = Array.Empty<Guid>()
    };

    [Fact]
    public async Task ResidentReportsCommonPartsAndAdministrationIsNotified()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory, "odrzavanje@zev.example");
        using var owner = Client(factory, seed.OwnerId);
        using var manager = Client(factory, seed.ManagerId);
        using var outsider = Client(factory, seed.OutsiderId);
        var prefix = $"/api/zevs/{seed.ZevId}/maintenance";

        await ReadAsync(await owner.PostAsJsonAsync($"{prefix}/tickets", Ticket(null)), HttpStatusCode.Created);
        var mail = await factory.Mail.WaitAsync();
        Assert.Equal("odrzavanje@zev.example", mail.To);
        Assert.Equal("Nova prijava kvara – Zgrada Centar: Rasvjeta", mail.Subject);
        Assert.Contains("Zajednički prostori – Ulaz A, stubište", mail.Body);
        // Priority as named in the MAINTENANCE_PRIORITY code list, not the enum code.
        Assert.Contains("Prioritet: Standardni", mail.Body);
        Assert.Contains("owner@example.invalid", mail.Body);
        Assert.Contains("Ne radi svjetlo na drugom spratu.", mail.Body);

        await ReadAsync(await owner.PostAsJsonAsync($"{prefix}/tickets", Ticket(seed.OwnUnitId, "Kupatilo")), HttpStatusCode.Created);
        Assert.Contains("Stan / posebni dio S1", (await factory.Mail.WaitAsync()).Body);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"{prefix}/tickets", Ticket(seed.OtherUnitId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"{prefix}/tickets", Ticket(null))).StatusCode);

        Assert.Equal(2, (await ReadAsync(await manager.GetAsync($"{prefix}/tickets/unread-count"), HttpStatusCode.OK)).GetProperty("unread").GetInt32());
        var tickets = await ReadAsync(await manager.GetAsync($"{prefix}/tickets"), HttpStatusCode.OK);
        var first = tickets.GetProperty("items")[0];
        Assert.False(first.GetProperty("isReadByAdmin").GetBoolean());
        var read = await ReadAsync(await manager.PostAsync($"{prefix}/tickets/{first.GetProperty("id").GetGuid()}/mark-as-read", null), HttpStatusCode.OK);
        Assert.True(read.GetProperty("isReadByAdmin").GetBoolean());
        await ReadAsync(await manager.PostAsync($"{prefix}/tickets/{first.GetProperty("id").GetGuid()}/mark-as-read", null), HttpStatusCode.OK);
        Assert.Equal(1, (await ReadAsync(await manager.GetAsync($"{prefix}/tickets/unread-count"), HttpStatusCode.OK)).GetProperty("unread").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{prefix}/tickets/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"{prefix}/tickets/{first.GetProperty("id").GetGuid()}/mark-as-read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{prefix}/tickets/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsync($"{prefix}/tickets/{Guid.NewGuid()}/mark-as-read", null)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var ticket = await scope.ServiceProvider.GetRequiredService<ZevDbContext>().MaintenanceTickets
            .SingleAsync(item => item.Id == first.GetProperty("id").GetGuid());
        Assert.Equal(seed.ManagerId, ticket.ReadByAdminUserId);
        Assert.NotNull(ticket.ReadByAdminAt);
    }

    [Fact]
    public async Task WithoutNotificationAddressTheReportIsSavedWithoutEmail()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory, null);
        using var owner = Client(factory, seed.OwnerId);
        await ReadAsync(await owner.PostAsJsonAsync($"/api/zevs/{seed.ZevId}/maintenance/tickets", Ticket(null)), HttpStatusCode.Created);
        await Task.Delay(300);
        Assert.Empty(factory.Mail.Messages);
    }

    [Fact]
    public async Task NotificationAddressIsASystemSetting()
    {
        using var factory = new TestFactory();
        var seed = await SeedAsync(factory, null);
        using var admin = Client(factory, seed.SystemAdminId);
        var current = await ReadAsync(await admin.GetAsync("/api/system/settings"), HttpStatusCode.OK);
        object Change(string? email) => new { decimalSeparator = ",", dateFormat = "DD.MM.YYYY", fontSize = "13px",
            defaultAdminEmail = email, updatedAt = current.GetProperty("updatedAt").GetDateTimeOffset() };
        Assert.Equal(JsonValueKind.Null, current.GetProperty("defaultAdminEmail").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/system/settings", Change("nije-email"))).StatusCode);
        var saved = await ReadAsync(await admin.PutAsJsonAsync("/api/system/settings", Change("  uprava@zev.example ")), HttpStatusCode.OK);
        Assert.Equal("uprava@zev.example", saved.GetProperty("defaultAdminEmail").GetString());
    }

    [Fact]
    public void MessageNamesTheZevUnitAndLink()
    {
        var ticket = new MaintenanceTicket { Category = "Lift", Description = "Lift stoji.", Location = "Ulaz B", Priority = MaintenancePriority.Urgent,
            CreatedUtc = new DateTimeOffset(2026, 10, 11, 8, 30, 0, TimeSpan.Zero) };
        var message = Zev.Api.Maintenance.TicketNotifications.Compose("a@b.c", "Zgrada", "Z1", "S4", "vlasnik@b.c", ticket, "https://zev.example");
        Assert.Equal("Nova prijava kvara – Zgrada: Lift", message.Subject);
        Assert.Contains("Prioritet: Hitno", message.Body);
        Assert.DoesNotContain("Urgent", message.Body);
        Assert.Contains("Prioritet: Vrlo hitno", Zev.Api.Maintenance.TicketNotifications.Compose("a@b.c", "Zgrada", "Z1", null, null,
            ticket, null, "Vrlo hitno").Body);
        Assert.Equal(["Nizak", "Standardni", "Visok", "Hitno"], Enum.GetValues<MaintenancePriority>()
            .Select(Zev.Api.Maintenance.TicketNotifications.PriorityLabel));
        Assert.Contains("Stan / posebni dio S4 – Ulaz B", message.Body);
        Assert.Contains("11.10.2026 08:30 UTC", message.Body);
        Assert.EndsWith("https://zev.example", message.Body);
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

    private sealed record Seed(Guid ZevId, Guid OwnerId, Guid ManagerId, Guid OutsiderId, Guid SystemAdminId, Guid OwnUnitId, Guid OtherUnitId);

    private static async Task<Seed> SeedAsync(TestFactory factory, string? notificationAddress)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        database.Database.EnsureCreated();
        (await database.SystemSettings.SingleAsync()).DefaultAdminEmail = notificationAddress;
        var organization = new Organization { Code = "TN", Name = "Prijave" };
        var zev = new ZevEntity { Organization = organization, Code = "ZC", Name = "Zgrada Centar" };
        var otherZev = new ZevEntity { Organization = organization, Code = "ZS", Name = "Zgrada Sjever" };
        AppUser User(string name) => new() { Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@EXAMPLE.INVALID".ToUpperInvariant() };
        var owner = User("owner"); var manager = User("manager"); var outsider = User("outsider"); var admin = User("sysadmin");
        admin.IsSystemAdministrator = true;
        var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Code = "B", Name = "Zgrada" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id, Code = "A", Name = "Ulaz A" };
        PropertyUnit Unit(ZevEntity owner2, Building b, Entrance e, string code) => new() { OrganizationId = organization.Id, ZevId = owner2.Id,
            BuildingId = b.Id, EntranceId = e.Id, Code = code, Type = PropertyUnitType.Apartment, AreaSquareMeters = 50 };
        var ownUnit = Unit(zev, building, entrance, "S1");
        var otherUnit = Unit(zev, building, entrance, "S2");
        var otherBuilding = new Building { OrganizationId = organization.Id, ZevId = otherZev.Id, Code = "B", Name = "Druga" };
        var otherEntrance = new Entrance { OrganizationId = organization.Id, ZevId = otherZev.Id, BuildingId = otherBuilding.Id, Code = "A", Name = "Ulaz" };
        var outsiderUnit = Unit(otherZev, otherBuilding, otherEntrance, "X1");
        Person Resident(AppUser user) => new() { OrganizationId = organization.Id, UserId = user.Id, Kind = PersonKind.Individual,
            DisplayName = user.Email, UserLinkStatus = UserPersonLinkStatus.Verified };
        var ownerPerson = Resident(owner); var outsiderPerson = Resident(outsider);
        database.AddRange(organization, zev, otherZev, owner, manager, outsider, admin, building, entrance, ownUnit, otherUnit,
            otherBuilding, otherEntrance, outsiderUnit, ownerPerson, outsiderPerson,
            new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnitId = ownUnit.Id, PersonId = ownerPerson.Id,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1 },
            new Ownership { OrganizationId = organization.Id, ZevId = otherZev.Id, PropertyUnitId = outsiderUnit.Id, PersonId = outsiderPerson.Id,
                ValidFrom = new DateOnly(2026, 1, 1), Share = 1 },
            new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
        await database.SaveChangesAsync();
        return new(zev.Id, owner.Id, manager.Id, outsider.Id, admin.Id, ownUnit.Id, otherUnit.Id);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Messages { get; } = new();
        private readonly SemaphoreSlim arrived = new(0);

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            arrived.Release();
            return Task.FromResult(true);
        }

        public async Task<EmailMessage> WaitAsync()
        {
            Assert.True(await arrived.WaitAsync(TimeSpan.FromSeconds(10)), "no e-mail was sent");
            return Messages.Last();
        }
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();
        public CapturingEmailSender Mail { get; } = new();

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
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Mail);
            });
        }
    }
}
