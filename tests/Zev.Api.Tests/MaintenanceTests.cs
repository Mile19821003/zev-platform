using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Maintenance;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class MaintenanceTests
{
    [Fact]
    public void StatusFlowRejectsSkippingCompletion()
    {
        Assert.True(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Open, MaintenanceStatus.InProgress));
        Assert.True(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Completed, MaintenanceStatus.Verified));
        Assert.False(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Open, MaintenanceStatus.Verified));
        Assert.False(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Verified, MaintenanceStatus.InProgress));
    }

    [Fact]
    public async Task ScheduleGenerationIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "Z", Name = "ZEV" };
        var today = MaintenanceScheduleService.Today();
        var plan = new MaintenancePlan { OrganizationId = organization.Id, ZevId = zev.Id,
            Title = "Lift", Category = "Lift", FirstDueOn = today.AddDays(1), FrequencyDays = 30 };
        database.AddRange(organization, zev, plan);
        await database.SaveChangesAsync();
        var service = new MaintenanceScheduleService(database);
        Assert.Equal(2, await service.EnsureUntilAsync(zev.Id, plan.Id, today.AddDays(40), CancellationToken.None));
        Assert.Equal(0, await service.EnsureUntilAsync(zev.Id, plan.Id, today.AddDays(40), CancellationToken.None));
        Assert.Equal(2, database.ScheduledMaintenances.Count());
    }

    [Fact]
    public async Task GenerationStaysWithinSixtyDaysAndStartsAtTheLastDueDate()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id, Organization = organization, Code = "Z", Name = "ZEV" };
        var today = MaintenanceScheduleService.Today();
        // Every 3 days since 100 days ago: one overdue occurrence (the latest due date), then the next 60 days only.
        var frequent = new MaintenancePlan { OrganizationId = organization.Id, ZevId = zev.Id, Title = "Čišćenje",
            Category = "Higijena", FirstDueOn = today.AddDays(-100), FrequencyDays = 3 };
        database.AddRange(organization, zev, frequent);
        await database.SaveChangesAsync();
        var service = new MaintenanceScheduleService(database);

        Assert.Equal(21, await service.EnsureUntilAsync(zev.Id, frequent.Id, today.AddDays(3660), CancellationToken.None));
        var dates = database.ScheduledMaintenances.Select(item => item.DueOn).OrderBy(item => item).ToList();
        Assert.Equal(today.AddDays(-1), dates[0]);
        Assert.True(dates[^1] <= today.AddDays(MaintenanceScheduleService.HorizonDays));
        Assert.All(dates.Zip(dates.Skip(1)), pair => Assert.Equal(3, pair.Second.DayNumber - pair.First.DayNumber));
        await service.EnsureHorizonAsync(zev.Id, CancellationToken.None);
        Assert.Equal(21, database.ScheduledMaintenances.Count());
    }

    [Fact]
    public void OpenOccurrenceIsPlannedBeforeItsDateAndDueFromIt()
    {
        var today = new DateOnly(2026, 10, 11);
        Assert.Equal("Planned", MaintenanceScheduleService.EffectiveStatus(ScheduleStatus.Due, today.AddDays(1), today));
        Assert.Equal("Due", MaintenanceScheduleService.EffectiveStatus(ScheduleStatus.Due, today, today));
        Assert.Equal("Due", MaintenanceScheduleService.EffectiveStatus(ScheduleStatus.Due, today.AddDays(-5), today));
        Assert.Equal("Completed", MaintenanceScheduleService.EffectiveStatus(ScheduleStatus.Completed, today.AddDays(9), today));
        Assert.Equal("InProgress", MaintenanceScheduleService.EffectiveStatus(ScheduleStatus.InProgress, today.AddDays(9), today));
        Assert.Equal(new DateOnly(2026, 10, 9), MaintenanceScheduleService.WindowStart(new DateOnly(2026, 10, 1), 4, today));
        Assert.Equal(today, MaintenanceScheduleService.WindowStart(new DateOnly(2026, 10, 1), 2, today));
        Assert.Equal(today.AddDays(5), MaintenanceScheduleService.WindowStart(today.AddDays(5), 2, today));
    }

    [Fact]
    public async Task OwnerCanReportOnlyOwnedUnitAndRepresentativeCannotReadFinances()
    {
        var accessor = new HttpContextAccessor();
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, accessor);
        await database.Database.EnsureCreatedAsync();
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "Z", Name = "ZEV" };
        var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Zev = zev,
            Code = "B", Name = "Building" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, Building = building, Code = "E", Name = "Entrance" };
        var owner = new AppUser { Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var representative = new AppUser { Email = "rep@example.test", NormalizedEmail = "REP@EXAMPLE.TEST" };
        var person = new Person { OrganizationId = organization.Id, Organization = organization,
            UserId = owner.Id, User = owner, UserLinkStatus = UserPersonLinkStatus.Verified,
            DisplayName = "Owner" };
        var owned = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 50m };
        var other = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance, Code = "2", AreaSquareMeters = 50m };
        database.AddRange(organization, zev, building, entrance, owner, representative, person, owned, other);
        database.Memberships.Add(new Membership { UserId = representative.Id, User = representative,
            OrganizationId = organization.Id, ZevId = zev.Id, Role = MembershipRole.BuildingRepresentative });
        database.Ownerships.Add(new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
            PropertyUnitId = owned.Id, PersonId = person.Id, Person = person,
            ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
        await database.SaveChangesAsync();

        SetUser(accessor, owner);
        var effectiveAccess = new EffectiveAccessService(database, new ConfigurationBuilder().Build());
        var ownerAccess = new TenantAccess(database, accessor, effectiveAccess);
        Assert.True(await ownerAccess.CanReportTicketAsync(zev.Id, owned.Id, CancellationToken.None));
        Assert.False(await ownerAccess.CanReportTicketAsync(zev.Id, other.Id, CancellationToken.None));
        Assert.False(await ownerAccess.CanManageMaintenanceAsync(zev.Id, CancellationToken.None));

        SetUser(accessor, representative);
        var representativeAccess = new TenantAccess(database, accessor, effectiveAccess);
        Assert.True(await representativeAccess.CanManageMaintenanceAsync(zev.Id, CancellationToken.None));
        Assert.False(await representativeAccess.CanManageFinancesAsync(zev.Id, CancellationToken.None));
    }

    private static void SetUser(HttpContextAccessor accessor, AppUser user) =>
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test"))
        };
}
